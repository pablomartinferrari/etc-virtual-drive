using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ETCStorageHelper.Caching
{
    /// <summary>
    /// Background upload queue for async file uploads
    /// Returns immediately to caller while upload happens in background
    /// </summary>
    public class BackgroundUploadQueue : IDisposable
    {
        private readonly CacheConfig _config;
        private readonly ConcurrentQueue<UploadTask> _queue;
        private readonly List<Task> _workers;
        private readonly CancellationTokenSource _cancellationToken;
        private readonly object _lock = new object();
        private bool _disposed = false;

        public BackgroundUploadQueue(CacheConfig config)
        {
            _config = config;
            _queue = new ConcurrentQueue<UploadTask>();
            _workers = new List<Task>();
            _cancellationToken = new CancellationTokenSource();

            if (_config.EnableBackgroundUpload)
            {
                StartWorkers();
            }
        }

        /// <summary>
        /// Queue a file for background upload
        /// Returns immediately
        /// </summary>
        public UploadHandle QueueUpload(
            string path,
            byte[] data,
            Func<string, byte[], Task> uploadFunc,
            Action<string> onSuccess = null,
            Action<string, Exception> onError = null)
        {
            if (!_config.EnableBackgroundUpload)
            {
                throw new InvalidOperationException("Background upload is disabled");
            }

            var task = new UploadTask
            {
                Id = Guid.NewGuid().ToString(),
                Path = path,
                Data = data,
                UploadFunc = uploadFunc,
                OnSuccess = onSuccess,
                OnError = onError,
                QueuedTime = DateTime.UtcNow,
                Status = UploadStatus.Queued
            };

            _queue.Enqueue(task);

            System.Diagnostics.Debug.WriteLine(
                $"[Upload Queue] Queued: {path} ({data.Length / 1024.0 / 1024.0:F2} MB) - " +
                $"Queue size: {_queue.Count}");

            return new UploadHandle(task.Id, this);
        }

        /// <summary>
        /// Get status of a queued upload
        /// </summary>
        public UploadStatus GetStatus(string uploadId)
        {
            lock (_lock)
            {
                // Check completed tasks (kept for 5 minutes)
                if (_completedTasks.ContainsKey(uploadId))
                {
                    var task = _completedTasks[uploadId];
                    return task.Status;
                }

                // Check queue
                foreach (var task in _queue)
                {
                    if (task.Id == uploadId)
                        return task.Status;
                }

                return UploadStatus.NotFound;
            }
        }

        /// <summary>
        /// Wait for all uploads to complete
        /// </summary>
        public void WaitForAll(int timeoutSeconds = 300)
        {
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var startTime = DateTime.UtcNow;

            while (!_queue.IsEmpty)
            {
                Thread.Sleep(100);

                if (DateTime.UtcNow - startTime > timeout)
                {
                    throw new TimeoutException(
                        $"Upload queue did not complete within {timeoutSeconds} seconds. " +
                        $"Remaining items: {_queue.Count}");
                }
            }

            System.Diagnostics.Debug.WriteLine("[Upload Queue] All uploads complete");
        }

        /// <summary>
        /// Get queue statistics
        /// </summary>
        public QueueStats GetStats()
        {
            return new QueueStats
            {
                QueuedCount = _queue.Count,
                CompletedCount = _completedTasks.Count,
                ActiveWorkers = _workers.Count
            };
        }

        #region Private Methods

        private readonly Dictionary<string, UploadTask> _completedTasks = new Dictionary<string, UploadTask>();

        private void StartWorkers()
        {
            for (int i = 0; i < _config.MaxConcurrentUploads; i++)
            {
                var worker = Task.Run(() => WorkerLoop(i), _cancellationToken.Token);
                _workers.Add(worker);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Upload Queue] Started {_config.MaxConcurrentUploads} workers");
        }

        private async Task WorkerLoop(int workerId)
        {
            while (!_cancellationToken.Token.IsCancellationRequested)
            {
                try
                {
                    if (_queue.TryDequeue(out UploadTask task))
                    {
                        await ProcessUpload(task, workerId);
                    }
                    else
                    {
                        // No work, sleep briefly
                        await Task.Delay(100, _cancellationToken.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Upload Worker {workerId}] Unexpected error: {ex.Message}");
                }
            }
        }

        private async Task ProcessUpload(UploadTask task, int workerId)
        {
            var startTime = DateTime.UtcNow;
            task.Status = UploadStatus.Uploading;

            try
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Upload Worker {workerId}] Starting: {task.Path} " +
                    $"({task.Data.Length / 1024.0 / 1024.0:F2} MB)");

                // Perform upload
                await task.UploadFunc(task.Path, task.Data);

                // Success
                task.Status = UploadStatus.Completed;
                task.CompletedTime = DateTime.UtcNow;

                var elapsed = task.CompletedTime.Value - startTime;
                System.Diagnostics.Debug.WriteLine(
                    $"[Upload Worker {workerId}] Completed: {task.Path} in {elapsed.TotalSeconds:F2}s");

                // Callback
                task.OnSuccess?.Invoke(task.Path);

                // Store in completed tasks
                lock (_lock)
                {
                    _completedTasks[task.Id] = task;
                    CleanOldCompletedTasks();
                }
            }
            catch (Exception ex)
            {
                // Error
                task.Status = UploadStatus.Failed;
                task.Error = ex;
                task.CompletedTime = DateTime.UtcNow;

                System.Diagnostics.Debug.WriteLine(
                    $"[Upload Worker {workerId}] Failed: {task.Path} - {ex.Message}");

                // Callback
                task.OnError?.Invoke(task.Path, ex);

                // Store in completed tasks
                lock (_lock)
                {
                    _completedTasks[task.Id] = task;
                    CleanOldCompletedTasks();
                }
            }
        }

        private void CleanOldCompletedTasks()
        {
            // Remove completed tasks older than 5 minutes
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            var toRemove = new List<string>();

            foreach (var kvp in _completedTasks)
            {
                if (kvp.Value.CompletedTime.HasValue && kvp.Value.CompletedTime.Value < cutoff)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                _completedTasks.Remove(id);
            }
        }

        #endregion

        public void Dispose()
        {
            if (_disposed)
                return;

            _cancellationToken.Cancel();
            Task.WaitAll(_workers.ToArray(), TimeSpan.FromSeconds(30));
            _cancellationToken.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Upload task in the queue
    /// </summary>
    internal class UploadTask
    {
        public string Id { get; set; }
        public string Path { get; set; }
        public byte[] Data { get; set; }
        public Func<string, byte[], Task> UploadFunc { get; set; }
        public Action<string> OnSuccess { get; set; }
        public Action<string, Exception> OnError { get; set; }
        public DateTime QueuedTime { get; set; }
        public DateTime? CompletedTime { get; set; }
        public UploadStatus Status { get; set; }
        public Exception Error { get; set; }
    }

    /// <summary>
    /// Handle for a queued upload
    /// </summary>
    public class UploadHandle
    {
        private readonly string _uploadId;
        private readonly BackgroundUploadQueue _queue;

        internal UploadHandle(string uploadId, BackgroundUploadQueue queue)
        {
            _uploadId = uploadId;
            _queue = queue;
        }

        public string UploadId => _uploadId;

        public UploadStatus GetStatus()
        {
            return _queue.GetStatus(_uploadId);
        }

        public bool IsComplete()
        {
            var status = GetStatus();
            return status == UploadStatus.Completed || status == UploadStatus.Failed;
        }
    }

    /// <summary>
    /// Upload status
    /// </summary>
    public enum UploadStatus
    {
        NotFound,
        Queued,
        Uploading,
        Completed,
        Failed
    }

    /// <summary>
    /// Queue statistics
    /// </summary>
    public class QueueStats
    {
        public int QueuedCount { get; set; }
        public int CompletedCount { get; set; }
        public int ActiveWorkers { get; set; }

        public override string ToString()
        {
            return $"Queued: {QueuedCount}, Completed: {CompletedCount}, Workers: {ActiveWorkers}";
        }
    }
}

