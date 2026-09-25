namespace JumpChainSearch.DTOs;

/// <summary>Reports the user's observed state of a document source.</summary>
public sealed record ReportDocumentLinkRequest
{
    public bool ReportedDead { get; init; }
}
