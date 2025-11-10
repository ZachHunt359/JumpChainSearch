namespace JumpChainSearch.Models;

public class SuggestTagRequest
{
    public int DocumentId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string TagCategory { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public class RequestTagRemovalRequest
{
    public int DocumentId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string TagCategory { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}

public class CastVoteRequest
{
    public string UserId { get; set; } = string.Empty;
    public int? SuggestionId { get; set; }
    public int? RemovalRequestId { get; set; }
    public bool IsInFavor { get; set; }
}

public class UpdateVotingConfigRequest
{
    public int MinimumVotesRequired { get; set; }
    public double RequiredAgreementPercentage { get; set; }
    public bool ScaleByPopularity { get; set; }
    public double PopularityScaleFactor { get; set; }
    public int MaximumVotesRequired { get; set; }
    public int VoteDecayStartDays { get; set; }
    public double VoteDecayRatePerDay { get; set; }
    public bool AutoApplyEnabled { get; set; }
}

public class AdminActionRequest
{
    public string? Reason { get; set; }
}

public class TrackViewRequest
{
    public int DocumentId { get; set; }
    public string UserId { get; set; } = string.Empty;
}

public class ApplyRulesRequest
{
    public bool DryRun { get; set; } = false;
}

public class CreateManualRuleRequest
{
    public string GoogleDriveFileId { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string TagCategory { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty; // "Add" or "Remove"
    public string? Notes { get; set; }
}
