using System.Net;
using System.Net.Sockets;
using System.Text;

using Frimerki.Services.Email;
using Frimerki.Services.User;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Smtp;

/// <summary>
/// SMTP server implementation following RFC 5321
/// </summary>
public class SmtpServer : BackgroundService, IDisposable {
    private readonly ILogger<SmtpServer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _port;
    private TcpListener? _listener;
    private readonly List<Task> _clientTasks = [];
    private readonly object _lock = new();

    public SmtpServer(ILogger<SmtpServer> logger, IServiceProvider serviceProvider, int port = 25) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _logger.LogInformation("SMTP server started on port {Port}", _port);

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var tcpClient = await _listener.AcceptTcpClientAsync();

                    var clientTask = Task.Run(async () => {
                        using var scope = _serviceProvider.CreateScope();
                        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                        var emailDeliveryService = scope.ServiceProvider.GetRequiredService<EmailDeliveryService>();
                        var session = new SmtpSession(tcpClient, userService, emailDeliveryService, _logger);

                        try {
                            await session.HandleAsync(stoppingToken);
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error in SMTP session");
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
                    _logger.LogError(ex, "SMTP server error");
                    await Task.Delay(1000, stoppingToken); // Brief pause before retrying
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "SMTP server failed to start");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Stopping SMTP server...");

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
                _logger.LogWarning("Some SMTP client sessions did not complete within timeout");
            }
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("SMTP server stopped");
    }

    public new void Dispose() {
        _listener?.Stop();
        base.Dispose();
    }
}
