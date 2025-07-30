using Frimerki.Services.Authentication;
using Frimerki.Services.Common;
using Frimerki.Services.Domain;
using Frimerki.Services.Email;
using Frimerki.Services.Folder;
using Frimerki.Services.Message;
using Frimerki.Services.Server;
using Frimerki.Services.Session;
using Frimerki.Services.User;
using Microsoft.Extensions.DependencyInjection;

namespace Frimerki.Services;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddFrimerkiServices(this IServiceCollection services) {
        // Common services
        services.AddSingleton<INowProvider, SystemNowProvider>();

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

        // Email services
        services.AddScoped<SmtpClientService>();
        services.AddScoped<EmailDeliveryService>();

        return services;
    }
}
