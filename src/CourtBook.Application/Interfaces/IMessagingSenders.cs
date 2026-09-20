namespace CourtBook.Application.Interfaces;

public interface IEmailSender
{
    Task SendEmailAsync(string toEmail, string subject, string body);
}

public interface IPushNotificationSender
{
    Task SendPushAsync(Guid userId, string title, string message, string? actionUrl);
}
