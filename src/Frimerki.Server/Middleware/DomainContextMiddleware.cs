using System.Text.RegularExpressions;
using Frimerki.Models;
using Frimerki.Services.Domain;

namespace Frimerki.Server.Middleware;

/// <summary>
/// Middleware to extract and validate domain context from incoming requests
/// </summary>
public class DomainContextMiddleware(
    RequestDelegate next,
    ILogger<DomainContextMiddleware> logger) {
    // Regex to extract email addresses from various contexts
    private static readonly Regex EmailRegex = new(Constants.ValidEmailRegex,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task InvokeAsync(HttpContext context) {
        var domain = await ExtractDomainFromRequestAsync(context);

        if (!string.IsNullOrEmpty(domain)) {
            // Validate domain exists and is active using scoped service
            using var scope = context.RequestServices.CreateScope();
            var domainRegistry = scope.ServiceProvider.GetRequiredService<IDomainRegistryService>();

            var domainExists = await domainRegistry.DomainExistsAsync(domain);
            if (domainExists) {
                context.Items["Domain"] = domain;
                logger.LogDebug("Domain context set to {Domain} for request {Path}",
                    domain, context.Request.Path);
            } else {
                logger.LogWarning("Request for unknown domain {Domain} from {RemoteIp}",
                    domain, context.Connection.RemoteIpAddress);
            }
        }

        await next(context);
    }

    private async Task<string> ExtractDomainFromRequestAsync(HttpContext context) {
        // Try to extract domain from different sources

        // 1. From query parameters (for API requests)
        if (context.Request.Query.TryGetValue("domain", out var domainFromQuery)) {
            return domainFromQuery.ToString().ToLower();
        }

        // 2. From request headers (useful for email protocols)
        if (context.Request.Headers.TryGetValue("X-Domain", out var domainFromHeader)) {
            return domainFromHeader.ToString().ToLower();
        }

        // 3. From Host header (subdomain-based routing)
        var host = context.Request.Host.Host;
        if (!string.IsNullOrEmpty(host) && host != "localhost" && !IsIpAddress(host)) {
            // Extract subdomain if present (e.g., mail.example.com -> example.com)
            var parts = host.Split('.');
            if (parts.Length >= 2) {
                var domain = string.Join(".", parts.Skip(parts.Length >= 3 ? 1 : 0));
                return domain.ToLower();
            }
        }

        // 4. From request body for email-related endpoints
        if (context.Request.Method == "POST" && context.Request.ContentType?.Contains("application/json") == true) {
            var domain = await ExtractDomainFromJsonBodyAsync(context);
            if (!string.IsNullOrEmpty(domain)) {
                return domain;
            }
        }

        return null;
    }

    private async Task<string> ExtractDomainFromJsonBodyAsync(HttpContext context) {
        try {
            // Enable buffering so we can read the body multiple times
            context.Request.EnableBuffering();

            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();

            // Reset the stream position for the next middleware
            context.Request.Body.Position = 0;

            // Look for email patterns in the JSON
            var matches = EmailRegex.Matches(body);
            foreach (Match match in matches) {
                if (match.Groups.Count >= 3) {
                    var domain = match.Groups[2].Value.ToLower();
                    // We'll validate this domain later in the main InvokeAsync method
                    // For now, return the first domain found
                    return domain;
                }
            }
        } catch (Exception ex) {
            logger.LogWarning(ex, "Error extracting domain from request body");
        }

        return null;
    }

    private static bool IsIpAddress(string host) {
        return System.Net.IPAddress.TryParse(host, out _);
    }
}

/// <summary>
/// Extension methods for domain context
/// </summary>
public static class DomainContextExtensions {
    public static string GetDomain(this HttpContext context) {
        return context.Items.TryGetValue("Domain", out var domain) ? domain as string : null;
    }

    public static void SetDomain(this HttpContext context, string domain) {
        context.Items["Domain"] = domain;
    }
}

/// <summary>
/// Extension method to register the middleware
/// </summary>
public static class DomainContextMiddlewareExtensions {
    public static IApplicationBuilder UseDomainContext(this IApplicationBuilder builder) {
        return builder.UseMiddleware<DomainContextMiddleware>();
    }
}
