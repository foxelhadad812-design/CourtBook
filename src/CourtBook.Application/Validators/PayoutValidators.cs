using System.Text.RegularExpressions;
using CourtBook.Application.DTOs;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class CreatePayoutMethodRequestValidator : AbstractValidator<CreatePayoutMethodRequest>
{
    private static readonly Regex EgyptianIbanRegex = new(@"^EG\d{27}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EgyptianMobileRegex = new(@"^01[0125]\d{8}$", RegexOptions.Compiled);
    private static readonly Regex InstaPayRegex = new(@"^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+$", RegexOptions.Compiled);

    public CreatePayoutMethodRequestValidator()
    {
        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Payout method type is required.")
            .Must(t => t.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase) ||
                       t.Equals("InstaPay", StringComparison.OrdinalIgnoreCase) ||
                       t.Equals("MobileWallet", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Payout method type must be BankTransfer, InstaPay, or MobileWallet.");

        RuleFor(x => x.AccountHolderName)
            .NotEmpty().WithMessage("Account holder name is required.")
            .MaximumLength(100).WithMessage("Account holder name cannot exceed 100 characters.");

        When(x => x.Type.Equals("BankTransfer", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.BankName)
                .NotEmpty().WithMessage("Bank name is required for bank transfer.")
                .MaximumLength(100).WithMessage("Bank name cannot exceed 100 characters.");

            RuleFor(x => x.Iban)
                .NotEmpty().WithMessage("IBAN is required for bank transfer.")
                .Must(iban => !string.IsNullOrWhiteSpace(iban) && EgyptianIbanRegex.IsMatch(iban.Replace(" ", "")))
                .WithMessage("Valid Egyptian IBAN is required (format: EG followed by 27 digits).");
        });

        When(x => x.Type.Equals("InstaPay", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.InstaPayAddress)
                .NotEmpty().WithMessage("InstaPay address is required.")
                .Must(ipa => !string.IsNullOrWhiteSpace(ipa) && (ipa.EndsWith("@instapay", StringComparison.OrdinalIgnoreCase) || InstaPayRegex.IsMatch(ipa)))
                .WithMessage("Valid InstaPay address is required (e.g. username@instapay).");
        });

        When(x => x.Type.Equals("MobileWallet", StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(x => x.MobileWalletNumber)
                .NotEmpty().WithMessage("Mobile wallet number is required.")
                .Must(num => !string.IsNullOrWhiteSpace(num) && EgyptianMobileRegex.IsMatch(num.Replace(" ", "").Replace("-", "")))
                .WithMessage("Valid Egyptian mobile wallet number is required (010, 011, 012, or 015 followed by 8 digits).");
        });
    }
}

public class CreatePayoutRequestValidator : AbstractValidator<CreatePayoutRequest>
{
    public CreatePayoutRequestValidator()
    {
        RuleFor(x => x.PayoutMethodId)
            .NotEmpty().WithMessage("Payout destination method is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Payout amount must be greater than zero.")
            .GreaterThanOrEqualTo(100m).WithMessage("Minimum payout withdrawal is EGP 100.00.");
    }
}

public class MarkPayoutPaidRequestValidator : AbstractValidator<MarkPayoutPaidRequest>
{
    public MarkPayoutPaidRequestValidator()
    {
        RuleFor(x => x.ExternalTransactionReference)
            .NotEmpty().WithMessage("External transaction reference is mandatory for disbursement confirmation.")
            .MaximumLength(200).WithMessage("External reference cannot exceed 200 characters.");

        RuleFor(x => x.DisbursementNote)
            .MaximumLength(500).WithMessage("Disbursement note cannot exceed 500 characters.");
    }
}
