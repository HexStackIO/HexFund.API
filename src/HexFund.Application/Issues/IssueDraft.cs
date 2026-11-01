namespace HexFund.Application.Issues;

/// <summary>Everything needed to create an issue. Contains no user identifiers.</summary>
public sealed record IssueDraft(string Title, string Body, IReadOnlyList<string> Labels);
