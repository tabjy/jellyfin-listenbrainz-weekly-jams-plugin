using System;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// A sync is already in progress (single-flight, design D-13).
/// </summary>
public sealed class SyncInProgressException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncInProgressException"/> class.
    /// </summary>
    public SyncInProgressException()
        : base("A Weekly Jams sync is already running; skipping this request.")
    {
    }
}
