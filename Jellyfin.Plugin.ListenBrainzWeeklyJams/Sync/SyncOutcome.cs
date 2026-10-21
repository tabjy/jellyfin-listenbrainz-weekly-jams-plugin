namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// The outcome of a sync run.
/// </summary>
public enum SyncOutcome
{
    /// <summary>The playlist was created or refreshed.</summary>
    Updated,

    /// <summary>The latest playlist was already synced; the run short-circuited (FR-25).</summary>
    UpToDate,

    /// <summary>No tracks matched; the playlist was left untouched and state was not advanced (FR-18).</summary>
    ZeroMatchGuard
}
