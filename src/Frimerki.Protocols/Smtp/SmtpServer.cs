using System.Net;
using System.Net.Sockets;
using Frimerki.Services.Email;
using Frimerki.Services.User;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Smtp;

/// <summary>
/// SMTP server implementation following RFC 5321
/// </summary>
public class SmtpServer(ILogger<SmtpServer> logger, IServiceProvider serviceProvider, int port = 25)
    : BackgroundService, IDisposable {
    private TcpListener _listener;
    private readonly List<Task> _clientTasks = [];
    private readonly object _lock = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            logger.LogInformation("SMTP server started on port {Port}", port);

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var tcpClient = await _listener.AcceptTcpClientAsync(stoppingToken);

                    var clientTask = Task.Run(async () => {
                        using var scope = serviceProvider.CreateScope();
                        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                        var emailDeliveryService = scope.ServiceProvider.GetRequiredService<EmailDeliveryService>();
                        var session = new SmtpSession(tcpClient, userService, emailDeliveryService, logger);

                        try {
                            await session.HandleAsync(stoppingToken);
                        } catch (Exception ex) {
                            logger.LogError(ex, "Error in SMTP session");
                        }
                    }, stoppingToken);

                    lock (_lock) {
                        _clientTasks.Add(clientTask);

                        // Clean up completed tasks
                        _clientTasks.RemoveAll(t => t.IsCompleted);
                    }
                } catch (ObjectDisposedException) {
                    // Server is shutting down
                    break;
                } catch (Exception ex) {
                    logger.LogError(ex, "SMTP server error");
                    await Task.Delay(1000, stoppingToken); // Brief pause before retrying
                }
            }
        } catch (Exception ex) {
            logger.LogError(ex, "SMTP server failed to start");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        logger.LogInformation("Stopping SMTP server...");

        _listener?.Stop();

        // Wait for client tasks to complete
        Task[] tasksToWait;
        lock (_lock) {
            tasksToWait = [.. _clientTasks];
        }

        if (tasksToWait.Length > 0) {
            try {
                await Task.WhenAll(tasksToWait).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            } catch (TimeoutException) {
                logger.LogWarning("Some SMTP client sessions did not complete within timeout");
            }
        }

        await base.StopAsync(cancellationToken);
        logger.LogInformation("SMTP server stopped");
    }

    public new void Dispose() {
        _listener?.Stop();
        base.Dispose();
    }
}
