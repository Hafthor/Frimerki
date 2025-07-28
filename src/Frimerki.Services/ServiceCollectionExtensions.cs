using Frimerki.Services.Server;
using Frimerki.Services.Domain;
using Frimerki.Services.User;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.Authentication;
using Frimerki.Services.Session;
using Microsoft.Extensions.DependencyInjection;

namespace Frimerki.Services;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddFrimerkiServices(this IServiceCollection services) {
        // Server management services
        services.AddScoped<IServerService, ServerService>();

        // Domain management services
        services.AddScoped<IDomainService, DomainService>();

        // User management services
        services.AddScoped<IUserService, UserService>();

        // Folder management services
        services.AddScoped<IFolderService, FolderService>();

        // Message management services
        services.AddScoped<IMessageService, MessageService>();

        // Authentication services
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ISessionService, SessionService>();

        // Add your other services here as they are implemented
        // services.AddScoped<IEmailService, EmailService>();

        return services;
    }
}
