using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ContentRatings.Configuration;
using Jellyfin.Plugin.ContentRatings.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRatings.Services;

/// <summary>
/// Translates the frequencies on the settings page into Jellyfin scheduled task triggers.
/// </summary>
public static class TaskScheduleSync
{
    /// <summary>
    /// Builds the triggers for the new-media task.
    /// </summary>
    /// <param name="config">Current configuration.</param>
    /// <returns>An interval trigger, or none for manual only.</returns>
    public static IReadOnlyList<TaskTriggerInfo> NewMediaTriggers(PluginConfiguration config)
        => IntervalTrigger(TimeSpan.FromHours(Math.Max(0, config.NewMediaIntervalHours)));

    /// <summary>
    /// Builds the triggers for the full-library task.
    /// </summary>
    /// <param name="config">Current configuration.</param>
    /// <returns>An interval trigger, or none for manual only.</returns>
    public static IReadOnlyList<TaskTriggerInfo> FullScanTriggers(PluginConfiguration config)
        => IntervalTrigger(TimeSpan.FromDays(Math.Max(0, config.FullScanIntervalDays)));

    /// <summary>
    /// Replaces the triggers on both tasks to match the configuration.
    /// </summary>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="config">Current configuration.</param>
    /// <param name="logger">Logger.</param>
    public static void Apply(ITaskManager taskManager, PluginConfiguration config, ILogger logger)
    {
        foreach (var worker in taskManager.ScheduledTasks)
        {
            var triggers = worker.ScheduledTask switch
            {
                UpdateNewMediaRatingsTask => NewMediaTriggers(config),
                UpdateAllRatingsTask => FullScanTriggers(config),
                _ => null
            };

            if (triggers is not null)
            {
                worker.Triggers = triggers.ToArray();
                logger.LogInformation(
                    "Set schedule for {Task}: {Schedule}",
                    worker.Name,
                    triggers.Count == 0 ? "manual only" : TimeSpan.FromTicks(triggers[0].IntervalTicks ?? 0).ToString());
            }
        }
    }

    private static IReadOnlyList<TaskTriggerInfo> IntervalTrigger(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            return [];
        }

        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = interval.Ticks
            }
        ];
    }
}
