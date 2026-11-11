using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using System.Data;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace IMCAPI.Services;

public sealed class AuditSaveChangesInterceptor(IHttpContextAccessor accessor) : SaveChangesInterceptor
{
    private const string Redacted = "[REDACTED]";
    private readonly ConditionalWeakTable<DbContext, Pending> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Prepare(eventData.Context, async: false, default).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await Prepare(eventData.Context, async: true, cancellationToken);
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Persist(eventData.Context, async: false, default).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        await Persist(eventData.Context, async: true, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Rollback(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        return RollbackAsync(eventData.Context, cancellationToken);
    }

    private async Task Prepare(DbContext? context, bool async, CancellationToken ct)
    {
        if (context is null) return;
        var entries = context.ChangeTracker.Entries().ToList();
        var auditEntries = entries.Where(entry => entry.Entity is AuditLog).ToList();
        if (auditEntries.Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Audit log rows are append-only.");
        var changed = entries.Where(entry => entry.Entity is not AuditLog
            && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
        if (changed.Count == 0) return;
        try
        {
            IDbContextTransaction? transaction = context.Database.CurrentTransaction;
            var owns = transaction is null;
            if (owns) transaction = async
                ? await context.Database.BeginTransactionAsync(ct)
                : context.Database.BeginTransaction();
            var http = accessor.HttpContext;
            var principal = http?.User;
            var now = DateTime.UtcNow;
            var impersonated = principal?.FindFirst("act")?.Value;
            var actorId = impersonated is null
                ? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value
                : impersonated;
            var actorEmail = impersonated is null
                ? principal?.FindFirst(ClaimTypes.Email)?.Value ?? principal?.Identity?.Name
                : principal?.FindFirst("act_email")?.Value;
            var imp = (string?)null;
            var changes = new List<Change>(changed.Count);
            foreach (var entry in changed)
            {
                var isItemRestore = entry.Entity is Item && http?.Items.ContainsKey("ItemArchiveRestore") == true;
                if (entry.Entity is Base entity)
                {
                    if (entry.State == EntityState.Added && !isItemRestore)
                    {
                        entity.CreatedAt = now;
                        entity.CreatedBy = actorEmail ?? actorId;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        var createdAt = entry.Property(nameof(Base.CreatedAt));
                        createdAt.CurrentValue = createdAt.OriginalValue;
                        createdAt.IsModified = false;
                        var createdBy = entry.Property(nameof(Base.CreatedBy));
                        createdBy.CurrentValue = createdBy.OriginalValue;
                        createdBy.IsModified = false;
                    }
                    if (entry.State is EntityState.Added or EntityState.Modified)
                    {
                        entity.UpdatedAt = now;
                        entity.UpdatedBy = actorEmail ?? actorId;
                        if (entry.State == EntityState.Modified)
                        {
                            entry.Property(nameof(Base.UpdatedAt)).IsModified = true;
                            entry.Property(nameof(Base.UpdatedBy)).IsModified = true;
                        }
                    }
                }
                var isRole = entry.Metadata.ClrType.IsGenericType
                    && entry.Metadata.ClrType.GetGenericTypeDefinition() == typeof(IdentityUserRole<>);
                var isSoftDelete = entry.State == EntityState.Modified &&
                    (entry.Entity is Base && Equals(entry.Property(nameof(Base.IsActive)).OriginalValue, true)
                        && Equals(entry.Property(nameof(Base.IsActive)).CurrentValue, false)
                    || entry.Entity is UserProfile && Equals(entry.Property(nameof(UserProfile.IsActive)).OriginalValue, true)
                        && Equals(entry.Property(nameof(UserProfile.IsActive)).CurrentValue, false));
                var action = isRole ? "RoleChange" : isSoftDelete ? "SoftDelete" : isItemRestore ? "Restore" : entry.State switch
                {
                    EntityState.Added => "Create", EntityState.Modified => "Update", EntityState.Deleted => "Delete",
                    _ => throw new InvalidOperationException("Unexpected entity state.")
                };
                changes.Add(new Change(entry, action, isRole ? "IdentityUserRole" : entry.Metadata.ClrType.Name,
                    isRole ? entry.Property("UserId").CurrentValue?.ToString() : null,
                    entry.State == EntityState.Added ? null : Serialize(entry, original: true),
                    entry.State == EntityState.Deleted ? null : Serialize(entry, original: false),
                    now, actorId, actorEmail, imp, http?.Connection.RemoteIpAddress?.ToString(),
                    http?.Request.Headers.UserAgent.ToString(), http?.TraceIdentifier ?? Activity.Current?.Id));
            }
            _pending.Add(context, new Pending(changes, transaction, owns));
        }
        catch
        {
            if (async) await RollbackAsync(context, ct);
            else Rollback(context);
            throw;
        }
    }

    private static Dictionary<string, object?> Serialize(EntityEntry entry, bool original)
    {
        return entry.Properties.ToDictionary(property => property.Metadata.Name,
            property => Sensitive(property.Metadata.Name, entry.Metadata.ClrType) ? (object?)Redacted
                : original ? property.OriginalValue : property.CurrentValue);
    }

    private static bool Sensitive(string property, Type type)
    {
        var name = property.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();
        return name.Contains("password", StringComparison.Ordinal) || name.Contains("securitystamp", StringComparison.Ordinal)
            || name.Contains("token", StringComparison.Ordinal) || name.Contains("secret", StringComparison.Ordinal)
            || name.Contains("credential", StringComparison.Ordinal)
            || name == "value" && type.Name.StartsWith("IdentityUserToken", StringComparison.Ordinal);
    }

    private async Task Persist(DbContext? context, bool async, CancellationToken ct)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        try
        {
            foreach (var change in pending.Changes)
            {
                var id = change.FixedId ?? string.Join(",", change.Entry.Metadata.FindPrimaryKey()!.Properties
                    .Select(property => change.Entry.Property(property.Name).CurrentValue?.ToString() ?? ""));
                var afterValues = change.After;
                if (change.Action == "Create" && afterValues is not null)
                {
                    foreach (var key in change.Entry.Metadata.FindPrimaryKey()!.Properties)
                    {
                        if (!Sensitive(key.Name, change.Entry.Metadata.ClrType))
                            afterValues[key.Name] = change.Entry.Property(key.Name).CurrentValue;
                    }
                }
                var log = new AuditLog
                {
                    TimestampUtc = change.Timestamp, ActorUserId = change.ActorId, ActorEmail = change.Email,
                    ImpersonatedUserId = change.ImpersonatedId, Action = change.Action, Entity = change.Entity,
                    EntityId = id,
                    BeforeJson = change.Before is null ? null : JsonSerializer.Serialize(change.Before),
                    AfterJson = afterValues is null ? null : JsonSerializer.Serialize(afterValues),
                    IpAddress = change.Ip, UserAgent = change.UserAgent, RequestId = change.RequestId
                };
                await InsertLog(context, log, async, ct);
            }
            if (pending.OwnsTransaction && pending.Transaction is not null)
            {
                if (async) await pending.Transaction.CommitAsync(ct);
                else pending.Transaction.Commit();
            }
        }
        catch
        {
            if (context.Database.CurrentTransaction is { } transaction)
            {
                if (async) await transaction.RollbackAsync(ct);
                else transaction.Rollback();
            }
            throw;
        }
        finally
        {
            if (pending.OwnsTransaction && pending.Transaction is not null)
            {
                if (async) await pending.Transaction.DisposeAsync();
                else pending.Transaction.Dispose();
            }
        }
    }

    private static async Task InsertLog(DbContext context, AuditLog log, bool async, CancellationToken cancellationToken)
    {
        const string sql = "INSERT INTO \"AuditLogs\" (\"TimestampUtc\", \"ActorUserId\", \"ActorEmail\", \"ImpersonatedUserId\", \"Action\", \"Entity\", \"EntityId\", \"BeforeJson\", \"AfterJson\", \"IpAddress\", \"UserAgent\", \"RequestId\") VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11)";
        var values = new object?[] { log.TimestampUtc, log.ActorUserId, log.ActorEmail, log.ImpersonatedUserId,
            log.Action, log.Entity, log.EntityId, log.BeforeJson, log.AfterJson, log.IpAddress, log.UserAgent, log.RequestId };
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        for (var index = 0; index < values.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{index}";
            parameter.Value = values[index] ?? DBNull.Value;
            if (index == 0) parameter.DbType = DbType.DateTime;
            else if (values[index] is string || values[index] is null) parameter.DbType = DbType.String;
            command.Parameters.Add(parameter);
        }
        if (connection.State != ConnectionState.Open)
        {
            if (async) await connection.OpenAsync(cancellationToken);
            else connection.Open();
        }
        if (async) await command.ExecuteNonQueryAsync(cancellationToken);
        else command.ExecuteNonQuery();
    }

    private void Rollback(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        try { pending.Transaction?.Rollback(); }
        finally { if (pending.OwnsTransaction) pending.Transaction?.Dispose(); }
    }

    private async Task RollbackAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null || !_pending.TryGetValue(context, out var pending)) return;
        _pending.Remove(context);
        try
        {
            if (pending.Transaction is not null) await pending.Transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            if (pending.OwnsTransaction && pending.Transaction is not null)
                await pending.Transaction.DisposeAsync();
        }
    }

    private sealed record Pending(List<Change> Changes, IDbContextTransaction? Transaction, bool OwnsTransaction);
    private sealed record Change(EntityEntry Entry, string Action, string Entity, string? FixedId,
        Dictionary<string, object?>? Before, Dictionary<string, object?>? After, DateTime Timestamp, string? ActorId,
        string? Email, string? ImpersonatedId, string? Ip, string? UserAgent, string? RequestId);
}

public sealed class AuditLogWriter(ApplicationContext context, IHttpContextAccessor accessor)
{
    public async Task WriteAsync(string action, string? entity = null, string? entityId = null,
        string? actorUserId = null, string? actorEmail = null, CancellationToken cancellationToken = default,
        string? impersonatedUserId = null, string? beforeJson = null, string? afterJson = null)
    {
        var http = accessor.HttpContext;
        var principal = http?.User;
        var impersonating = principal?.FindFirst("act")?.Value;
        context.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = DateTime.UtcNow,
            ActorUserId = actorUserId ?? (impersonating is null
                ? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal?.FindFirst("sub")?.Value
                : principal?.FindFirst("act")?.Value),
            ActorEmail = actorEmail ?? (impersonating is null
                ? principal?.FindFirst(ClaimTypes.Email)?.Value ?? principal?.Identity?.Name
                : principal?.FindFirst("act_email")?.Value),
            ImpersonatedUserId = impersonatedUserId,
            Action = action, Entity = entity, EntityId = entityId,
            BeforeJson = beforeJson, AfterJson = afterJson,
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(), UserAgent = http?.Request.Headers.UserAgent.ToString(),
            RequestId = http?.TraceIdentifier ?? Activity.Current?.Id
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}
