using System.Reflection;
using Frimerki.Services.Common;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase {
    private readonly ILogger<HealthController> _logger;
    private readonly INowProvider _nowProvider;

    // Assembly information - cached at startup since it cannot change during runtime
    private static readonly string Version;
    private static readonly string ProductName;
    private static readonly string Description;

    static HealthController() {
        var assembly = Assembly.GetEntryAssembly();
        Version = assembly?.GetName().Version?.ToString() ?? "Unknown";
        ProductName = assembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Frímerki";
        Description = assembly?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "Lightweight Email Server";
    }

    public HealthController(ILogger<HealthController> logger, INowProvider nowProvider) {
        _logger = logger;
        _nowProvider = nowProvider;
    }

    [HttpGet]
    public IActionResult GetHealth() {
        var response = new {
            Status = "Healthy",
            Server = "Frímerki Email Server",
            Version = Version,
            Timestamp = _nowProvider.UtcNow,
            Framework = ".NET 8"
        };

        _logger.LogInformation("Health check requested");
        return Ok(response);
    }

    [HttpGet("info")]
    public IActionResult GetServerInfo() {
        var response = new {
            Name = ProductName,
            Description = Description,
            Version = Version,
            Framework = ".NET 8",
            Database = "SQLite",
            Protocols = new {
                SMTP = new { Enabled = true, Ports = new[] { 25, 587, 465 } },
                IMAP = new { Enabled = true, Ports = new[] { 143, 993 } },
                POP3 = new { Enabled = true, Ports = new[] { 110, 995 } }
            },
            Features = new[] {
                "Email Routing",
                "IMAP4rev1 Support",
                "Real-time Notifications",
                "Web Management Interface",
                "DKIM Signing",
                "Full-text Search"
            }
        };

        return Ok(response);
    }
}
