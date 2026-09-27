using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.ContentRatings.Tasks;

/// <summary>
/// Queues a new-media check when a library scan finishes, if enabled.
/// </summary>
public class LibraryScanTrigger : ILibraryPostScanTask
{
    private readonly ITaskManager _taskManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryScanTrigger"/> class.
    /// </summary>
    /// <param name="taskManager">Task manager.</param>
    public LibraryScanTrigger(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (Plugin.Instance?.Configuration.RunAfterLibraryScan == true)
        {
            // Queue rather than run inline so the scan is not held up and progress shows under Scheduled Tasks.
            _taskManager.QueueIfNotRunning<UpdateNewMediaRatingsTask>();
        }

        progress.Report(100);
        return Task.CompletedTask;
    }
}
