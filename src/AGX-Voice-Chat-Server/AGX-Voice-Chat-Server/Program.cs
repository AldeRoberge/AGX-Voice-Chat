using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using Serilog;

namespace AGX_Voice_Chat_Server
{
    internal abstract class Program
    {
        private const int VoicePort = 10515;
        private const int MetricsPort = 9090;

        private static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Verbose()
                .CreateLogger();

            Log.Information("Starting AGX Voice Server...");

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

                // Add singleton server instance with voice port configuration
                builder.Services.AddSingleton(sp => new Server { VoicePort = VoicePort });
                builder.Services.AddHostedService<ServerHostedService>();

                // Configure web server to listen on specific port
                var app = builder.Build();
                app.Urls.Add($"http://localhost:{MetricsPort}");

                app.Map("/", () => "AGX Voice Server Metrics Endpoint");

                // Map Prometheus scraping endpoint
                app.MapPrometheusScrapingEndpoint();

                Log.Information("Metrics HTTP endpoint available at {Url}.", $"http://localhost:{MetricsPort}/metrics");
                Log.Information("Voice server (UDP) available on port {VoicePort}.", VoicePort);

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
}