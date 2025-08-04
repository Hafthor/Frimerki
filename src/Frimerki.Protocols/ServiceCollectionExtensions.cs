using Frimerki.Protocols.Imap;
using Frimerki.Protocols.Pop3;
using Frimerki.Protocols.Smtp;
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
    /// Adds POP3 server services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddPop3Server(this IServiceCollection services) {
        services.AddSingleton<IHostedService, Pop3Server>();
        return services;
    }

    /// <summary>
    /// Adds SMTP server services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddSmtpServer(this IServiceCollection services) {
        services.AddSingleton<IHostedService, SmtpServer>();
        return services;
    }
}
