using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ETCStorageHelper.Resilience
{
    /// <summary>
    /// Implements retry logic with exponential backoff for resilient operations
    /// </summary>
    internal class RetryPolicy
    {
        private readonly int _maxRetries;
        private readonly int _initialDelayMs;
        private readonly int _maxDelayMs;

        public RetryPolicy(int maxRetries = 3, int initialDelayMs = 1000, int maxDelayMs = 30000)
        {
            _maxRetries = maxRetries;
            _initialDelayMs = initialDelayMs;
            _maxDelayMs = maxDelayMs;
        }

        /// <summary>
        /// Execute an async operation with retry and exponential backoff
        /// </summary>
        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, string operationName = "Operation")
        {
            int attempt = 0;
            Exception lastException = null;

            while (attempt <= _maxRetries)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (IsTransientError(ex) && attempt < _maxRetries)
                {
                    lastException = ex;
                    attempt++;

                    var delay = CalculateDelay(attempt);
                    
                    // Log retry attempt (could be enhanced with proper logging framework)
                    var inner = ex.InnerException != null ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : string.Empty;
                    System.Diagnostics.Debug.WriteLine(
                        $"[RetryPolicy] {operationName} failed (attempt {attempt}/{_maxRetries}). " +
                        $"Error: {ex.GetType().Name}: {ex.Message}{inner}. Retrying in {delay}ms...");

                    await Task.Delay(delay);
                }
                catch (Exception ex)
                {
                    // Non-transient error or max retries exceeded
                    var inner = ex.InnerException != null ? $" | Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : string.Empty;
                    throw new Exception(
                        $"{operationName} failed after {attempt} attempt(s). " +
                        $"Last error: {ex.GetType().Name}: {ex.Message}{inner}", ex);
                }
            }

            // Should never reach here, but just in case
            throw new Exception(
                $"{operationName} failed after {_maxRetries} retries. " +
                $"Last error: {lastException?.Message}", lastException);
        }

        /// <summary>
        /// Execute an async operation (no return value) with retry and exponential backoff
        /// </summary>
        public async Task ExecuteAsync(Func<Task> operation, string operationName = "Operation")
        {
            await ExecuteAsync(async () =>
            {
                await operation();
                return true; // Dummy return value
            }, operationName);
        }

        /// <summary>
        /// Determine if an error is transient and worth retrying
        /// </summary>
        private bool IsTransientError(Exception ex)
        {
            // HTTP-related transient errors
            if (ex is HttpRequestException httpEx)
            {
                return true; // Network issues, timeouts, etc.
            }

            // Specific HTTP status codes that are transient
            if (ex.Message.Contains("429") || // Too Many Requests (throttling)
                ex.Message.Contains("503") || // Service Unavailable
                ex.Message.Contains("504") || // Gateway Timeout
                ex.Message.Contains("408") || // Request Timeout
                ex.Message.Contains("500") || // Internal Server Error (sometimes transient)
                ex.Message.Contains("502"))   // Bad Gateway
            {
                return true;
            }

            // Network-level errors
            if (ex is WebException webEx)
            {
                return webEx.Status == WebExceptionStatus.Timeout ||
                       webEx.Status == WebExceptionStatus.ConnectFailure ||
                       webEx.Status == WebExceptionStatus.ReceiveFailure ||
                       webEx.Status == WebExceptionStatus.SendFailure ||
                       webEx.Status == WebExceptionStatus.NameResolutionFailure;
            }

            // Task cancellation (timeout)
            if (ex is TaskCanceledException || ex is OperationCanceledException)
            {
                return true;
            }

            // Default: not transient
            return false;
        }

        /// <summary>
        /// Calculate delay using exponential backoff with jitter
        /// </summary>
        private int CalculateDelay(int attemptNumber)
        {
            // Exponential backoff: delay = initialDelay * 2^(attempt-1)
            var exponentialDelay = _initialDelayMs * Math.Pow(2, attemptNumber - 1);
            
            // Cap at max delay
            var cappedDelay = Math.Min(exponentialDelay, _maxDelayMs);
            
            // Add jitter (randomness) to avoid thundering herd problem
            // Jitter range: 80% to 100% of calculated delay
            var random = new Random();
            var jitter = random.Next((int)(cappedDelay * 0.8), (int)cappedDelay);
            
            return jitter;
        }
    }
}

