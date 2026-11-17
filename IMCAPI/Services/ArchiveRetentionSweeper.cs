using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Services;

/// <summary>
/// The clock half of the retention policy: every day, the ledger loses the rows older than the
/// configured age. Without it the retention is a document — the purge endpoint only runs when
/// somebody calls it, so an archive that nobody prunes keeps growing while the policy says otherwise.
///
/// `Archive:Retention:Days` (or `ARCHIVE_RETENTION_DAYS`) sets the age; `0` turns the sweep off and
/// leaves the endpoint as the only way to purge, which is what a dev box may prefer.
/// </summary>
public sealed class ArchiveRetentionSweeper : BackgroundService
{
    /// <summary>A day between runs; the first one waits a minute so boot does not start with a delete.</summary>
    public static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchiveRetentionSweeper> _logger;

    public ArchiveRetentionSweeper(IServiceScopeFactory scopes, IConfiguration configuration,
        ILogger<ArchiveRetentionSweeper> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// One pass. Public so a test can ask for it instead of waiting a day for the timer.
    /// </summary>
    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        var ageDays = ArchiveRetention.ResolveDays(null, _configuration);
        if (ageDays < 1)
        {
            _logger.LogInformation("Archive retention sweep is disabled (age {Age} days).", ageDays);
            return 0;
        }

        try
        {
            using var scope = _scopes.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<TenantScope>();
            var db = scope.ServiceProvider.GetRequiredService<IMCAPI.DB.ApplicationContext>();
            // The sweeper has no token; explicitly enumerate active organizations, never sweep an implicit "all".
            var organizations = await db.Constants.IgnoreQueryFilters()
                .Where(row => row.Group == "Organization" && row.TenantId == 0 && row.IsActive)
                .Select(row => row.Id).ToListAsync(cancellationToken);
            var retention = scope.ServiceProvider.GetRequiredService<ArchiveRetention>();
            var cutoff = DateTime.UtcNow.AddDays(-ageDays);
            var deleted = 0;
            foreach (var organizationId in organizations)
            {
                tenant.SelectSystemTenant(organizationId);
                var result = await retention.PurgeAsync(cutoff, ageDays, "archive-retention", cancellationToken);
                deleted += result.Deleted;
                if (result.Deleted > 0)
                _logger.LogInformation(
                    "Archive retention removed {Deleted} ledger rows older than {Cutoff} and {Files} files.",
                    result.Deleted, cutoff, result.DestroyedAttachments);
            }
            return deleted;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A sweep that fails must not take the host down: it runs again tomorrow.
            _logger.LogError(exception, "Archive retention sweep failed; it will try again in {Interval}.", Interval);
            return 0;
        }
    }
}
