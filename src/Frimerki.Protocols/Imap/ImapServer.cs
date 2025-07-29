using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Frimerki.Services.User;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;

namespace Frimerki.Protocols.Imap;

/// <summary>
/// IMAP server that listens for client connections
/// </summary>
public class ImapServer : BackgroundService {
    private readonly ILogger<ImapServer> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener? _listener;
    private int _port;

    public ImapServer(
        ILogger<ImapServer> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider) {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _port = _configuration.GetValue<int>("Ports:IMAP", 143);
    }

    /// <summary>
    /// Constructor for testing with custom port
    /// </summary>
    public ImapServer(
        ILogger<ImapServer> logger,
        IServiceProvider serviceProvider,
        int port) {
        _logger = logger;
        _configuration = null!;
        _serviceProvider = serviceProvider;
        _port = port;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();

            _logger.LogInformation("IMAP server started on port {Port}", _port);

            while (!stoppingToken.IsCancellationRequested) {
                try {
                    var client = await _listener.AcceptTcpClientAsync();

                    // Handle client connection in background task
                    _ = Task.Run(async () => {
                        using var scope = _serviceProvider.CreateScope();
                        var sessionLogger = scope.ServiceProvider.GetRequiredService<ILogger<ImapSession>>();
                        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
                        var folderService = scope.ServiceProvider.GetRequiredService<IFolderService>();
                        var messageService = scope.ServiceProvider.GetRequiredService<IMessageService>();

                        var session = new ImapSession(client, sessionLogger, userService, folderService, messageService);
                        await session.HandleSessionAsync();
                    }, stoppingToken);

                } catch (ObjectDisposedException) {
                    // Server is shutting down
                    break;
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error accepting IMAP client connection");
                }
            }
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
