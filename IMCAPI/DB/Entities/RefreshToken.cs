namespace IMCAPI.DB.Entities;

public sealed class RefreshToken
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
}
