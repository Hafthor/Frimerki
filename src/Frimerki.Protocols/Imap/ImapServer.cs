using System.Net;
using System.Net.Sockets;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// IMAP server that listens for client connections
/// </summary>
public class ImapServer : BackgroundService {
    private readonly ILogger<ImapServer> _logger;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener _listener;
    private readonly int _port;

    public ImapServer(
        ILogger<ImapServer> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _port = configuration.GetValue("Ports:IMAP", 143);
    }

    /// <summary>
    /// Constructor for testing with custom port
    /// </summary>
    public ImapServer(
        ILogger<ImapServer> logger,
        IServiceProvider serviceProvider,
        int port) {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            _logger.LogInformation("IMAP: ExecuteAsync starting...");

            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _logger.LogInformation("IMAP: TcpListener started");

            _logger.LogInformation("IMAP server started on port {Port}", _port);

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    _logger.LogInformation("IMAP: Waiting for client connections...");
                    var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                    _logger.LogInformation("IMAP: Client connected from {ClientEndpoint}", client.Client.RemoteEndPoint);

                    // Handle client connection in background task
                    _ = Task.Run(async () => {
                        try {
                            _logger.LogInformation("IMAP: Handling new client connection");

                            using var scope = _serviceProvider.CreateScope();
                            _logger.LogInformation("IMAP: Created service scope");

                            var sessionLogger = scope.ServiceProvider.GetRequiredService<ILogger<ImapSession>>();
                            _logger.LogInformation("IMAP: Retrieved session logger");

                            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                            _logger.LogInformation("IMAP: Retrieved user service");

                            var folderService = scope.ServiceProvider.GetRequiredService<IFolderService>();
                            _logger.LogInformation("IMAP: Retrieved folder service");

                            var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();
                            _logger.LogInformation("IMAP: Retrieved message service");

                            var session = new ImapSession(client, sessionLogger, userService, messageService);
                            _logger.LogInformation("IMAP: Created session, starting session handling");

                            await session.HandleSessionAsync();

                            _logger.LogInformation("IMAP: Session handling completed");
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error handling IMAP client session");
                        }
                    }, stoppingToken);

                } catch (ObjectDisposedException) {
                    // Server is shutting down
                    _logger.LogInformation("IMAP: Server shutting down (ObjectDisposedException)");
                    break;
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error accepting IMAP client connection");
                }
            }

            _logger.LogInformation("IMAP: Main server loop exited");
        } catch (Exception ex) {
            _logger.LogError(ex, "IMAP server error");
        } finally {
            _listener?.Stop();
            _logger.LogInformation("IMAP server stopped");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken) {
        _logger.LogInformation("Stopping IMAP server...");
        _listener?.Stop();
        await base.StopAsync(cancellationToken);
    }
}
