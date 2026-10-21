using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// The scheduled task (FR-21/22): daily at 04:00 by default, after
/// ListenBrainz regenerates weekly playlists (with delays of days being
/// common — hence daily polling backed by the cheap MBID short-circuit, D-7).
/// </summary>
public sealed class WeeklyJamsSyncTask : IScheduledTask
{
    /// <summary>The stable task key.</summary>
    public const string TaskKey = "ListenBrainzWeeklyJamsSync";

    private readonly SyncOrchestrator _orchestrator;
    private readonly ILogger<WeeklyJamsSyncTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WeeklyJamsSyncTask"/> class.
    /// </summary>
    /// <param name="orchestrator">The sync orchestrator.</param>
    /// <param name="logger">The logger.</param>
    public WeeklyJamsSyncTask(SyncOrchestrator orchestrator, ILogger<WeeklyJamsSyncTask> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Sync ListenBrainz Weekly Jams";

    /// <inheritdoc />
    public string Key => TaskKey;

    /// <inheritdoc />
    public string Description => "Syncs the latest ListenBrainz Weekly Jams playlist into a Jellyfin playlist.";

    /// <inheritdoc />
    public string Category => "Library";

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.ListenBrainzUsername))
        {
            _logger.LogError(
                "Weekly Jams sync skipped: the ListenBrainz username is not configured (NFR-08)");
            return;
        }

        var options = new SyncOptions(
            configuration.ListenBrainzUsername,
            string.IsNullOrWhiteSpace(configuration.PlaylistName) ? "Weekly Jams" : configuration.PlaylistName,
            configuration.OwningUserId,
            configuration.IsPublic);

        progress.Report(10.0);

        try
        {
            // Dashboard "Run now" follows the task path and therefore honors
            // the up-to-date short-circuit (FR-25); the config-page button is
            // the forced escape hatch.
            var report = await _orchestrator.SyncAsync(options, force: false, cancellationToken)
                .ConfigureAwait(false);

            progress.Report(100.0);
            _logger.LogInformation(
                "Weekly Jams task finished: {Outcome} (week of {Week}, {Matched}/{Total} matched)",
                report.Outcome,
                report.Week,
                report.Matched,
                report.Total);
        }
        catch (Exception e) when (e is ListenBrainzException or SyncInProgressException)
        {
            _logger.LogError(e, "Weekly Jams sync failed: {Message}", e.Message);
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.DailyTrigger,
            TimeOfDayTicks = TimeSpan.FromHours(4).Ticks
        };
    }
}
