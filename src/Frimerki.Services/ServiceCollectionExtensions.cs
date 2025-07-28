using Frimerki.Services.Server;
using Microsoft.Extensions.DependencyInjection;

namespace Frimerki.Services;

public static class ServiceCollectionExtensions {
    public static IServiceCollection AddFrimerkiServices(this IServiceCollection services) {
        // Server management services
        services.AddScoped<IServerService, ServerService>();

        // Add your other services here as they are implemented
        // services.AddScoped<IEmailService, EmailService>();
        // services.AddScoped<IUserService, UserService>();
        // services.AddScoped<IAuthenticationService, AuthenticationService>();

        return services;
    }
}
