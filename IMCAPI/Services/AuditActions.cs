using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Services;

/// <summary>
/// Every name the audit trail writes, in the one place the writers and the reader can both see.
///
/// The reader used to keep its own allow-list of action names apart from the writers, and it had
/// already drifted: <c>OrganizationSwitch</c>, <c>ItemImport</c>, <c>Restore</c> and
/// <c>PasswordReset</c> were written by the API and refused by <c>GET /api/audit?action=...</c> with
/// "action is not a recognized audit action", which is exactly the pair of events an incident is
/// reconstructed from — who switched organization, and who imported what into which one. Four strings
/// added to the old list would have fixed today's names and left the same gap for tomorrow's: the
/// names live here now, next to the writers, and the reader validates against
/// <see cref="All"/> instead of a copy of it.
///
/// What keeps it from drifting again is <c>Testing/AuditVocabularyTests.cs</c>: the reader must accept
/// every name here, <see cref="All"/> must hold every constant declared here, and every literal a
/// writer still passes to <c>AuditLogWriter.WriteAsync</c> must be one of these names.
/// </summary>
public static class AuditActions
{
    // Written by the persistence layer from the entity state alone: the row's own history.
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string SoftDelete = "SoftDelete";
    public const string Restore = "Restore";
    public const string RoleChange = "RoleChange";

    // Identity: signing in, the borrow that impersonates, and the organization a token belongs to.
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string RefreshReuse = "RefreshReuse";
    public const string PasswordReset = "PasswordReset";
    public const string OrganizationSwitch = "OrganizationSwitch";
    public const string ImpersonateStart = "ImpersonateStart";
    public const string ImpersonateEnd = "ImpersonateEnd";

    // Bulk work and retention: what an import wrote, what the sweeper removed.
    public const string ItemImport = "ItemImport";
    public const string ArchivePurge = "ArchivePurge";

    /// <summary>
    /// Every action, in declaration order. The reader validates a query against this and a test
    /// asserts it holds every constant declared above, so a new name left out here fails the suite
    /// instead of quietly becoming a name nobody can filter by.
    /// </summary>
    public static readonly IReadOnlyList<string> All =
    [
        Create, Update, Delete, SoftDelete, Restore, RoleChange,
        Login, LoginFailed, Logout, RefreshReuse, PasswordReset, OrganizationSwitch,
        ImpersonateStart, ImpersonateEnd, ItemImport, ArchivePurge,
    ];

    /// <summary>
    /// The declared spelling of a known action, or null when this vocabulary does not hold the name.
    /// The query filter is what a client types, so the match ignores case; what reaches the database
    /// is always the spelling the writers used, because <c>Action == name</c> is compared as stored.
    /// </summary>
    public static string? Canonical(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return null;
        return All.FirstOrDefault(name => string.Equals(name, action, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// What the persistence layer calls a write, from the entity state and the three shapes it can
    /// recognise. The precedence is the interceptor's history, not a preference: an identity role row
    /// is a role change even though it arrives as an insert, a soft delete is a delete that kept the
    /// row, and a restore is the update that undid one. Living here is what makes the interceptor's
    /// action set something a test can enumerate instead of a string somebody might retype.
    /// </summary>
    public static string ForChange(EntityState state, bool isRoleChange, bool isSoftDelete, bool isRestore)
    {
        if (isRoleChange) return RoleChange;
        if (isSoftDelete) return SoftDelete;
        if (isRestore) return Restore;
        return state switch
        {
            EntityState.Added => Create,
            EntityState.Modified => Update,
            EntityState.Deleted => Delete,
            _ => throw new InvalidOperationException("Unexpected entity state.")
        };
    }
}
