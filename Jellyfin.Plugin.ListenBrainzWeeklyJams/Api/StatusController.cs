using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.ListenBrainz;
using Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Api;

/// <summary>
/// Config-page API (FR-04, FR-22, FR-25, D-14/D-15): status readout and the
/// forced Sync now. Plugin assemblies are registered as MVC application
/// parts by the server (Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs L163).
/// </summary>
[ApiController]
[Route("plugins/ListenBrainzWeeklyJams")]
[Authorize(Policy = Policies.RequiresElevation)]
public sealed class StatusController : ControllerBase
{
    private readonly SyncOrchestrator _orchestrator;
    private readonly SyncStateStore _stateStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusController"/> class.
    /// </summary>
    /// <param name="orchestrator">The sync orchestrator.</param>
    /// <param name="stateStore">The sync state store.</param>
    public StatusController(SyncOrchestrator orchestrator, SyncStateStore stateStore)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
    }

    /// <summary>
    /// Gets the last-sync status.
    /// </summary>
    /// <returns>The status.</returns>
    [HttpGet("Status")]
    public ActionResult<SyncState> GetStatus()
        => Ok(_stateStore.Load());

    /// <summary>
    /// Runs a full (forced) sync now — the short-circuit escape hatch (FR-25).
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The sync report.</returns>
    [HttpPost("SyncNow")]
    public async Task<ActionResult<SyncReport>> SyncNow(CancellationToken cancellationToken)
    {
        var configuration = Plugin.Instance?.Configuration;
        if (configuration is null || string.IsNullOrWhiteSpace(configuration.ListenBrainzUsername))
        {
            return BadRequest("The ListenBrainz username is not configured.");
        }

        var options = new SyncOptions(
            configuration.ListenBrainzUsername,
            string.IsNullOrWhiteSpace(configuration.PlaylistName) ? "Weekly Jams" : configuration.PlaylistName,
            configuration.OwningUserId,
            configuration.IsPublic);

        try
        {
            return Ok(await _orchestrator.SyncAsync(options, force: true, cancellationToken).ConfigureAwait(false));
        }
        catch (ListenBrainzException e)
        {
            return BadRequest(e.Message);
        }
        catch (SyncInProgressException e)
        {
            return Conflict(e.Message);
        }
    }
}
