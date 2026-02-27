using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AGX_Voice_Chat_Server
{
    /// <summary>
    /// Centralized OpenTelemetry metrics for the AGX server.
    /// All metrics are thread-safe and designed for low-allocation, tick-safe operation.
    /// </summary>
    public class ServerMetrics : IDisposable
    {
        private readonly Meter _meter;
        private readonly Process _process = Process.GetCurrentProcess();
        
        // Network & Bandwidth Metrics
        private long _networkBytesIn;
        private long _networkBytesOut;
        private long _networkPacketsIn;
        private long _networkPacketsOut;
        
        public Counter<long> NetworkBytesIn { get; }
        public Counter<long> NetworkBytesOut { get; }
        public Counter<long> NetworkPacketsIn { get; }
        public Counter<long> NetworkPacketsOut { get; }
        
        // CPU & Memory Metrics
        public ObservableGauge<double> CpuUsage { get; }
        public ObservableGauge<long> MemoryUsed { get; }
        public ObservableGauge<long> GcHeap { get; }
        public Counter<long> GcCollections { get; }
        public ObservableGauge<int> ThreadPoolActive { get; }
        public ObservableGauge<long> ThreadPoolQueueLength { get; }
        
        // Tick & Main Loop Metrics
        public Histogram<double> TickDuration { get; }
        public ObservableGauge<double> TickRate { get; }
        public Counter<long> TickOverruns { get; }
        
        // Player & Session Metrics
        private int _playersConnected;
        public UpDownCounter<int> PlayersConnected { get; }
        public ObservableGauge<int> PlayersMax { get; }
        public Counter<long> PlayerJoins { get; }
        public Counter<long> PlayerLeaves { get; }
        public Histogram<double> PlayerPing { get; }
        
        // World & Simulation Metrics
        private int _entitiesActive;
        public ObservableGauge<int> EntitiesActive { get; }
        public Counter<long> EntitiesSpawned { get; }
        public Counter<long> EntitiesDespawned { get; }
        
        // Error & Stability Metrics
        public Counter<long> ErrorsTotal { get; }
        public Counter<long> DisconnectsTotal { get; }
        public ObservableGauge<double> Uptime { get; }
        
        private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();
        private DateTime _lastCpuTime = DateTime.UtcNow;
        private TimeSpan _lastTotalProcessorTime;
        private int _maxPlayers;
        
        public ServerMetrics()
        {
            _meter = new Meter("AGH.Server", "1.0.0");
            _lastTotalProcessorTime = _process.TotalProcessorTime;
            
            // Network & Bandwidth Metrics
            NetworkBytesIn = _meter.CreateCounter<long>(
                "server.network.in.bytes",
                unit: "bytes",
                description: "Total bytes received by the server");
            
            NetworkBytesOut = _meter.CreateCounter<long>(
                "server.network.out.bytes",
                unit: "bytes",
                description: "Total bytes sent by the server");
            
            NetworkPacketsIn = _meter.CreateCounter<long>(
                "server.network.packets.in",
                unit: "packets",
                description: "Total packets received by the server");
            
            NetworkPacketsOut = _meter.CreateCounter<long>(
                "server.network.packets.out",
                unit: "packets",
                description: "Total packets sent by the server");
            
            // CPU & Memory Metrics
            CpuUsage = _meter.CreateObservableGauge(
                "server.cpu.usage",
                () => GetCpuUsage(),
                unit: "%",
                description: "Process CPU usage percentage (normalized per core)");
            
            MemoryUsed = _meter.CreateObservableGauge(
                "server.memory.used",
                () => _process.WorkingSet64,
                unit: "bytes",
                description: "Current memory usage");
            
            GcHeap = _meter.CreateObservableGauge(
                "server.memory.gc.heap",
                () => GC.GetTotalMemory(false),
                unit: "bytes",
                description: "Total bytes allocated on the managed heap");
            
            GcCollections = _meter.CreateCounter<long>(
                "server.memory.gc.collections",
                unit: "collections",
                description: "Number of garbage collections");
            
            ThreadPoolActive = _meter.CreateObservableGauge(
                "server.threadpool.active",
                () =>
                {
                    ThreadPool.GetAvailableThreads(out var availableWorkers, out _);
                    ThreadPool.GetMaxThreads(out var maxWorkers, out _);
                    return maxWorkers - availableWorkers;
                },
                unit: "threads",
                description: "Number of active thread pool threads");
            
            ThreadPoolQueueLength = _meter.CreateObservableGauge(
                "server.threadpool.queue.length",
                () => ThreadPool.PendingWorkItemCount,
                unit: "items",
                description: "Number of items in the thread pool queue");
            
            // Tick & Main Loop Metrics
            TickDuration = _meter.CreateHistogram<double>(
                "server.tick.duration",
                unit: "ms",
                description: "Time taken to process a single server tick");
            
            TickRate = _meter.CreateObservableGauge(
                "server.tick.rate",
                () => GetCurrentTickRate(),
                unit: "ticks/sec",
                description: "Current server tick rate");
            
            TickOverruns = _meter.CreateCounter<long>(
                "server.tick.overrun.count",
                unit: "overruns",
                description: "Number of ticks exceeding target duration");
            
            // Player & Session Metrics
            PlayersConnected = _meter.CreateUpDownCounter<int>(
                "server.players.connected",
                unit: "players",
                description: "Current number of connected players");
            
            PlayersMax = _meter.CreateObservableGauge(
                "server.players.max",
                () => _maxPlayers,
                unit: "players",
                description: "Maximum concurrent players");
            
            PlayerJoins = _meter.CreateCounter<long>(
                "server.players.joins",
                unit: "players",
                description: "Total player joins");
            
            PlayerLeaves = _meter.CreateCounter<long>(
                "server.players.leaves",
                unit: "players",
                description: "Total player leaves");
            
            PlayerPing = _meter.CreateHistogram<double>(
                "server.player.ping",
                unit: "ms",
                description: "Player latency");
            
            // World & Simulation Metrics
            EntitiesActive = _meter.CreateObservableGauge(
                "server.entities.active",
                () => _entitiesActive,
                unit: "entities",
                description: "Current number of active entities");
            
            EntitiesSpawned = _meter.CreateCounter<long>(
                "server.entities.spawned",
                unit: "entities",
                description: "Total entities spawned");
            
            EntitiesDespawned = _meter.CreateCounter<long>(
                "server.entities.despawned",
                unit: "entities",
                description: "Total entities despawned");
            
            // Error & Stability Metrics
            ErrorsTotal = _meter.CreateCounter<long>(
                "server.errors.total",
                unit: "errors",
                description: "Total number of errors");
            
            DisconnectsTotal = _meter.CreateCounter<long>(
                "server.disconnects.total",
                unit: "disconnects",
                description: "Total number of disconnects");
            
            Uptime = _meter.CreateObservableGauge(
                "server.uptime",
                () => _uptimeStopwatch.Elapsed.TotalSeconds,
                unit: "seconds",
                description: "Server uptime in seconds");
        }
        
        // Thread-safe network metric recording
        public void RecordBytesReceived(long bytes)
        {
            Interlocked.Add(ref _networkBytesIn, bytes);
            NetworkBytesIn.Add(bytes);
        }
        
        public void RecordBytesSent(long bytes)
        {
            Interlocked.Add(ref _networkBytesOut, bytes);
            NetworkBytesOut.Add(bytes);
        }
        
        public void RecordPacketReceived()
        {
            Interlocked.Increment(ref _networkPacketsIn);
            NetworkPacketsIn.Add(1);
        }
        
        public void RecordPacketSent()
        {
            Interlocked.Increment(ref _networkPacketsOut);
            NetworkPacketsOut.Add(1);
        }
        
        // Player metrics
        public void RecordPlayerJoin()
        {
            var current = Interlocked.Increment(ref _playersConnected);
            PlayersConnected.Add(1);
            PlayerJoins.Add(1);
            
            // Update max players
            var max = _maxPlayers;
            while (current > max)
            {
                var prev = Interlocked.CompareExchange(ref _maxPlayers, current, max);
                if (prev == max) break;
                max = _maxPlayers;
            }
        }
        
        public void RecordPlayerLeave()
        {
            Interlocked.Decrement(ref _playersConnected);
            PlayersConnected.Add(-1);
            PlayerLeaves.Add(1);
        }
        
        // Entity metrics
        public void UpdateEntitiesActive(int count)
        {
            Interlocked.Exchange(ref _entitiesActive, count);
        }
        
        public void RecordEntitySpawned()
        {
            EntitiesSpawned.Add(1);
        }
        
        public void RecordEntityDespawned()
        {
            EntitiesDespawned.Add(1);
        }
        
        // GC tracking
        private int _lastGen0Count;
        private int _lastGen1Count;
        private int _lastGen2Count;
        
        public void UpdateGcCollections()
        {
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            
            var gen0Diff = gen0 - _lastGen0Count;
            var gen1Diff = gen1 - _lastGen1Count;
            var gen2Diff = gen2 - _lastGen2Count;
            
            if (gen0Diff > 0) GcCollections.Add(gen0Diff, new KeyValuePair<string, object?>("generation", 0));
            if (gen1Diff > 0) GcCollections.Add(gen1Diff, new KeyValuePair<string, object?>("generation", 1));
            if (gen2Diff > 0) GcCollections.Add(gen2Diff, new KeyValuePair<string, object?>("generation", 2));
            
            _lastGen0Count = gen0;
            _lastGen1Count = gen1;
            _lastGen2Count = gen2;
        }
        
        private double GetCpuUsage()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var currentTotalProcessorTime = _process.TotalProcessorTime;
                
                var timeDiff = (currentTime - _lastCpuTime).TotalMilliseconds;
                var processorTimeDiff = (currentTotalProcessorTime - _lastTotalProcessorTime).TotalMilliseconds;
                
                _lastCpuTime = currentTime;
                _lastTotalProcessorTime = currentTotalProcessorTime;
                
                if (timeDiff > 0)
                {
                    var cpuUsage = (processorTimeDiff / timeDiff) * 100.0;
                    // Normalize per core to show percentage of a single core (0-100%)
                    // This represents average CPU usage across all cores
                    return cpuUsage / Environment.ProcessorCount;
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash - CPU metrics are non-critical
                System.Diagnostics.Debug.WriteLine($"Error calculating CPU usage: {ex.Message}");
            }
            
            return 0;
        }
        
        private double _currentTickRate;
        
        public void UpdateTickRate(double tickRate)
        {
            _currentTickRate = tickRate;
        }
        
        private double GetCurrentTickRate()
        {
            return _currentTickRate;
        }
        
        public void Dispose()
        {
            _meter?.Dispose();
        }
    }
}
