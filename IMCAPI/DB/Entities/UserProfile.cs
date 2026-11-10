namespace IMCAPI.DB.Entities;

/// <summary>Application state for an Identity user without replacing Identity's user type.</summary>
public sealed class UserProfile
{
    public string UserId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime? LastAccessUtc { get; set; }
}
