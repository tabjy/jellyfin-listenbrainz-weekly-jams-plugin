using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ListenBrainzWeeklyJams.Sync;

/// <summary>
/// JSON-file sync state store (design D-9): atomic writes (temp file +
/// move), corrupt or missing files are logged and treated as "no state"
/// (safe: worst case one redundant full sync).
/// </summary>
public sealed class SyncStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly ILogger<SyncStateStore> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncStateStore"/> class.
    /// </summary>
    /// <param name="path">The state file path.</param>
    /// <param name="logger">The logger.</param>
    public SyncStateStore(string path, ILogger<SyncStateStore> logger)
    {
        _path = path;
        _logger = logger;
    }

    /// <summary>
    /// Loads the state; a fresh state when the file is missing or unreadable.
    /// </summary>
    /// <returns>The sync state.</returns>
    public SyncState Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new SyncState();
                }

                var state = JsonSerializer.Deserialize<SyncState>(File.ReadAllText(_path), JsonOptions);
                return state ?? new SyncState();
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(e, "Sync state file {Path} is unreadable; treating as first run", _path);
                return new SyncState();
            }
        }
    }

    /// <summary>
    /// Persists the state atomically.
    /// </summary>
    /// <param name="state">The state to persist.</param>
    public void Save(SyncState state)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, _path, overwrite: true);
        }
    }
}
