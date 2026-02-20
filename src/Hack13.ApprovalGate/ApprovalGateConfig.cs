namespace Hack13.ApprovalGate;

internal sealed class ApprovalGateConfig
{
    public string PollUrl { get; set; } = string.Empty;
    public string PollMethod { get; set; } = "GET";
    public Dictionary<string, string>? PollHeaders { get; set; }
    public int PollIntervalSeconds { get; set; } = 30;
    public int TimeoutSeconds { get; set; } = 86400;
    public string ApprovedPath { get; set; } = string.Empty;
    public string ApprovedValue { get; set; } = string.Empty;
    public string RejectedPath { get; set; } = string.Empty;
    public string RejectedValue { get; set; } = string.Empty;
}
