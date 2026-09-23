using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace CourtBook.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingOverlapTrigger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Enforces database-level protection against overlapping bookings for the same court.
            // Blocking statuses: all except Cancelled (Pending, Confirmed, Completed, NoShow).
            // This is the backstop that catches races which pass the application-level check.
            // Matches BookingService.CreateAsync overlap predicate exactly:
            //   b.Status != 'Cancelled' && b.StartTime < @end && b.EndTime > @start
            migrationBuilder.Sql(@"
                CREATE TRIGGER TRG_Booking_NoOverlap
                ON dbo.Bookings
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF UPDATE(Status) OR UPDATE(StartTime) OR UPDATE(EndTime) OR UPDATE(CourtId)
                    BEGIN
                        DECLARE @ConflictCount INT;

                        SELECT @ConflictCount = COUNT(*)
                        FROM dbo.Bookings AS b
                        INNER JOIN inserted AS i ON b.CourtId = i.CourtId
                            AND b.Id <> i.Id
                            AND b.Status <> 'Cancelled'
                            AND i.Status <> 'Cancelled'
                            AND b.StartTime < i.EndTime
                            AND b.EndTime > i.StartTime;

                        IF @ConflictCount > 0
                        BEGIN
                            THROW 50001, N'Court is not available for the selected time slot. The selected time overlaps with an existing booking.', 1;
                        END
                    END
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
