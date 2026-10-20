namespace HexFund.Core.Enums;

/// <summary>
/// What kind of report a user is sending. The numeric values are stored in the database and sent
/// by clients, so they must not be renumbered. New values are appended.
/// </summary>
public enum FeedbackType
{
    Bug = 0,
    Idea = 1,
    Question = 2
}
