namespace PresenceTracker.Models;

public class PresenceChange
{
    public long Id { get; set; }
    public required string UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? UserPrincipalName { get; set; }
    public required string Availability { get; set; }
    public required string Activity { get; set; }
    public DateTime DetectedAtUtc { get; set; }
}
