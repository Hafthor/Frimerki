using System;
using System.Threading;
using System.Threading.Tasks;
using Frimerki.Server;
using Microsoft.AspNetCore.SignalR;
using Moq;
using Xunit;

namespace Frimerki.Tests.Server.Hubs;

public class EmailHubTests {
    private readonly EmailHub _hub;
    private readonly Mock<IGroupManager> _mockGroups;
    private readonly Mock<HubCallerContext> _mockContext;
    private readonly Mock<IHubCallerClients> _mockClients;

    public EmailHubTests() {
        _hub = new EmailHub();
        _mockGroups = new Mock<IGroupManager>();
        _mockContext = new Mock<HubCallerContext>();
        _mockClients = new Mock<IHubCallerClients>();

        // Setup the hub context
        _mockContext.Setup(x => x.ConnectionId).Returns("test-connection-id");

        // Setup the hub properties using reflection since they're protected
        var contextProperty = typeof(Hub).GetProperty("Context");
        contextProperty?.SetValue(_hub, _mockContext.Object);

        var groupsProperty = typeof(Hub).GetProperty("Groups");
        groupsProperty?.SetValue(_hub, _mockGroups.Object);

        var clientsProperty = typeof(Hub).GetProperty("Clients");
        clientsProperty?.SetValue(_hub, _mockClients.Object);
    }

    [Fact]
    public async Task JoinFolder_ValidFolderId_AddsConnectionToGroup() {
        // Arrange
        var folderId = "inbox";
        var expectedGroupName = "folder_inbox";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.AddToGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.JoinFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.AddToGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task JoinFolder_EmptyFolderId_AddsConnectionToEmptyGroup() {
        // Arrange
        var folderId = "";
        var expectedGroupName = "folder_";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.AddToGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.JoinFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.AddToGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task JoinFolder_SpecialCharactersInFolderId_HandlesCorrectly() {
        // Arrange
        var folderId = "INBOX/Sent Items";
        var expectedGroupName = "folder_INBOX/Sent Items";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.AddToGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.JoinFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.AddToGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveFolder_ValidFolderId_RemovesConnectionFromGroup() {
        // Arrange
        var folderId = "sent";
        var expectedGroupName = "folder_sent";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.LeaveFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveFolder_EmptyFolderId_RemovesConnectionFromEmptyGroup() {
        // Arrange
        var folderId = "";
        var expectedGroupName = "folder_";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.LeaveFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task LeaveFolder_SpecialCharactersInFolderId_HandlesCorrectly() {
        // Arrange
        var folderId = "INBOX/Drafts";
        var expectedGroupName = "folder_INBOX/Drafts";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.LeaveFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Fact]
    public async Task OnConnectedAsync_WhenCalled_CallsBaseOnConnectedAsync() {
        // Arrange & Act
        var exception = await Record.ExceptionAsync(async () => await _hub.OnConnectedAsync());

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithoutException_CallsBaseOnDisconnectedAsync() {
        // Arrange & Act
        var exception = await Record.ExceptionAsync(async () => await _hub.OnDisconnectedAsync(null));

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task OnDisconnectedAsync_WithException_CallsBaseOnDisconnectedAsync() {
        // Arrange
        var testException = new InvalidOperationException("Test exception");

        // Act
        var exception = await Record.ExceptionAsync(async () => await _hub.OnDisconnectedAsync(testException));

        // Assert
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("sent")]
    [InlineData("drafts")]
    [InlineData("INBOX/Work")]
    [InlineData("INBOX/Personal")]
    public async Task JoinFolder_MultipleValidFolderIds_AddsToCorrectGroups(string folderId) {
        // Arrange
        var expectedGroupName = $"folder_{folderId}";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.AddToGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.JoinFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.AddToGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("sent")]
    [InlineData("drafts")]
    [InlineData("INBOX/Work")]
    [InlineData("INBOX/Personal")]
    public async Task LeaveFolder_MultipleValidFolderIds_RemovesFromCorrectGroups(string folderId) {
        // Arrange
        var expectedGroupName = $"folder_{folderId}";
        var connectionId = "test-connection-id";

        _mockGroups
            .Setup(x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await _hub.LeaveFolder(folderId);

        // Assert
        _mockGroups.Verify(
            x => x.RemoveFromGroupAsync(connectionId, expectedGroupName, default),
            Times.Once);
    }
}
