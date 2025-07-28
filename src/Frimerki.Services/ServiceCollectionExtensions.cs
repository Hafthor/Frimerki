using Microsoft.Extensions.DependencyInjection;

namespace Frimerki.Services;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFrimerkiServices(this IServiceCollection services)
    {
        // Add your services here as they are implemented
        // services.AddScoped<IEmailService, EmailService>();
        // services.AddScoped<IUserService, UserService>();
        // services.AddScoped<IAuthenticationService, AuthenticationService>();
        
        return services;
    }
}
