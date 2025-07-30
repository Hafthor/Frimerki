using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Frimerki.Models.DTOs;
using Frimerki.Server.Controllers;
using Frimerki.Services.User;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Frimerki.Tests.Controllers;

public class UsersControllerTests {
    private readonly UsersController _controller;
    private readonly MockUserServiceForController _mockUserService;

    public UsersControllerTests() {
        _mockUserService = new MockUserServiceForController();
        var logger = NullLogger<UsersController>.Instance;
        _controller = new UsersController(_mockUserService, logger);
    }

    [Fact]
    public async Task GetUsers_ValidPagination_ReturnsOk() {
        // Arrange
        var expectedResponse = new PaginatedInfo<UserResponse> {
            Items = [
                new UserResponse { Email = "user1@example.com", Username = "user1" },
                new UserResponse { Email = "user2@example.com", Username = "user2" }
            ],
            Skip = 0,
            Take = 50,
            TotalCount = 2
        };
        _mockUserService.SetUsersResponse(expectedResponse);

        // Act
        var result = await _controller.GetUsers(1, 50, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<PaginatedInfo<UserResponse>>(okResult.Value);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task GetUsers_InvalidPageNumber_CorrectsToPag1() {
        // Arrange
        var expectedResponse = new PaginatedInfo<UserResponse> { Items = [], Skip = 0, Take = 50, TotalCount = 0 };
        _mockUserService.SetUsersResponse(expectedResponse);

        // Act
        var result = await _controller.GetUsers(-5, 50, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetUsers_InvalidPageSize_CorrectsTo50() {
        // Arrange
        var expectedResponse = new PaginatedInfo<UserResponse> { Items = [], Skip = 0, Take = 50, TotalCount = 0 };
        _mockUserService.SetUsersResponse(expectedResponse);

        // Act
        var result = await _controller.GetUsers(1, 150, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetUsers_ServiceThrows_ReturnsInternalServerError() {
        // Arrange
        _mockUserService.ShouldThrowOnGetUsers = true;

        // Act
        var result = await _controller.GetUsers();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ValidRequest_ReturnsCreated() {
        // Arrange
        var request = new CreateUserRequest {
            Username = "newuser",
            DomainName = "example.com",
            Password = "password123",
            Role = "User"
        };
        var expectedUser = new UserResponse {
            Email = "newuser@example.com",
            Username = "newuser",
            Role = "User"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(UsersController.GetUser), createdResult.ActionName);
        Assert.Equal("newuser@example.com", ((dynamic)createdResult.RouteValues!)["email"]);
    }

    [Fact]
    public async Task CreateUser_InvalidRequest_ReturnsBadRequest() {
        // Arrange
        var request = new CreateUserRequest {
            Username = "",
            DomainName = "example.com",
            Password = "weak"
        };
        _mockUserService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task CreateUser_ServiceThrows_ReturnsInternalServerError() {
        // Arrange
        var request = new CreateUserRequest {
            Username = "newuser",
            DomainName = "example.com",
            Password = "password123"
        };
        _mockUserService.ShouldThrowGenericException = true;

        // Act
        var result = await _controller.CreateUser(request);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetUser_ExistingUserAsAdmin_ReturnsFullDetails() {
        // Arrange
        SetupAdminUser();
        var expectedUser = new UserResponse {
            Email = "user@example.com",
            Username = "user",
            Role = "User"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.GetUser("user@example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var user = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal("user@example.com", user.Email);
    }

    [Fact]
    public async Task GetUser_NonExistentUser_ReturnsNotFound() {
        // Arrange
        _mockUserService.ShouldReturnNull = true;

        // Act
        var result = await _controller.GetUser("nonexistent@example.com");

        // Assert
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetUser_OwnAccount_ReturnsFullDetails() {
        // Arrange
        SetupRegularUser("user@example.com");
        var expectedUser = new UserResponse {
            Email = "user@example.com",
            Username = "user",
            Role = "User"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.GetUser("user@example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var user = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal("user@example.com", user.Email);
    }

    [Fact]
    public async Task GetUser_OtherUserAccount_ReturnsMinimalDetails() {
        // Arrange
        SetupRegularUser("user1@example.com");
        var expectedUser = new UserResponse {
            Email = "user2@example.com",
            Username = "user2",
            Role = "User"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.GetUser("user2@example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var user = Assert.IsType<UserMinimalResponse>(okResult.Value);
        Assert.Equal("user2@example.com", user.Email);
        Assert.Equal("user2", user.Username);
    }

    [Fact]
    public async Task UpdateUser_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new UserUpdateRequest {
            FullName = "Updated Name"
        };
        var expectedUser = new UserResponse {
            Email = "user@example.com",
            Username = "user",
            FullName = "Updated Name"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.UpdateUser("user@example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal("Updated Name", user.FullName);
    }

    [Fact]
    public async Task UpdateUser_NonExistentUser_ReturnsNotFound() {
        // Arrange
        var request = new UserUpdateRequest { FullName = "Updated" };
        _mockUserService.ShouldReturnNull = true;

        // Act
        var result = await _controller.UpdateUser("nonexistent@example.com", request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task PatchUser_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new UserUpdateRequest { Role = "DomainAdmin" };
        var expectedUser = new UserResponse {
            Email = "user@example.com",
            Username = "user",
            Role = "DomainAdmin"
        };
        _mockUserService.SetUserResponse(expectedUser);

        // Act
        var result = await _controller.PatchUser("user@example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var user = Assert.IsType<UserResponse>(okResult.Value);
        Assert.Equal("DomainAdmin", user.Role);
    }

    [Fact]
    public async Task UpdateUserPassword_ValidRequest_ReturnsOk() {
        // Arrange
        var request = new UserPasswordUpdateRequest {
            CurrentPassword = "oldpass",
            NewPassword = "newpass123"
        };
        _mockUserService.PasswordUpdateSuccess = true;

        // Act
        var result = await _controller.UpdateUserPassword("user@example.com", request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task UpdateUserPassword_NonExistentUser_ReturnsNotFound() {
        // Arrange
        var request = new UserPasswordUpdateRequest {
            CurrentPassword = "oldpass",
            NewPassword = "newpass123"
        };
        _mockUserService.PasswordUpdateSuccess = false;

        // Act
        var result = await _controller.UpdateUserPassword("nonexistent@example.com", request);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task UpdateUserPassword_UnauthorizedAccess_ReturnsBadRequest() {
        // Arrange
        var request = new UserPasswordUpdateRequest {
            CurrentPassword = "wrongpass",
            NewPassword = "newpass123"
        };
        _mockUserService.ShouldThrowUnauthorizedException = true;

        // Act
        var result = await _controller.UpdateUserPassword("user@example.com", request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(badRequestResult.Value);
    }

    [Fact]
    public async Task DeleteUser_ExistingUser_ReturnsOk() {
        // Arrange
        _mockUserService.DeleteSuccess = true;

        // Act
        var result = await _controller.DeleteUser("user@example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task DeleteUser_NonExistentUser_ReturnsNotFound() {
        // Arrange
        _mockUserService.DeleteSuccess = false;

        // Act
        var result = await _controller.DeleteUser("nonexistent@example.com");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task GetUserStats_ExistingUser_ReturnsStats() {
        // Arrange
        var expectedStats = new UserStatsResponse {
            MessageCount = 100,
            FolderCount = 5,
            StorageUsed = 1024000
        };
        _mockUserService.SetUserStatsResponse(expectedStats);

        // Act
        var result = await _controller.GetUserStats("user@example.com");

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var stats = Assert.IsType<UserStatsResponse>(okResult.Value);
        Assert.Equal(100, stats.MessageCount);
    }

    [Fact]
    public async Task GetUserStats_NonExistentUser_ReturnsNotFound() {
        // Arrange
        _mockUserService.ShouldThrowArgumentException = true;

        // Act
        var result = await _controller.GetUserStats("nonexistent@example.com");

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    private void SetupAdminUser() {
        var claims = new[] {
            new Claim(ClaimTypes.Email, "admin@example.com"),
            new Claim(ClaimTypes.Role, "HostAdmin")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext {
                User = principal
            }
        };
    }

    private void SetupRegularUser(string email) {
        var claims = new[] {
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Role, "User")
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext {
                User = principal
            }
        };
    }
}

// Mock service for controller testing
public class MockUserServiceForController : IUserService {
    public bool ShouldThrowOnGetUsers { get; set; }
    public bool ShouldThrowArgumentException { get; set; }
    public bool ShouldThrowGenericException { get; set; }
    public bool ShouldThrowUnauthorizedException { get; set; }
    public bool ShouldReturnNull { get; set; }
    public bool PasswordUpdateSuccess { get; set; } = true;
    public bool DeleteSuccess { get; set; } = true;

    private PaginatedInfo<UserResponse>? _usersResponse;
    private UserResponse? _userResponse;
    private UserStatsResponse? _userStatsResponse;

    public void SetUsersResponse(PaginatedInfo<UserResponse> response) => _usersResponse = response;
    public void SetUserResponse(UserResponse response) => _userResponse = response;
    public void SetUserStatsResponse(UserStatsResponse response) => _userStatsResponse = response;

    public Task<PaginatedInfo<UserResponse>> GetUsersAsync(int page = 1, int pageSize = 50, string? domain = null) {
        if (ShouldThrowOnGetUsers) {
            throw new Exception("Service error");
        }
        return Task.FromResult(_usersResponse ?? new PaginatedInfo<UserResponse> { Items = [], Skip = (page - 1) * pageSize, Take = pageSize, TotalCount = 0 });
    }

    public Task<UserResponse> CreateUserAsync(CreateUserRequest request) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException("Invalid user data");
        }
        if (ShouldThrowGenericException) {
            throw new Exception("Service error");
        }
        return Task.FromResult(_userResponse ?? new UserResponse { Email = $"{request.Username}@{request.DomainName}", Username = request.Username });
    }

    public Task<UserResponse?> GetUserByEmailAsync(string email) {
        if (ShouldReturnNull) {
            return Task.FromResult<UserResponse?>(null);
        }
        return Task.FromResult<UserResponse?>(_userResponse ?? new UserResponse { Email = email, Username = email.Split('@')[0] });
    }

    public Task<UserResponse?> UpdateUserAsync(string email, UserUpdateRequest request) {
        if (ShouldReturnNull) {
            return Task.FromResult<UserResponse?>(null);
        }
        return Task.FromResult<UserResponse?>(_userResponse ?? new UserResponse { Email = email, Username = email.Split('@')[0] });
    }

    public Task<bool> UpdateUserPasswordAsync(string email, UserPasswordUpdateRequest request) {
        if (ShouldThrowUnauthorizedException) {
            throw new UnauthorizedAccessException("Current password is incorrect");
        }
        return Task.FromResult(PasswordUpdateSuccess);
    }

    public Task<bool> DeleteUserAsync(string email) {
        return Task.FromResult(DeleteSuccess);
    }

    public Task<UserStatsResponse> GetUserStatsAsync(string email) {
        if (ShouldThrowArgumentException) {
            throw new ArgumentException($"User '{email}' not found");
        }
        return Task.FromResult(_userStatsResponse ?? new UserStatsResponse { MessageCount = 0, FolderCount = 0, StorageUsed = 0 });
    }

    public Task<bool> UserExistsAsync(string email) {
        return Task.FromResult(!ShouldReturnNull);
    }

    public Task<UserResponse?> AuthenticateUserAsync(string email, string password) {
        if (ShouldReturnNull) {
            return Task.FromResult<UserResponse?>(null);
        }
        return Task.FromResult<UserResponse?>(_userResponse ?? new UserResponse { Email = email, Username = email.Split('@')[0] });
    }

    public Task<Frimerki.Models.Entities.User?> AuthenticateUserEntityAsync(string email, string password) {
        if (ShouldReturnNull) {
            return Task.FromResult<Frimerki.Models.Entities.User?>(null);
        }
        return Task.FromResult<Frimerki.Models.Entities.User?>(new Frimerki.Models.Entities.User {
            Id = 1,
            Username = email.Split('@')[0],
            DomainId = 1,
            CanLogin = true,
            Domain = new Frimerki.Models.Entities.DomainSettings { Name = email.Split('@')[1] }
        });
    }

    public Task<Frimerki.Models.Entities.User?> GetUserEntityByEmailAsync(string email) {
        if (ShouldReturnNull) {
            return Task.FromResult<Frimerki.Models.Entities.User?>(null);
        }
        return Task.FromResult<Frimerki.Models.Entities.User?>(new Frimerki.Models.Entities.User {
            Id = 1,
            Username = email.Split('@')[0],
            DomainId = 1,
            CanLogin = true,
            Domain = new Frimerki.Models.Entities.DomainSettings { Name = email.Split('@')[1] }
        });
    }

    public Task<bool> ValidateEmailFormatAsync(string email) {
        return Task.FromResult(email.Contains('@') && email.Contains('.'));
    }
}
