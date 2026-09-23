using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBookingTriggerForPhase12 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE OR ALTER TRIGGER TRG_Booking_NoOverlap
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

                    CREATE TABLE #LockCourts (
                        RowIdx INT IDENTITY(1,1) PRIMARY KEY,
                        CourtId UNIQUEIDENTIFIER NOT NULL
                    );

                    IF NOT UPDATE(Id)
                    BEGIN
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM inserted ORDER BY CourtId;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM deleted
                        UNION
                        SELECT DISTINCT CourtId FROM inserted
                        ORDER BY CourtId;
                    END

                    SELECT @Cursor = COUNT(*) FROM #LockCourts;

                    IF @Cursor = 0
                    BEGIN
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
                            @LockTimeout = 0;

                        SET @ErrorCode = @@ERROR;
                        IF @ErrorCode <> 0 OR @LockResult < 0
                        BEGIN
                            DROP TABLE #LockCourts;
                            THROW 50001, N'Court is not available for the selected time slot. Another transaction is in progress.', 1;
                        END

                        SET @Idx = @Idx + 1;
                    END

                    -- Overlap check against existing bookings
                    SELECT @ConflictCount = COUNT(*)
                    FROM inserted AS i
                    INNER JOIN dbo.Bookings AS b ON b.CourtId = i.CourtId
                        AND b.Id <> i.Id
                        AND b.Status <> 'Cancelled'
                        AND i.Status <> 'Cancelled'
                        AND b.StartTime < i.EndTime
                        AND b.EndTime > i.StartTime;

                    -- Overlap check within inserted set
                    SELECT @ConflictCount = @ConflictCount + COUNT(*)
                    FROM inserted AS i1
                    INNER JOIN inserted AS i2
                        ON i1.CourtId = i2.CourtId
                        AND i1.Id <> i2.Id
                        AND i1.Status <> 'Cancelled'
                        AND i2.Status <> 'Cancelled'
                        AND i1.StartTime < i2.EndTime
                        AND i1.EndTime > i2.StartTime;

                    IF @ConflictCount > 0
                    BEGIN
                        DROP TABLE #LockCourts;
                        THROW 50001, N'Court is not available for the selected time slot. The selected time overlaps with an existing booking.', 1;
                    END

                    IF EXISTS (SELECT 1 FROM deleted)
                    BEGIN
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
                            b.Notes           = i.Notes,
                            b.IsManualBooking = i.IsManualBooking,
                            b.CustomerName    = i.CustomerName,
                            b.CustomerPhone   = i.CustomerPhone,
                            b.IsCheckedIn     = i.IsCheckedIn,
                            b.CheckedInAt     = i.CheckedInAt,
                            b.PromoCodeId     = i.PromoCodeId,
                            b.DiscountAmount  = i.DiscountAmount
                        FROM dbo.Bookings AS b
                        INNER JOIN inserted AS i ON b.Id = i.Id;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO dbo.Bookings (
                            Id, CourtId, UserId, StartTime, EndTime, Status,
                            PaymentStatus, TotalPrice, CancellationFee,
                            CancellationReason, CancelledAt, Notes,
                            BookingReference, CreatedAt,
                            IsManualBooking, CustomerName, CustomerPhone,
                            IsCheckedIn, CheckedInAt, PromoCodeId, DiscountAmount
                        )
                        SELECT
                            i.Id, i.CourtId, i.UserId, i.StartTime, i.EndTime, i.Status,
                            i.PaymentStatus, i.TotalPrice, i.CancellationFee,
                            i.CancellationReason, i.CancelledAt, i.Notes,
                            i.BookingReference, i.CreatedAt,
                            i.IsManualBooking, i.CustomerName, i.CustomerPhone,
                            i.IsCheckedIn, i.CheckedInAt, i.PromoCodeId, i.DiscountAmount
                        FROM inserted AS i;
                    END

                    DROP TABLE #LockCourts;
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert trigger to pre-Phase 12 definition
            migrationBuilder.Sql(@"
                CREATE OR ALTER TRIGGER TRG_Booking_NoOverlap
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

                    CREATE TABLE #LockCourts (
                        RowIdx INT IDENTITY(1,1) PRIMARY KEY,
                        CourtId UNIQUEIDENTIFIER NOT NULL
                    );

                    IF NOT UPDATE(Id)
                    BEGIN
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM inserted ORDER BY CourtId;
                    END
                    ELSE
                    BEGIN
                        INSERT INTO #LockCourts (CourtId)
                        SELECT DISTINCT CourtId FROM deleted
                        UNION
                        SELECT DISTINCT CourtId FROM inserted
                        ORDER BY CourtId;
                    END

                    SELECT @Cursor = COUNT(*) FROM #LockCourts;

                    IF @Cursor = 0
                    BEGIN
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
                            @LockTimeout = 0;

                        SET @ErrorCode = @@ERROR;
                        IF @ErrorCode <> 0 OR @LockResult < 0
                        BEGIN
                            DROP TABLE #LockCourts;
                            THROW 50001, N'Court is not available for the selected time slot. Another transaction is in progress.', 1;
                        END

                        SET @Idx = @Idx + 1;
                    END

                    SELECT @ConflictCount = COUNT(*)
                    FROM inserted AS i
                    INNER JOIN dbo.Bookings AS b ON b.CourtId = i.CourtId
                        AND b.Id <> i.Id
                        AND b.Status <> 'Cancelled'
                        AND i.Status <> 'Cancelled'
                        AND b.StartTime < i.EndTime
                        AND b.EndTime > i.StartTime;

                    SELECT @ConflictCount = @ConflictCount + COUNT(*)
                    FROM inserted AS i1
                    INNER JOIN inserted AS i2
                        ON i1.CourtId = i2.CourtId
                        AND i1.Id <> i2.Id
                        AND i1.Status <> 'Cancelled'
                        AND i2.Status <> 'Cancelled'
                        AND i1.StartTime < i2.EndTime
                        AND i1.EndTime > i2.StartTime;

                    IF @ConflictCount > 0
                    BEGIN
                        DROP TABLE #LockCourts;
                        THROW 50001, N'Court is not available for the selected time slot. The selected time overlaps with an existing booking.', 1;
                    END

                    IF EXISTS (SELECT 1 FROM deleted)
                    BEGIN
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
    }
}
