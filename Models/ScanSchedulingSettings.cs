namespace JumpChainSearch.Models;

public class ScanSchedulingSettings
{
    public bool Enabled { get; set; } = false;
    public int IntervalHours { get; set; } = 24;
    public DateTime? LastScanTime { get; set; }
    public DateTime? NextScheduledScan { get; set; }
}