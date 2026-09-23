using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingCourtLockResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============================================================================
            // BOOKING OVERLAP ENFORCEMENT — sp_getapplock-based serialization
            //
            // Two-layer defense-in-depth:
            //   Layer 1 (application): BookingService acquires sp_getapplock before the
            //       overlap check + INSERT. Fast, friendly, handles the common case.
            //   Layer 2 (database):   This INSTEAD OF trigger ALSO acquires sp_getapplock
            //       per CourtId and performs the overlap check. This is the final
            //       backstop — even if the application layer is bypassed, the database
            //       refuses overlapping writes.
            //
            // Lock resource: CourtBook:Booking:Court:{CourtId}
            //   - Same Court => same lock => serialized
            //   - Different Courts => different locks => concurrent execution safe
            //
            // Blocking statuses (non-Cancelled): Pending, Confirmed, Completed, NoShow
            // Only Cancelled bookings free the slot.
            //
            // Multi-row / multi-court safety:
            //   - INSERT: locks ALL distinct CourtIds in inserted, sorted by CourtId
            //   - UPDATE: locks ALL distinct CourtIds from both deleted AND inserted,
            //     sorted by CourtId (deterministic order to reduce deadlock risk)
            //
            // For UPDATEs changing CourtId:
            //   - The OLD Court is locked (from deleted)
            //   - The NEW Court is locked (from inserted)
            //   - Overlap checks run against the NEW CourtId (inserted)
            //   - This prevents: concurrent booking on old court, concurrent booking
            //     on new court, and overlap with either court's existing bookings
            // ============================================================================

            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'dbo.TRG_Booking_NoOverlap', N'TR') IS NOT NULL
                    DROP TRIGGER dbo.TRG_Booking_NoOverlap;
            ");

            migrationBuilder.Sql(@"
                CREATE TRIGGER TRG_Booking_NoOverlap
                ON dbo.Bookings
                INSTEAD OF INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    SET XACT_ABORT ON;

                    DECLARE @ResourceName NVARCHAR(256);
                    DECLARE @LockResult INT;
                    DECLARE @ErrorCode INT;
                    DECLARE @ConflictCount INT;
                    DECLARE @CourtId UNIQUEIDENTIFIER;
                    DECLARE @Cursor INT;

                    -- ========================================================================
                    -- STEP 1: Determine ALL distinct CourtIds involved and lock them
                    --         in deterministic (sorted) order to minimize deadlock risk.
                    --
                    -- For INSERT: all CourtIds from inserted.
                    -- For UPDATE: union of CourtIds from deleted (old) and inserted (new).
                    -- ========================================================================

                    -- Use a temp table to collect distinct CourtIds with sort order
                    CREATE TABLE #LockCourts (
                        RowIdx INT IDENTITY(1,1) PRIMARY KEY,
                        CourtId UNIQUEIDENTIFIER NOT NULL
                    );

                    -- INSERT case: lock all distinct CourtIds from inserted
                    IF NOT UPDATE(Id)
                    BEGIN
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM inserted ORDER BY CourtId;
                    END
                    ELSE
                    BEGIN
                        -- UPDATE case: union of old (deleted) and new (inserted) CourtIds
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM deleted
                        UNION
                        SELECT DISTINCT CourtId FROM inserted
                        ORDER BY CourtId;
                    END

                    -- Acquire locks in sorted order (RowIdx order)
                    SELECT @Cursor = COUNT(*) FROM #LockCourts;

                    IF @Cursor = 0
                    BEGIN
                        -- No CourtId in statement — bail (should not happen)
                        DROP TABLE #LockCourts;
                        RETURN;
                    END

                    DECLARE @Idx INT = 1;
                    WHILE @Idx <= @Cursor
                    BEGIN
                        SELECT @CourtId = CourtId FROM #LockCourts WHERE RowIdx = @Idx;

                        SET @ResourceName = N'CourtBook:Booking:Court:' + CAST(@CourtId AS NVARCHAR(36));

                        EXEC @LockResult = sp_getapplock
                            @Resource = @ResourceName,
                            @LockMode = 'Exclusive',
                            @LockOwner = 'Transaction',
                            @LockTimeout = 0;  -- Non-blocking: fail immediately if held

                        SET @ErrorCode = @@ERROR;
                        IF @ErrorCode <> 0 OR @LockResult < 0
                        BEGIN
                            -- Lock not acquired for this Court — another transaction
                            -- is operating on it. Surface friendly error.
                            DROP TABLE #LockCourts;
                            THROW 50001, N'Court is not available for the selected time slot. Another transaction is in progress.', 1;
                        END

                        SET @Idx = @Idx + 1;
                    END

                    -- ========================================================================
                    -- STEP 2: Overlap check — all locks held.
                    --
                    -- Check overlaps of NEW rows (inserted) against existing bookings.
                    -- For UPDATE, use the NEW CourtId (inserted.CourtId) — this is correct
                    -- because the booking is moving TO the new Court and must not conflict
                    -- with existing bookings there. Also check against the OLD Court to
                    -- prevent a race where a concurrent booking is created on the old Court
                    -- at the same time.
                    -- ========================================================================

                    -- Check inserted rows against existing bookings
                    SELECT @ConflictCount = COUNT(*)
                    FROM inserted AS i
                    INNER JOIN dbo.Bookings AS b ON b.CourtId = i.CourtId
                        AND b.Id <> i.Id
                        AND b.Status <> 'Cancelled'
                        AND i.Status <> 'Cancelled'
                        AND b.StartTime < i.EndTime
                        AND b.EndTime > i.StartTime;

                    -- Check for conflicts WITHIN the inserted set (multi-row insert with mutual overlaps)
                    SELECT @ConflictCount = @ConflictCount + COUNT(*)
                    FROM inserted AS i1
                    INNER JOIN inserted AS i2
                        ON i1.CourtId = i2.CourtId
                        AND i1.Id <> i2.Id
                        AND i1.Status <> 'Cancelled'
                        AND i2.Status <> 'Cancelled'
                        AND i1.StartTime < i2.EndTime
                        AND i1.EndTime > i2.StartTime;

                    -- For UPDATEs: also check that the NEW position doesn't overlap with
                    -- existing bookings. (The check above already covers this because
                    -- we JOIN on b.CourtId = i.CourtId, where i.CourtId is the NEW value.)
                    --
                    -- Also check: the OLD court (from deleted) must not have a gap created
                    -- by the move that would allow an overlapping booking to slip in.
                    -- Since we locked BOTH old and new courts, this is protected.

                    IF @ConflictCount > 0
                    BEGIN
                        DROP TABLE #LockCourts;
                        THROW 50001, N'Court is not available for the selected time slot. The selected time overlaps with an existing booking.', 1;
                    END

                    -- ========================================================================
                    -- STEP 3: Perform the actual DML.
                    --
                    -- For UPDATE: preserve immutable columns (BookingReference, CreatedAt).
                    --             Only update mutable columns from inserted.
                    -- For INSERT: insert all columns from inserted.
                    -- ========================================================================

                    IF EXISTS (SELECT 1 FROM deleted)
                    BEGIN
                        -- UPDATE: update only mutable columns. Immutable columns
                        -- (BookingReference, CreatedAt) are preserved from the
                        -- existing row and MUST NOT be overwritten.
                        UPDATE b
                        SET b.CourtId         = i.CourtId,
                            b.UserId          = i.UserId,
                            b.StartTime       = i.StartTime,
                            b.EndTime         = i.EndTime,
                            b.Status          = i.Status,
                            b.PaymentStatus   = i.PaymentStatus,
                            b.TotalPrice      = i.TotalPrice,
                            b.CancellationFee = i.CancellationFee,
                            b.CancellationReason = i.CancellationReason,
                            b.CancelledAt     = i.CancelledAt,
                            b.Notes           = i.Notes
                        FROM dbo.Bookings AS b
                        INNER JOIN inserted AS i ON b.Id = i.Id;
                    END
                    ELSE
                    BEGIN
                        -- INSERT: insert all rows
                        INSERT INTO dbo.Bookings (
                            Id, CourtId, UserId, StartTime, EndTime, Status,
                            PaymentStatus, TotalPrice, CancellationFee,
                            CancellationReason, CancelledAt, Notes,
                            BookingReference, CreatedAt
                        )
                        SELECT
                            i.Id, i.CourtId, i.UserId, i.StartTime, i.EndTime, i.Status,
                            i.PaymentStatus, i.TotalPrice, i.CancellationFee,
                            i.CancellationReason, i.CancelledAt, i.Notes,
                            i.BookingReference, i.CreatedAt
                        FROM inserted AS i;
                    END

                    DROP TABLE #LockCourts;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF OBJECT_ID(N'dbo.TRG_Booking_NoOverlap', N'TR') IS NOT NULL
                    DROP TRIGGER dbo.TRG_Booking_NoOverlap;
            ");
        }
    }
}
