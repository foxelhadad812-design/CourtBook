namespace CourtBook.Domain.Enums;

public enum ReportStatus
{
    Pending,
    UnderReview,
    Resolved,
    Dismissed
}

public enum ReportTargetType
{
    Venue,
    Court,
    Review,
    User,
    Game
}
