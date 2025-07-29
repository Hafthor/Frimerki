using Frimerki.Protocols.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Frimerki.Protocols;

/// <summary>
/// Extension methods for registering protocol services
/// </summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds IMAP server services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddImapServer(this IServiceCollection services) {
        services.AddSingleton<IHostedService, ImapServer>();
        return services;
    }

    /// <summary>
    /// Adds all email protocol services (future: SMTP, POP3)
    /// </summary>
    public static IServiceCollection AddEmailProtocols(this IServiceCollection services) {
        services.AddImapServer();
        // Future: services.AddSmtpServer();
        // Future: services.AddPop3Server();
        return services;
    }
}
