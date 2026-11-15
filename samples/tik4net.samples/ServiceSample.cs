using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tik4net.Objects;
using tik4net.Objects.System;

namespace tik4net.Samples;

/// <summary>
/// A long-running service on the .NET generic host: one setup from a connection string, one connection kept open,
/// and a new one when the old one is lost. The pattern the wiki's <i>Building an application</i> page describes.
/// </summary>
public static class ServiceSample
{
    /// <summary>Runs the host until Ctrl+C.</summary>
    public static Task RunAsync(SampleOptions options)
    {
        var builder = Host.CreateApplicationBuilder();

        // One setup for the application: it holds no connection and no lock, so a singleton is right. Built from a
        // connection string, its password can be a reference (password=env:ROUTER_PW) rather than the password.
        builder.Services.AddSingleton(options.Setup());
        builder.Services.AddHostedService<RouterMonitorService>();

        return builder.Build().RunAsync();
    }
}

/// <summary>Reads <c>/system/resource</c> every few seconds over one long-lived connection.</summary>
public sealed class RouterMonitorService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly TikConnectionSetup _setup;
    private readonly ILogger<RouterMonitorService> _log;
    private ITikConnection? _connection;

    /// <summary>Takes the application's setup and a logger from the container.</summary>
    public RouterMonitorService(TikConnectionSetup setup, ILogger<RouterMonitorService> log)
    {
        _setup = setup;
        _log = log;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await ConnectionAsync(stoppingToken);
                var resource = await connection.LoadSingleAsync<SystemResource>(stoppingToken);
                _log.LogInformation("cpu {Cpu} %, free memory {Free} B, up {Uptime}",
                    resource.CpuLoad.Value, resource.FreeMemory.Value, resource.Uptime.Value);
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (IsConnectionLost(ex))
            {
                // The session is gone: drop the connection and open a new one. A read is safe to repeat; a write
                // interrupted this way may or may not have run on the router, so it is not retried blindly.
                _log.LogWarning("connection lost ({Reason}); reconnecting in {Delay}", ex.Message, ReconnectDelay);
                DropConnection();
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                _log.LogWarning("router unreachable ({Reason}); retrying in {Delay}", ex.Message, ReconnectDelay);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }
    }

    /// <inheritdoc/>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        DropConnection();
    }

    // The one connection, opened on first use and after a loss. A connection is safe to share between threads; more
    // connections to one router are not faster, so a service holds one per router.
    private async Task<ITikConnection> ConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connection != null)
            return _connection;
        _connection = await _setup.CreateAsync(cancellationToken);
        _log.LogInformation("connected to {Router}", _setup.ToString());
        return _connection;
    }

    private void DropConnection()
    {
        _connection?.Dispose();
        _connection = null;
    }

    // What says "this session is over, open another": the connection closed or dropped under a command, or the
    // router ended it. Everything else (a trap, a missing field) is about the command and leaves the connection usable.
    private static bool IsConnectionLost(Exception ex)
        => ex is TikConnectionNotOpenException
            or TikConnectionSessionClosedException
            or TikCommandFatalException
            or TikConnectionReceiveTimeoutException;
}
