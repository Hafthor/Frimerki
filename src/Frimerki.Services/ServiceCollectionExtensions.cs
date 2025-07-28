using Frimerki.Services.Server;
using Frimerki.Services.Domain;
using Frimerki.Services.User;
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

        // Add your other services here as they are implemented
        // services.AddScoped<IEmailService, EmailService>();
        // services.AddScoped<IAuthenticationService, AuthenticationService>();

        return services;
    }
}
