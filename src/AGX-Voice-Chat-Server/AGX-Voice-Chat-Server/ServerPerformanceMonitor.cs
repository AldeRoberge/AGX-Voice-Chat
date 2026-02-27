using System.Diagnostics;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Extracts all performance-monitoring concerns from the main server loop.
    /// Measures poll-cycle durations, detects overruns, and periodically logs
    /// a human-readable performance summary while updating OpenTelemetry metrics.
    /// </summary>
    public sealed class ServerPerformanceMonitor(ServerMetrics metrics, Func<int> getPlayerCount) : IDisposable
    {
        private readonly Stopwatch _pollStopwatch = new();
        private readonly Stopwatch _intervalStopwatch = Stopwatch.StartNew();

        private double _pollDurationSum;
        private long _pollCount;

        /// <summary>How often (in seconds) a performance summary is logged.</summary>
        private const double LogIntervalSeconds = 10.0;

        /// <summary>If a single poll cycle exceeds this many ms, log a warning.</summary>
        private const double PollWarningThresholdMs = 50.0;

        /// <summary>
        /// Call at the very start of the poll loop body, before <c>PollEvents()</c>.
        /// </summary>
        public void BeginPollCycle()
        {
            _pollStopwatch.Restart();
        }

        /// <summary>
        /// Call at the very end of the poll loop body, after <c>Thread.Sleep</c>.
        /// Records the cycle duration, checks for overruns, and periodically
        /// logs a summary + updates metrics.
        /// </summary>
        public void EndPollCycle()
        {
            _pollStopwatch.Stop();
            var durationMs = _pollStopwatch.Elapsed.TotalMilliseconds;

            _pollDurationSum += durationMs;
            _pollCount++;

            metrics.TickDuration.Record(durationMs);

            // Overrun detection
            if (durationMs > PollWarningThresholdMs)
            {
                Log.Warning(
                    "Poll cycle took {Duration:F2}ms (threshold {Threshold:F0}ms) — server may be overloaded",
                    durationMs, PollWarningThresholdMs);
                metrics.TickOverruns.Add(1);
            }

            // Periodic summary
            if (_intervalStopwatch.Elapsed.TotalSeconds >= LogIntervalSeconds)
            {
                LogPerformanceSummary();
                _intervalStopwatch.Restart();
                _pollDurationSum = 0;
                _pollCount = 0;
            }
        }

        private void LogPerformanceSummary()
        {
            var playerCount = getPlayerCount();
            var avgPollMs = _pollCount > 0 ? _pollDurationSum / _pollCount : 0;
            var pollsPerSecond = _pollCount > 0
                ? _pollCount / _intervalStopwatch.Elapsed.TotalSeconds
                : 0;

            metrics.UpdateGcCollections();

            var memoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);

            Log.Information(
                "{PlayerCount} players | {PollRate:F0} polls/s | avg {AvgPoll:F3}ms/poll | mem {Memory:F1} MB",
                playerCount, pollsPerSecond, avgPollMs, memoryMb);

            metrics.UpdateTickRate(pollsPerSecond);
        }

        public void Dispose()
        {
            // Nothing to dispose currently; reserved for future use.
        }
    }
}