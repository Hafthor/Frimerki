using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly ILogger<HealthController> _logger;

    public HealthController(ILogger<HealthController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public IActionResult GetHealth()
    {
        var response = new
        {
            Status = "Healthy",
            Server = "Frímerki Email Server",
            Version = "1.0.0-alpha",
            Timestamp = DateTime.UtcNow,
            Framework = ".NET 8"
        };

        _logger.LogInformation("Health check requested");
        return Ok(response);
    }

    [HttpGet("info")]
    public IActionResult GetServerInfo()
    {
        var response = new
        {
            Name = "Frímerki",
            Description = "Lightweight Email Server",
            Version = "1.0.0-alpha",
            Framework = ".NET 8",
            Database = "SQLite",
            Protocols = new
            {
                SMTP = new { Enabled = true, Ports = new[] { 25, 587, 465 } },
                IMAP = new { Enabled = true, Ports = new[] { 143, 993 } },
                POP3 = new { Enabled = true, Ports = new[] { 110, 995 } }
            },
            Features = new[]
            {
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
