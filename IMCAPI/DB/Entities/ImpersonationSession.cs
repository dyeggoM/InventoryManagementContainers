namespace IMCAPI.DB.Entities;

/// <summary>
/// One impersonation: who borrowed which role, from when to when, and whether it was ended.
///
/// The token carries its <see cref="Id"/> as <c>imp_sid</c>, which makes the borrow a thing the server
/// can see and stop — instead of a stateless token that stays valid for its seven minutes no matter
/// what the actor does afterwards. `ImpersonateStart`/`ImpersonateEnd` in the audit log point at the
/// same id, so the two can be paired, and any row written while impersonating names it too.
/// </summary>
public sealed class ImpersonationSession
{
    /// <summary>Guid with no dashes: it travels in a claim and in the audit rows.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int TenantId { get; set; }

    public string ActorUserId { get; set; } = string.Empty;
    public string? ActorEmail { get; set; }

    /// <summary>The role that was borrowed, which is also the only role the token carries.</summary>
    public string TargetRole { get; set; } = string.Empty;

    public DateTime StartedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Null while the borrow is live. Set once, by the first stop or by the sweeper.</summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary><c>api</c>, <c>expired</c>, or the id of whoever ended it.</summary>
    public string? EndedBy { get; set; }
}
