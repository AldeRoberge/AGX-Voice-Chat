using System;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace AGH_VOice_Chat_Client
{
    /// <summary>
    /// Centralized logging configuration with per-subsystem loggers.
    /// Supports runtime log level switching and PlayerID enrichment.
    /// </summary>
    public static class LoggingConfig
    {
        // Per-subsystem loggers - initialized in InitializeLogging() to ensure they use the configured logger
        private static ILogger? _loggingLog;
        private static ILogger? _networkLog;
        private static ILogger? _projectileLog;
        private static ILogger? _worldEditLog;
        private static ILogger? _reconciliationLog;
        private static ILogger? _interpolationLog;
        private static ILogger? _inputLog;
        private static ILogger? _chunksLog;
        private static ILogger? _renderingLog;
        private static ILogger? _chatLog;

        public static ILogger LoggingLog => _loggingLog ?? Log.Logger;
        public static ILogger NetworkLog => _networkLog ?? Log.Logger;
        public static ILogger ProjectileLog => _projectileLog ?? Log.Logger;
        public static ILogger WorldEditLog => _worldEditLog ?? Log.Logger;
        public static ILogger ReconciliationLog => _reconciliationLog ?? Log.Logger;
        public static ILogger InterpolationLog => _interpolationLog ?? Log.Logger;
        public static ILogger InputLog => _inputLog ?? Log.Logger;
        public static ILogger ChunksLog => _chunksLog ?? Log.Logger;
        public static ILogger RenderingLog => _renderingLog ?? Log.Logger;
        public static ILogger ChatLog => _chatLog ?? Log.Logger;

        // Per-subsystem level switches for runtime toggling
        public static readonly LoggingLevelSwitch LoggingLogSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch NetworkSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch ProjectileSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch WorldEditSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch ReconciliationSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch InterpolationSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch InputSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch ChunksSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch RenderingSwitch = new(LogEventLevel.Information);
        public static readonly LoggingLevelSwitch ChatSwitch = new(LogEventLevel.Information);

        // Current player ID for enrichment
        private static Guid? _currentPlayerId;

        public static Guid? CurrentPlayerId
        {
            get => _currentPlayerId;
            set
            {
                _currentPlayerId = value;
                // Log the change
                if (value.HasValue)
                {
                    NetworkLog.Information("Player ID set to {PlayerId}", value.Value);
                }
            }
        }

        /// <summary>
        /// Initializes the global Serilog logger with console and file sinks.
        /// Call this once at application startup.
        /// </summary>
        public static void InitializeLogging()
        {
            var logTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{Subsystem}] [Player:{PlayerId}] {Message:lj}{NewLine}{Exception}";

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .MinimumLevel.Override("Logging", LoggingLogSwitch)
                .MinimumLevel.Override("Network", NetworkSwitch)
                .MinimumLevel.Override("Projectile", ProjectileSwitch)
                .MinimumLevel.Override("WorldEdit", WorldEditSwitch)
                .MinimumLevel.Override("Reconciliation", ReconciliationSwitch)
                .MinimumLevel.Override("Interpolation", InterpolationSwitch)
                .MinimumLevel.Override("Input", InputSwitch)
                .MinimumLevel.Override("Chunks", ChunksSwitch)
                .MinimumLevel.Override("Rendering", RenderingSwitch)
                .MinimumLevel.Override("Chat", ChatSwitch)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("PlayerId", () => CurrentPlayerId?.ToString() ?? "Unknown", destructureObjects: false)
                .WriteTo.Console(
                    outputTemplate: logTemplate,
                    theme: AnsiConsoleTheme.Code,
                    restrictedToMinimumLevel: LogEventLevel.Information)
                .WriteTo.File(
                    path: "logs/client-.txt",
                    outputTemplate: logTemplate,
                    restrictedToMinimumLevel: LogEventLevel.Verbose,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30)
                .CreateLogger();

            // Initialize subsystem loggers AFTER Log.Logger is configured
            _loggingLog = Log.ForContext("Subsystem", "Logging");
            _networkLog = Log.ForContext("Subsystem", "Network");
            _projectileLog = Log.ForContext("Subsystem", "Projectile");
            _worldEditLog = Log.ForContext("Subsystem", "WorldEdit");
            _reconciliationLog = Log.ForContext("Subsystem", "Reconciliation");
            _interpolationLog = Log.ForContext("Subsystem", "Interpolation");
            _inputLog = Log.ForContext("Subsystem", "Input");
            _chunksLog = Log.ForContext("Subsystem", "Chunks");
            _renderingLog = Log.ForContext("Subsystem", "Rendering");
            _chatLog = Log.ForContext("Subsystem", "Chat");

            LoggingLog.Information("Logging system initialized");
        }

        /// <summary>
        /// Sets the log level for a specific subsystem at runtime.
        /// </summary>
        public static bool SetLogLevel(string subsystem, LogEventLevel level)
        {
            var switchToUpdate = subsystem.ToLowerInvariant() switch
            {
                "logging" => LoggingLogSwitch,
                "network" => NetworkSwitch,
                "projectile" => ProjectileSwitch,
                "worldedit" => WorldEditSwitch,
                "reconciliation" => ReconciliationSwitch,
                "interpolation" => InterpolationSwitch,
                "input" => InputSwitch,
                "chunks" => ChunksSwitch,
                "rendering" => RenderingSwitch,
                "chat" => ChatSwitch,
                "all" => null, // Special case to set all
                _ => null
            };

            if (subsystem.ToLowerInvariant() == "all")
            {
                LoggingLogSwitch.MinimumLevel = level;
                NetworkSwitch.MinimumLevel = level;
                ProjectileSwitch.MinimumLevel = level;
                WorldEditSwitch.MinimumLevel = level;
                ReconciliationSwitch.MinimumLevel = level;
                InterpolationSwitch.MinimumLevel = level;
                InputSwitch.MinimumLevel = level;
                ChunksSwitch.MinimumLevel = level;
                RenderingSwitch.MinimumLevel = level;
                ChatSwitch.MinimumLevel = level;
                Log.Information("Set ALL subsystems to {Level}", level);
                return true;
            }
            else if (switchToUpdate != null)
            {
                switchToUpdate.MinimumLevel = level;
                Log.Information("Set {Subsystem} log level to {Level}", subsystem, level);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Closes and flushes the logger. Call on application shutdown.
        /// </summary>
        public static void CloseAndFlush()
        {
            Log.CloseAndFlush();
        }
    }
}