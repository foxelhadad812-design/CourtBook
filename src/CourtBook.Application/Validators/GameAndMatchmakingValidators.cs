using CourtBook.Application.DTOs;
using CourtBook.Domain.Enums;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class CreateGameRequestValidator : AbstractValidator<CreateGameRequest>
{
    private static readonly string[] ValidSports = ["Football", "Padel", "Tennis", "Basketball", "Volleyball", "Badminton"];
    private static readonly string[] ValidSkillLevels = ["AllLevels", "Beginner", "Intermediate", "Advanced"];
    private static readonly string[] ValidAgeGroups = ["AllAges", "Kids", "Juniors", "Teens", "Adults", "Custom"];

    public CreateGameRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Game title is required.")
            .MinimumLength(3).WithMessage("Game title must be at least 3 characters.")
            .MaximumLength(150).WithMessage("Game title cannot exceed 150 characters.");

        RuleFor(x => x.SportType)
            .NotEmpty().WithMessage("Sport type is required.")
            .Must(s => ValidSports.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sport must be one of: {string.Join(", ", ValidSports)}.");

        RuleFor(x => x.VenueId)
            .NotEmpty().WithMessage("Venue ID is required.");

        RuleFor(x => x.CourtId)
            .NotEmpty().WithMessage("Court ID is required.");

        RuleFor(x => x.Date)
            .NotEmpty().WithMessage("Game date is required.")
            .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)))
            .WithMessage("Game date cannot be in the past.");

        RuleFor(x => x.StartTime)
            .NotEmpty().WithMessage("Start time is required.")
            .Matches(@"^\d{2}:\d{2}$").WithMessage("Start time must be in HH:mm format.");

        RuleFor(x => x.EndTime)
            .NotEmpty().WithMessage("End time is required.")
            .Matches(@"^\d{2}:\d{2}$").WithMessage("End time must be in HH:mm format.");

        RuleFor(x => x.SkillLevel)
            .Must(s => string.IsNullOrWhiteSpace(s) || ValidSkillLevels.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Skill level must be one of: {string.Join(", ", ValidSkillLevels)}.");

        RuleFor(x => x.AgeGroup)
            .Must(a => string.IsNullOrWhiteSpace(a) || ValidAgeGroups.Contains(a, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Age group must be one of: {string.Join(", ", ValidAgeGroups)}.");

        RuleFor(x => x.MaxPlayers)
            .InclusiveBetween(2, 50).WithMessage("Max players must be between 2 and 50.");

        RuleFor(x => x.MinPlayers)
            .InclusiveBetween(2, 50).WithMessage("Min players must be between 2 and 50.")
            .LessThanOrEqualTo(x => x.MaxPlayers).WithMessage("Min players cannot exceed max players.");

        RuleFor(x => x.PricePerPlayer)
            .GreaterThanOrEqualTo(0).WithMessage("Price per player cannot be negative.")
            .LessThan(10_000).WithMessage("Price per player seems unreasonably high.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.");

        RuleFor(x => x.AccessCode)
            .MaximumLength(32).WithMessage("Access code cannot exceed 32 characters.");
    }
}

public class JoinGameRequestValidator : AbstractValidator<JoinGameRequest>
{
    public JoinGameRequestValidator()
    {
        RuleFor(x => x.AccessCode)
            .MaximumLength(32).WithMessage("Access code cannot exceed 32 characters.");
    }
}

public class UpsertPlayerSportSkillRequestValidator : AbstractValidator<UpsertPlayerSportSkillRequest>
{
    private static readonly string[] ValidSports = ["Football", "Padel", "Tennis", "Basketball", "Volleyball", "Badminton"];
    private static readonly string[] ValidSkillLevels = ["Beginner", "Intermediate", "Advanced"];

    public UpsertPlayerSportSkillRequestValidator()
    {
        RuleFor(x => x.SportType)
            .NotEmpty().WithMessage("Sport type is required.")
            .Must(s => ValidSports.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sport must be one of: {string.Join(", ", ValidSports)}.");

        RuleFor(x => x.SkillLevel)
            .Must(s => string.IsNullOrWhiteSpace(s) || ValidSkillLevels.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Skill level must be one of: {string.Join(", ", ValidSkillLevels)}.");

        RuleFor(x => x.SkillScore)
            .InclusiveBetween(500, 3000)
            .When(x => x.SkillScore.HasValue)
            .WithMessage("Skill score rating must be between 500 and 3000.");
    }
}

public class UpdatePlayerPreferenceRequestValidator : AbstractValidator<UpdatePlayerPreferenceRequest>
{
    public UpdatePlayerPreferenceRequestValidator()
    {
        RuleFor(x => x.MaxDistanceKm)
            .InclusiveBetween(1, 500)
            .When(x => x.MaxDistanceKm.HasValue)
            .WithMessage("Max distance must be between 1 and 500 km.");

        RuleFor(x => x.PreferredGameType)
            .MaximumLength(200).WithMessage("Preferred game type cannot exceed 200 characters.");
    }
}

public class AssignTeamRequestValidator : AbstractValidator<AssignTeamRequest>
{
    public AssignTeamRequestValidator()
    {
        RuleFor(x => x.ParticipantUserId)
            .NotEmpty().WithMessage("Participant User ID is required.");

        RuleFor(x => x.Team)
            .Must(t => string.IsNullOrWhiteSpace(t) || t == "TeamA" || t == "TeamB")
            .WithMessage("Team must be 'TeamA', 'TeamB', or null.");
    }
}
