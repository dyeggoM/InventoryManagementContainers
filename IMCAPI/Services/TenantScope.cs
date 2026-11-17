using System.Globalization;
using IMCAPI.DB.Entities;

namespace IMCAPI.Services;

/// <summary>Request tenant, or an explicitly selected system tenant outside HTTP.</summary>
public sealed class TenantScope(IHttpContextAccessor accessor)
{
    private int? _systemTenant;

    // An import may write to another organization after its destination was authorized by the controller.
    public int? WriteTenantOverride { get; private set; }

    public int TenantId => WriteTenantOverride ?? _systemTenant ?? (int.TryParse(accessor.HttpContext?.User.FindFirst("org")?.Value,
        NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0);

    public IDisposable SetWriteTenant(int tenantId)
    {
        if (accessor.HttpContext is null || tenantId <= 0)
            throw new InvalidOperationException("A validated request destination is required.");
        if (WriteTenantOverride is int existing && existing != tenantId)
            throw new InvalidOperationException("Two import destinations cannot be active at once.");
        var previous = WriteTenantOverride;
        WriteTenantOverride = tenantId;
        return new WriteScope(this, previous);
    }

    private sealed class WriteScope(TenantScope owner, int? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            owner.WriteTenantOverride = previous;
            _disposed = true;
        }
    }

    /// <summary>
    /// Whether the request belongs to the one role that may reach outside its organization: the
    /// shared vocabulary (tenant 0) is seed data, and only this role may clean it up.
    /// </summary>
    public bool IsSuperadmin => accessor.HttpContext?.User.IsInRole("superadmin") == true;

    public void SelectSystemTenant(int tenantId)
    {
        if (accessor.HttpContext is not null || tenantId < 0)
            throw new InvalidOperationException("Only a background scope may select a system tenant.");
        _systemTenant = tenantId;
    }

    public static bool IsShared(Constants constant) => constant.Group is "Organization" or "FieldType" or "AttachmentType";
}
