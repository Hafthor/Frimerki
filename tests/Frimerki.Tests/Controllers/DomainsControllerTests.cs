using Frimerki.Models.DTOs;
using Frimerki.Server.Controllers;
using Frimerki.Services.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Frimerki.Tests.Controllers;

public class DomainsControllerTests {
    private readonly DomainsController _controller;
    private readonly MockDomainServiceForController _mockDomainService;

    public DomainsControllerTests() {
        _mockDomainService = new MockDomainServiceForController();
        var logger = NullLogger<DomainsController>.Instance;
        _controller = new DomainsController(_mockDomainService, logger);
    }

    [Fact]
    public async Task GetDomains_Success_ReturnsOkWithDomains() {
        // Arrange
        var expectedResponse = new DomainListResponse {
            Domains = [
                new DomainResponse { Name = "example.com", IsActive = true },
                new DomainResponse { Name = "test.org", IsActive = true }
            ]
        };
        _mockDomainService.SetDomainsResponse(expectedResponse);

        // Act
        var result = await _controller.GetDomains();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DomainListResponse>(okResult.Value);
        Assert.Equal(2, response.Domains.Count);
    }

    [Fact]
    public async Task GetDomains_ServiceThrows_ReturnsInternalServerError() {
        // Arrange
        _mockDomainService.ShouldThrowOnGetDomains = true;

        // Act
        var result = await _controller.GetDomains();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetDomain_ExistingDomain_ReturnsOkWithDomain() {
        // Arrange
        var expectedDomain = new DomainResponse { Name = "example.com", IsActive = true };
        _mockDomainService.SetDomainResponse(expectedDomain);

        // Act
        var result = await _controller.GetDomain("example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var domain = Assert.IsType<DomainResponse>(okResult.Value);
        Assert.Equal("example.com", domain.Name);
    }

    [Fact]
    public async Task GetDomain_NonExistentDomain_ReturnsNotFound() {
        // Arrange
        _mockDomainService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.GetDomain("nonexistent.com");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task GetDomain_ServiceThrows_ReturnsInternalServerError() {
        // Arrange
        _mockDomainService.ShouldThrowGenericException = true;

        // Act
        var result = await _controller.GetDomain("example.com");

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task CreateDomain_ValidRequest_ReturnsCreated() {
        // Arrange
        var request = new DomainRequest { Name = "newdomain.com" };
        var expectedResponse = new DomainResponse { Name = "newdomain.com", IsActive = true };
        _mockDomainService.SetCreateDomainResponse(new CreateDomainResponse {
            Name = "newdomain.com",
            IsActive = true
        });

        // Act
        var result = await _controller.CreateDomain(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(DomainsController.GetDomain), createdResult.ActionName);
        Assert.Equal("newdomain.com", ((dynamic)createdResult.RouteValues!)["domainName"]);
    }

    [Fact]
    public async Task CreateDomain_DomainAlreadyExists_ReturnsConflict() {
        // Arrange
        var request = new DomainRequest { Name = "existing.com" };
        _mockDomainService.ShouldThrowInvalidOperationException = true;

        // Act
        var result = await _controller.CreateDomain(request);

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.NotNull(conflictResult.Value);
    }

    [Fact]
    public async Task CreateDomain_InvalidRequest_ReturnsBadRequest() {
        // Arrange
        var request = new DomainRequest { Name = "invalid" };
        _mockDomainService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.CreateDomain(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task UpdateDomain_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new DomainUpdateRequest { Name = "updated.com" };
        var expectedResponse = new DomainResponse { Name = "updated.com", IsActive = true };
        _mockDomainService.SetDomainResponse(expectedResponse);

        // Act
        var result = await _controller.UpdateDomain("example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var domain = Assert.IsType<DomainResponse>(okResult.Value);
        Assert.Equal("updated.com", domain.Name);
    }

    [Fact]
    public async Task UpdateDomain_NonExistentDomain_ReturnsNotFound() {
        // Arrange
        var request = new DomainUpdateRequest { Name = "updated.com" };
        _mockDomainService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.UpdateDomain("nonexistent.com", request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task PatchDomain_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new DomainUpdateRequest { IsActive = false };
        var expectedResponse = new DomainResponse { Name = "example.com", IsActive = false };
        _mockDomainService.SetDomainResponse(expectedResponse);

        // Act
        var result = await _controller.PatchDomain("example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var domain = Assert.IsType<DomainResponse>(okResult.Value);
        Assert.Equal("example.com", domain.Name);
        Assert.False(domain.IsActive);
    }

    [Fact]
    public async Task DeleteDomain_ExistingDomain_ReturnsNoContent() {
        // Arrange
        _mockDomainService.DeleteShouldSucceed = true;

        // Act
        var result = await _controller.DeleteDomain("example.com");

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteDomain_NonExistentDomain_ReturnsNotFound() {
        // Arrange
        _mockDomainService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.DeleteDomain("nonexistent.com");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task DeleteDomain_DomainHasUsers_ReturnsConflict() {
        // Arrange
        _mockDomainService.ShouldThrowInvalidOperationException = true;

        // Act
        var result = await _controller.DeleteDomain("example.com");

        // Assert
        var conflictResult = Assert.IsType<ConflictObjectResult>(result);
        Assert.NotNull(conflictResult.Value);
    }

    [Fact]
    public async Task GetDkimKey_ExistingKey_ReturnsOk() {
        // Arrange
        var expectedKey = new DkimKeyResponse {
            Selector = "default",
            PublicKey = "test-key",
            IsActive = true
        };
        _mockDomainService.SetDkimKeyResponse(expectedKey);

        // Act
        var result = await _controller.GetDkimKey("example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var key = Assert.IsType<DkimKeyResponse>(okResult.Value);
        Assert.Equal("default", key.Selector);
    }

    [Fact]
    public async Task GenerateDkimKey_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new GenerateDkimKeyRequest { Selector = "new", KeySize = 2048 };
        var expectedKey = new DkimKeyResponse {
            Selector = "new",
            PublicKey = "generated-key",
            IsActive = true
        };
        _mockDomainService.SetDkimKeyResponse(expectedKey);

        // Act
        var result = await _controller.GenerateDkimKey("example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var key = Assert.IsType<DkimKeyResponse>(okResult.Value);
        Assert.Equal("new", key.Selector);
    }
}

// Mock service for controller testing
public class MockDomainServiceForController : IDomainService {
    public bool ShouldThrowOnGetDomains { get; set; }
    public bool ShouldThrowArgumentException { get; set; }
    public bool ShouldThrowInvalidOperationException { get; set; }
    public bool ShouldThrowGenericException { get; set; }
    public bool DeleteShouldSucceed { get; set; }

    private DomainListResponse? _domainsResponse;
    private DomainResponse? _domainResponse;
    private CreateDomainResponse? _createDomainResponse;
    private DkimKeyResponse? _dkimKeyResponse;

    public void SetDomainsResponse(DomainListResponse response) => _domainsResponse = response;
    public void SetDomainResponse(DomainResponse response) => _domainResponse = response;
    public void SetCreateDomainResponse(CreateDomainResponse response) => _createDomainResponse = response;
    public void SetDkimKeyResponse(DkimKeyResponse response) => _dkimKeyResponse = response;

    public Task<DomainListResponse> GetDomainsAsync(string userRole = "", int userDomainId = 0) {
        if (ShouldThrowOnGetDomains) {
            throw new Exception("Service error");
        }
        return Task.FromResult(_domainsResponse ?? new DomainListResponse { Domains = [] });
    }

    public Task<DomainResponse> GetDomainByNameAsync(string domainName) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }
        if (ShouldThrowGenericException) {
            throw new Exception("Service error");
        }
        return Task.FromResult(_domainResponse ?? new DomainResponse { Name = domainName });
    }

    public Task<CreateDomainResponse> CreateDomainAsync(DomainRequest request) {
        if (ShouldThrowInvalidOperationException) {
            throw new InvalidOperationException($"Domain '{request.Name}' already exists");
        }
        if (ShouldThrowArgumentException) {
            throw new ArgumentException("Invalid request");
        }
        return Task.FromResult(_createDomainResponse ?? new CreateDomainResponse { Name = request.Name });
    }

    public Task<DomainResponse> UpdateDomainAsync(string domainName, DomainUpdateRequest request) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }
        return Task.FromResult(_domainResponse ?? new DomainResponse { Name = domainName });
    }

    public Task DeleteDomainAsync(string domainName) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }
        if (ShouldThrowInvalidOperationException) {
            throw new InvalidOperationException("Domain has users");
        }
        if (!DeleteShouldSucceed) {
            throw new Exception("Service error");
        }
        return Task.CompletedTask;
    }

    public Task<DkimKeyResponse> GetDkimKeyAsync(string domainName) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"No DKIM key found for domain '{domainName}'");
        }
        return Task.FromResult(_dkimKeyResponse ?? new DkimKeyResponse { Selector = "default" });
    }

    public Task<DkimKeyResponse> GenerateDkimKeyAsync(string domainName, GenerateDkimKeyRequest request) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"Domain '{domainName}' not found");
        }
        if (ShouldThrowInvalidOperationException) {
            throw new InvalidOperationException("DKIM key already exists");
        }
        return Task.FromResult(_dkimKeyResponse ?? new DkimKeyResponse { Selector = request.Selector });
    }
}
