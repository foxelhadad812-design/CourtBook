using CourtBook.Application.DTOs;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(6).WithMessage("Password must be at least 6 characters.");
    }
}

public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Full name is required.")
            .MinimumLength(2).WithMessage("Name must be at least 2 characters.")
            .MaximumLength(100).WithMessage("Name must not exceed 100 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one number.")
            .Matches(@"[!@#$%^&*(),.?""':{}|<>\-_=+\\/\[\]~]").WithMessage("Password must contain at least one special symbol (e.g. @, #, $, !).");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^(\+?\d{7,15})$").WithMessage("Please enter a valid phone number.");

        When(x => x.DateOfBirth.HasValue, () =>
        {
            RuleFor(x => x.DateOfBirth!.Value)
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Date of birth cannot be in the future.")
                .LessThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-6)))
                .WithMessage("You must be at least 6 years old to register.")
                .GreaterThanOrEqualTo(DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-120)))
                .WithMessage("Please enter a valid date of birth.");
        });
    }
}
