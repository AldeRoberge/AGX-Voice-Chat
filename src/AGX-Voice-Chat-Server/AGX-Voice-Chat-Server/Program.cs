using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    internal abstract class Program
    {
        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Verbose()
                .CreateLogger();


            Log.Information("Starting AGH Server...");

            // Parse port from command line arguments
            var gamePort = 10515; // Default game port
            var metricsPort = 9090; // Default metrics port

            if (args.Length > 0)
            {
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--port" && i + 1 < args.Length)
                    {
                        if (int.TryParse(args[i + 1], out var customPort))
                        {
                            gamePort = customPort;
                        }
                    }
                    else if (args[i] == "--metrics-port" && i + 1 < args.Length)
                    {
                        if (int.TryParse(args[i + 1], out var customMetricsPort))
                        {
                            metricsPort = customMetricsPort;
                        }
                    }
                }
            }

            try
            {
                // Build ASP.NET Core host for metrics
                var builder = WebApplication.CreateBuilder(args);

                // Configure Serilog
                builder.Host.UseSerilog();

                // Configure OpenTelemetry metrics
                builder.Services.AddOpenTelemetry()
                    .WithMetrics(metrics =>
                    {
                        metrics
                            .AddMeter("AGH.Server")
                            .AddRuntimeInstrumentation()
                            .AddPrometheusExporter();
                    });

                // Add singleton server instance with game port configuration
                builder.Services.AddSingleton(sp => new Server { GamePort = gamePort });
                builder.Services.AddHostedService<ServerHostedService>();

                // Configure web server to listen on specific port
                var app = builder.Build();
                app.Urls.Add($"http://localhost:{metricsPort}");

                // Map Prometheus scraping endpoint
                app.MapPrometheusScrapingEndpoint();

                Log.Information("Metrics endpoint available at http://localhost:{MetricsPort}/metrics", metricsPort);
                Log.Information("Game server will start on port {GamePort}", gamePort);

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Server terminated unexpectedly.");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }

    /// <summary>
    /// Hosted service to run the game server loop in the background.
    /// </summary>
    public class ServerHostedService : BackgroundService
    {
        private readonly Server _server;

        public ServerHostedService(Server server)
        {
            _server = server;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(() => { _server.Start(_server.GamePort); }, stoppingToken);
        }
    }
}