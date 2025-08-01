using Frimerki.Models.DTOs;
using Frimerki.Services.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Frimerki.Server.Controllers;

[ApiController]
[Route("api/domains")]
// TODO: Add proper authorization when authentication is implemented
public class DomainsController : ControllerBase {
    private readonly IDomainService _domainService;
    private readonly ILogger<DomainsController> _logger;

    public DomainsController(IDomainService domainService, ILogger<DomainsController> logger) {
        _domainService = domainService;
        _logger = logger;
    }

    /// <summary>
    /// List domains (HostAdmin sees all, DomainAdmin sees own)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DomainListResponse>> GetDomains() {
        try {
            // TODO: Get actual user role and domain from authentication context
            // For now, simulate HostAdmin access (sees all domains)
            var userRole = "HostAdmin"; // This would come from JWT claims
            var userDomainId = (int?)null; // This would come from JWT claims for DomainAdmin

            var domains = await _domainService.GetDomainsAsync(userRole, userDomainId);
            return Ok(domains);
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving domains");
            return StatusCode(500, new { error = "Failed to retrieve domains" });
        }
    }

    /// <summary>
    /// Get specific domain by name
    /// </summary>
    [HttpGet("{domainName}")]
    public async Task<ActionResult<DomainResponse>> GetDomain(string domainName) {
        try {
            var domain = await _domainService.GetDomainByNameAsync(domainName);
            return Ok(domain);
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to retrieve domain" });
        }
    }

    /// <summary>
    /// Add new domain (HostAdmin only)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<DomainResponse>> CreateDomain([FromBody] DomainRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            // TODO: Check if user is HostAdmin
            var domain = await _domainService.CreateDomainAsync(request);

            _logger.LogInformation("Domain '{DomainName}' created successfully", request.Name);
            return CreatedAtAction(nameof(GetDomain), new { domainName = domain.Name }, domain);
        } catch (InvalidOperationException ex) {
            return Conflict(new { error = ex.Message });
        } catch (ArgumentException ex) {
            return BadRequest(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error creating domain {DomainName}", request.Name);
            return StatusCode(500, new { error = "Failed to create domain" });
        }
    }

    /// <summary>
    /// Update domain (HostAdmin only)
    /// </summary>
    [HttpPut("{domainName}")]
    public async Task<ActionResult<DomainResponse>> UpdateDomain(string domainName, [FromBody] DomainUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            // TODO: Check if user is HostAdmin
            var domain = await _domainService.UpdateDomainAsync(domainName, request);

            _logger.LogInformation("Domain '{DomainName}' updated successfully", domainName);
            return Ok(domain);
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error updating domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to update domain" });
        }
    }

    /// <summary>
    /// Partially update domain (HostAdmin only)
    /// </summary>
    [HttpPatch("{domainName}")]
    public async Task<ActionResult<DomainResponse>> PatchDomain(string domainName, [FromBody] DomainUpdateRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            // TODO: Check if user is HostAdmin
            var domain = await _domainService.UpdateDomainAsync(domainName, request);

            _logger.LogInformation("Domain '{DomainName}' patched successfully", domainName);
            return Ok(domain);
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error patching domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to patch domain" });
        }
    }

    /// <summary>
    /// Delete domain (HostAdmin only)
    /// </summary>
    [HttpDelete("{domainName}")]
    public async Task<IActionResult> DeleteDomain(string domainName) {
        try {
            // TODO: Check if user is HostAdmin
            await _domainService.DeleteDomainAsync(domainName);

            _logger.LogInformation("Domain '{DomainName}' deleted successfully", domainName);
            return NoContent();
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            return Conflict(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error deleting domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to delete domain" });
        }
    }

    /// <summary>
    /// Get DKIM public key for DNS setup
    /// </summary>
    [HttpGet("{domainName}/dkim")]
    public async Task<ActionResult<DkimKeyResponse>> GetDkimKey(string domainName) {
        try {
            var dkimKey = await _domainService.GetDkimKeyAsync(domainName);
            return Ok(dkimKey);
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error retrieving DKIM key for domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to retrieve DKIM key" });
        }
    }

    /// <summary>
    /// Generate new DKIM key pair
    /// </summary>
    [HttpPost("{domainName}/dkim")]
    public async Task<ActionResult<DkimKeyResponse>> GenerateDkimKey(string domainName, [FromBody] GenerateDkimKeyRequest request) {
        try {
            if (!ModelState.IsValid) {
                return BadRequest(ModelState);
            }

            // TODO: Check if user has permission to manage this domain
            var dkimKey = await _domainService.GenerateDkimKeyAsync(domainName, request);

            _logger.LogInformation("DKIM key generated for domain '{DomainName}' with selector '{Selector}'",
                domainName, request.Selector);
            return Ok(dkimKey);
        } catch (ArgumentException ex) {
            return NotFound(new { error = ex.Message });
        } catch (InvalidOperationException ex) {
            return Conflict(new { error = ex.Message });
        } catch (Exception ex) {
            _logger.LogError(ex, "Error generating DKIM key for domain {DomainName}", domainName);
            return StatusCode(500, new { error = "Failed to generate DKIM key" });
        }
    }
}
