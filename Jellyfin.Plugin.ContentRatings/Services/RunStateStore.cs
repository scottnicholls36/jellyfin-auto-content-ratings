using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRatings.Services;

/// <summary>
/// Remembers when the last successful run started, so new-media checks only look at items added since.
/// Kept out of the plugin configuration so saving the settings page can never roll it back.
/// </summary>
public class RunStateStore
{
    private readonly ILogger<RunStateStore> _logger;
    private readonly object _lock = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStateStore"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public RunStateStore(ILogger<RunStateStore> logger)
    {
        _logger = logger;
    }

    private static string? FilePath
        => Plugin.Instance is null ? null : Path.Combine(Plugin.Instance.DataFolderPath, "state.json");

    /// <summary>
    /// Gets the start time of the last successful run, or <c>null</c> if the plugin has never completed one.
    /// </summary>
    /// <returns>UTC timestamp.</returns>
    public virtual DateTime? GetLastRunUtc()
    {
        var path = FilePath;
        lock (_lock)
        {
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<RunState>(File.ReadAllText(path))?.LastRunUtc;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogWarning(ex, "Could not read {Path}; treating the next run as the first", path);
                return null;
            }
        }
    }

    /// <summary>
    /// Records the start time of a successful run.
    /// </summary>
    /// <param name="startedUtc">UTC timestamp.</param>
    public virtual void SetLastRunUtc(DateTime startedUtc)
    {
        var path = FilePath;
        if (path is null)
        {
            return;
        }

        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new RunState { LastRunUtc = startedUtc }));
        }
    }

    private sealed class RunState
    {
        public DateTime? LastRunUtc { get; set; }
    }
}
