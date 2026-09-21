using CourtBook.Application.DTOs;
using FluentValidation;

namespace CourtBook.Application.Validators;

public class CreateVenueRequestValidator : AbstractValidator<CreateVenueRequest>
{
    public CreateVenueRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Venue name is required.")
            .MinimumLength(3).WithMessage("Venue name must be at least 3 characters.")
            .MaximumLength(150).WithMessage("Venue name must not exceed 150 characters.");

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100);

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(300);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email address format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0, 90.0).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0, 180.0).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Longitude.HasValue);
    }
}

public class UpdateVenueRequestValidator : AbstractValidator<UpdateVenueRequest>
{
    public UpdateVenueRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Venue name is required.")
            .MinimumLength(3)
            .MaximumLength(150);

        RuleFor(x => x.City)
            .NotEmpty().WithMessage("City is required.")
            .MaximumLength(100);

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Address is required.")
            .MaximumLength(300);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("Invalid email address format.")
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0, 90.0).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0, 180.0).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Longitude.HasValue);
    }
}
