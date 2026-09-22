using CourtBook.Application.DTOs;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class CreateInvitationRequestValidator : AbstractValidator<CreateInvitationRequest>
{
    public CreateInvitationRequestValidator()
    {
        RuleFor(x => x.InviteeId)
            .NotEmpty().WithMessage("Invitee User ID is required.");

        RuleFor(x => x.Message)
            .MaximumLength(500).WithMessage("Invitation message cannot exceed 500 characters.");
    }
}

public class SendConnectionRequestValidator : AbstractValidator<SendConnectionRequest>
{
    public SendConnectionRequestValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty().WithMessage("Target User ID is required.");
    }
}

public class BlockUserRequestValidator : AbstractValidator<BlockUserRequest>
{
    public BlockUserRequestValidator()
    {
        RuleFor(x => x.TargetUserId)
            .NotEmpty().WithMessage("Target User ID to block is required.");
    }
}

public class GameHistoryFilterRequestValidator : AbstractValidator<GameHistoryFilterRequest>
{
    private static readonly string[] ValidSports = ["Football", "Padel", "Tennis", "Basketball", "Volleyball", "Badminton"];

    public GameHistoryFilterRequestValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page number must be at least 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100).WithMessage("Page size must be between 1 and 100.");

        RuleFor(x => x.SportType)
            .Must(s => string.IsNullOrWhiteSpace(s) || ValidSports.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sport must be one of: {string.Join(", ", ValidSports)}.");
    }
}
