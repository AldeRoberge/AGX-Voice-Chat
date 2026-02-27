using Microsoft.Extensions.Hosting;
using Serilog;

namespace AGX_Voice_Chat_Server;

/// <summary>
/// Hosted service to run the voice server loop in the background.
/// </summary>
public class ServerHostedService(Server server) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            Log.Information("Starting voice server loop...");
            await Task.Run(() => server.Start(server.VoicePort, stoppingToken), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Server shutdown requested.");
        }
    }
}