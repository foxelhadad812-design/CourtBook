using CourtBook.Application.DTOs;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class CreateCourtRequestValidator : AbstractValidator<CreateCourtRequest>
{
    private static readonly string[] ValidSports = ["Football", "Padel", "Tennis", "Basketball", "Volleyball", "Badminton"];

    public CreateCourtRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Court name is required.")
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(x => x.SportType)
            .NotEmpty().WithMessage("Sport type is required.")
            .Must(s => ValidSports.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sport must be one of: {string.Join(", ", ValidSports)}.");

        RuleFor(x => x.PricePerHour)
            .GreaterThan(0).WithMessage("Price per hour must be greater than 0.")
            .LessThan(10_000).WithMessage("Price per hour seems unreasonably high.");

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Court capacity must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Court capacity seems unreasonably high.");
    }
}

public class UpdateCourtRequestValidator : AbstractValidator<UpdateCourtRequest>
{
    private static readonly string[] ValidSports = ["Football", "Padel", "Tennis", "Basketball", "Volleyball", "Badminton"];

    public UpdateCourtRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Court name is required.")
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(x => x.SportType)
            .NotEmpty()
            .Must(s => ValidSports.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sport must be one of: {string.Join(", ", ValidSports)}.");

        RuleFor(x => x.PricePerHour)
            .GreaterThan(0)
            .LessThan(10_000);

        RuleFor(x => x.Capacity)
            .GreaterThan(0).WithMessage("Court capacity must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("Court capacity seems unreasonably high.");
    }
}

public class CreateBookingRequestValidator : AbstractValidator<CreateBookingRequest>
{
    public CreateBookingRequestValidator()
    {
        RuleFor(x => x.CourtId)
            .NotEmpty().WithMessage("Court ID is required.");

        RuleFor(x => x.StartTime)
            .NotEmpty().WithMessage("Start time is required.")
            .GreaterThan(DateTime.UtcNow.AddMinutes(-5))
            .WithMessage("Cannot book a slot in the past.");

        RuleFor(x => x.EndTime)
            .NotEmpty().WithMessage("End time is required.")
            .GreaterThan(x => x.StartTime)
            .WithMessage("End time must be after start time.");

        RuleFor(x => x)
            .Must(x => (x.EndTime - x.StartTime).TotalMinutes >= 30)
            .WithMessage("Minimum booking duration is 30 minutes.")
            .Must(x => (x.EndTime - x.StartTime).TotalHours <= 12)
            .WithMessage("Maximum booking duration is 12 hours.");
    }
}

public class CreateScheduleRequestValidator : AbstractValidator<CreateScheduleRequest>
{
    public CreateScheduleRequestValidator()
    {
        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween(0, 6).WithMessage("Day of week must be 0 (Sunday) to 6 (Saturday).");

        RuleFor(x => x.OpenTime)
            .NotEmpty().WithMessage("Open time is required.")
            .Matches(@"^\d{2}:\d{2}$").WithMessage("Open time must be in HH:mm format.");

        RuleFor(x => x.CloseTime)
            .NotEmpty().WithMessage("Close time is required.")
            .Matches(@"^\d{2}:\d{2}$").WithMessage("Close time must be in HH:mm format.");
    }
}
