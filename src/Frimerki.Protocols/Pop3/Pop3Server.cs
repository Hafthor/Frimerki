using System.Net;
using System.Net.Sockets;
using Frimerki.Services.Message;
using Frimerki.Services.Session;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Pop3;

public class Pop3Server : BackgroundService {
    private readonly ILogger<Pop3Server> _logger;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener _listener;
    private readonly int _port;

    public Pop3Server(
        ILogger<Pop3Server> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _port = configuration.GetValue("Ports:POP3", 110);
    }

    /// <summary>
    /// Constructor for testing with custom port
    /// </summary>
    public Pop3Server(ILogger<Pop3Server> logger, IServiceProvider serviceProvider, int port) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            using (_listener = new TcpListener(IPAddress.Any, _port)) {
                _listener.Start();
                _logger.LogInformation("POP3 server started on port {Port}", _port);

                while (!stoppingToken.IsCancellationRequested) {
                    try {
                        var tcpClient = await _listener.AcceptTcpClientAsync(stoppingToken);
                        _ = Task.Run(async () => {
                            using (tcpClient) {
                                using var scope = _serviceProvider.CreateScope();
                                var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
                                var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
                                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Pop3Session>>();

                                var session = new Pop3Session(sessionService, messageService, logger);
                                await session.HandleAsync(tcpClient, stoppingToken);
                            }
                        }, stoppingToken);
                    } catch (ObjectDisposedException) {
                        // Expected when stopping
                        break;
                    } catch (OperationCanceledException) {
                        // Expected when cancellation token is triggered
                        break;
                    } catch (Exception ex) {
                        _logger.LogError(ex, "POP3 server error");
                    }
                }
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "POP3 server failed to start");
        } finally {
            _logger.LogInformation("POP3 server stopped");
        }
    }
}
