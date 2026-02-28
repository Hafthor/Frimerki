using System.Net.Sockets;
using System.Text;
using Frimerki.Models.DTOs;
using Frimerki.Models.Entities;
using Frimerki.Protocols.Imap;
using Frimerki.Services.Message;
using Frimerki.Services.User;
using Microsoft.Extensions.Logging;
using Moq;

namespace Frimerki.Tests.Protocols.Imap;

/// <summary>
/// Tests for ImapSession command handling, covering LOGIN, AUTHENTICATE,
/// SELECT/EXAMINE, LIST, STORE, EXPUNGE, SEARCH, FETCH, unknown commands,
/// and the helper methods (via protocol interactions).
/// </summary>
public sealed class ImapSessionCommandTests : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly Mock<ILogger<ImapSession>> _mockLogger = new();
    private readonly Mock<IUserService> _mockUserService = new();
    private readonly Mock<IMessageService> _mockMessageService = new();
    private readonly List<TcpListener> _listeners = [];

    public void Dispose() {
        foreach (var listener in _listeners) {
            try { listener.Stop(); } catch { }
        }
    }

    private async Task<(TcpClient client, NetworkStream stream, Task sessionTask)> CreateSessionAsync() {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        _listeners.Add(listener);

        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var clientTask = Task.Run(async () => {
            var c = new TcpClient();
            await c.ConnectAsync(System.Net.IPAddress.Loopback, port);
            return c;
        });
        var serverClient = await listener.AcceptTcpClientAsync();
        var client = await clientTask;
        var stream = client.GetStream();

        var session = new ImapSession(serverClient, _mockLogger.Object,
            _mockUserService.Object, _mockMessageService.Object);
        var sessionTask = session.HandleSessionAsync();
        return (client, stream, sessionTask);
    }

    private async Task SendAsync(NetworkStream stream, string command) {
        var bytes = Utf8NoBom.GetBytes(command + "\r\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
    }

    private async Task<string[]> ReadLinesAsync(NetworkStream stream, int timeoutMs = 2000) {
        var buffer = new byte[8192];
        List<string> lines = [];
        using var cts = new CancellationTokenSource(timeoutMs);
        try {
            var n = await stream.ReadAsync(buffer, cts.Token);
            lines.AddRange(Utf8NoBom.GetString(buffer, 0, n)
                .Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        } catch (OperationCanceledException) { }
        return [.. lines];
    }

    /// <summary>
    /// Reads until we see a line starting with the given tag (the tagged response).
    /// Returns all lines collected (untagged + tagged).
    /// </summary>
    private async Task<string[]> ReadUntilTaggedAsync(NetworkStream stream, string tag, int timeoutMs = 3000) {
        List<string> all = [];
        var buffer = new byte[8192];
        using var cts = new CancellationTokenSource(timeoutMs);
        try {
            while (!cts.IsCancellationRequested) {
                var n = await stream.ReadAsync(buffer, cts.Token);
                if (n == 0) {
                    break;
                }

                var lines = Utf8NoBom.GetString(buffer, 0, n)
                    .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                all.AddRange(lines);
                if (all.Any(l => l.StartsWith(tag + " "))) {
                    break;
                }
            }
        } catch (OperationCanceledException) { }
        return [.. all];
    }

    private async Task LoginAsync(NetworkStream stream) {
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("user", "pass"))
            .ReturnsAsync(new User { Id = 1, Username = "user", DomainId = 1, PasswordHash = "h", Salt = "s", CanLogin = true });
        // Read greeting
        await ReadLinesAsync(stream);
        await SendAsync(stream, "a0 LOGIN \"user\" \"pass\"");
        await ReadLinesAsync(stream);
    }

    private async Task LoginAndSelectAsync(NetworkStream stream) {
        await LoginAsync(stream);
        await SendAsync(stream, "s1 SELECT \"INBOX\"");
        await ReadUntilTaggedAsync(stream, "s1");
    }

    private async Task CleanupAsync(TcpClient client, NetworkStream stream, Task sessionTask) {
        try {
            await SendAsync(stream, "z9 LOGOUT");
        } catch { }
        client.Close();
        try { await sessionTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }

    // ── LOGIN edge cases ──

    [Fact]
    public async Task Login_MissingArguments_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream); // greeting

        await SendAsync(stream, "a1 LOGIN \"onlyuser\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("LOGIN requires"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnsNo() {
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("bad", "creds"))
            .ReturnsAsync((User)null);

        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 LOGIN \"bad\" \"creds\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("LOGIN failed"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Login_WhenAlreadyAuthenticated_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a2 LOGIN \"user\" \"pass\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a2 BAD") && l.Contains("Already authenticated"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Login_AuthThrowsException_ReturnsNo() {
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("boom", "pass"))
            .ThrowsAsync(new InvalidOperationException("DB error"));

        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 LOGIN \"boom\" \"pass\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("LOGIN failed"));

        await CleanupAsync(client, stream, task);
    }

    // ── AUTHENTICATE PLAIN ──

    [Fact]
    public async Task Authenticate_UnsupportedMechanism_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 AUTHENTICATE CRAM-MD5");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("not supported"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Authenticate_MissingMechanism_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 AUTHENTICATE");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("requires mechanism"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Authenticate_WhenAlreadyAuthenticated_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a2 AUTHENTICATE PLAIN");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a2 BAD") && l.Contains("Already authenticated"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Authenticate_PlainValid_Succeeds() {
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("authuser", "authpass"))
            .ReturnsAsync(new User { Id = 2, Username = "authuser", DomainId = 1, PasswordHash = "h", Salt = "s", CanLogin = true });

        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 AUTHENTICATE PLAIN");
        // Wait for "+ " continuation
        var cont = await ReadLinesAsync(stream);
        Assert.Contains(cont, l => l.StartsWith("+"));

        // Send base64 encoded "\0authuser\0authpass"
        var plain = "\0authuser\0authpass";
        var base64 = Convert.ToBase64String(Utf8NoBom.GetBytes(plain));
        await SendAsync(stream, base64);
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("AUTHENTICATE completed"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Authenticate_PlainInvalidCredentials_ReturnsNo() {
        _mockUserService.Setup(x => x.AuthenticateUserEntityAsync("authuser", "wrong"))
            .ReturnsAsync((User)null);

        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 AUTHENTICATE PLAIN");
        await ReadLinesAsync(stream); // continuation

        var plain = "\0authuser\0wrong";
        var base64 = Convert.ToBase64String(Utf8NoBom.GetBytes(plain));
        await SendAsync(stream, base64);
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Authentication failed"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Authenticate_PlainInvalidBase64_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 AUTHENTICATE PLAIN");
        await ReadLinesAsync(stream); // continuation

        await SendAsync(stream, "!!!not-base64!!!");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Authentication failed"));

        await CleanupAsync(client, stream, task);
    }

    // ── SELECT / EXAMINE ──

    [Fact]
    public async Task Select_BeforeAuth_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 SELECT \"INBOX\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must be authenticated"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Select_MissingFolder_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 SELECT");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("requires folder"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Select_UnknownFolder_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 SELECT \"NonExistent\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Folder not found"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Select_Inbox_ReturnsCorrectUntaggedResponses() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 SELECT \"INBOX\"");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l == "* 0 EXISTS");
        Assert.Contains(resp, l => l == "* 0 RECENT");
        Assert.Contains(resp, l => l.Contains("FLAGS"));
        Assert.Contains(resp, l => l.Contains("PERMANENTFLAGS"));
        Assert.Contains(resp, l => l.Contains("UIDNEXT"));
        Assert.Contains(resp, l => l.Contains("UIDVALIDITY"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("READ-WRITE"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Select_Drafts_Succeeds() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 SELECT \"Drafts\"");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("READ-WRITE"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Examine_Inbox_ReturnsReadOnly() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 EXAMINE \"INBOX\"");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("READ-ONLY"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Examine_BeforeAuth_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 EXAMINE \"INBOX\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must be authenticated"));

        await CleanupAsync(client, stream, task);
    }

    // ── LIST ──

    [Fact]
    public async Task List_BeforeAuth_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 LIST \"\" \"*\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must be authenticated"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task List_WhenAuthenticated_ReturnsStandardFolders() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 LIST \"\" \"*\"");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.Contains("LIST") && l.Contains("INBOX"));
        Assert.Contains(resp, l => l.Contains("LIST") && l.Contains("Drafts"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        await CleanupAsync(client, stream, task);
    }

    // ── FETCH (stub) ──

    [Fact]
    public async Task Fetch_BeforeSelect_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 FETCH 1 (FLAGS)");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must have folder selected"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Fetch_WhenSelected_ReturnsOk() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 FETCH 1 (FLAGS)");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("FETCH completed"));

        await CleanupAsync(client, stream, task);
    }

    // ── SEARCH (stub) ──

    [Fact]
    public async Task Search_BeforeSelect_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 SEARCH ALL");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must have folder selected"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Search_WhenSelected_ReturnsEmptySearchAndOk() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 SEARCH ALL");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l == "* SEARCH");
        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("SEARCH completed"));

        await CleanupAsync(client, stream, task);
    }

    // ── STORE ──

    [Fact]
    public async Task Store_BeforeSelect_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS (\\Seen)");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must have folder selected"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_OnReadOnlyFolder_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        // EXAMINE opens read-only
        await SendAsync(stream, "s1 EXAMINE \"INBOX\"");
        await ReadUntilTaggedAsync(stream, "s1");

        await SendAsync(stream, "a1 STORE 1 +FLAGS (\\Seen)");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("read-only"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_MissingArguments_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_InvalidFlagsFormat_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS NoParens");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("Invalid flags"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_AddFlags_CallsUpdateAndReturnsFetch() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Subject = "Test" });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse {
                Id = 1,
                Flags = new MessageFlagsResponse { Seen = true }
            });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS (\\Seen)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        // Should have untagged FETCH response with flags
        Assert.Contains(resp, l => l.StartsWith("*") && l.Contains("FETCH") && l.Contains("\\Seen"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("STORE completed"));

        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Seen == true)), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_RemoveFlags_SetsToFalse() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse {
                Id = 1,
                Flags = new MessageFlagsResponse()
            });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 -FLAGS (\\Seen)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Seen == false)), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_SilentFlag_DoesNotSendFetch() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS.SILENT (\\Seen)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        // Should NOT have untagged FETCH response
        Assert.DoesNotContain(resp, l => l.StartsWith("*") && l.Contains("FETCH"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        // GetMessageAsync should NOT be called for silent mode
        _mockMessageService.Verify(x => x.GetMessageAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_ReplaceFlags_SetsAllToFalseThenSpecified() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Flags = new MessageFlagsResponse { Flagged = true } });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 FLAGS (\\Flagged)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        // Replace mode: Seen/Answered/Deleted/Draft should be false, Flagged should be true
        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Flagged == true && r.Flags.Seen == false && r.Flags.Deleted == false)),
            Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_MultipleFlags_SetsAll() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse {
                Id = 1,
                Flags = new MessageFlagsResponse { Seen = true, Answered = true }
            });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 STORE 1 +FLAGS (\\Seen \\Answered)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Seen == true && r.Flags.Answered == true)), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    /// <summary>
    /// \Recent is a read-only flag per RFC 3501 §2.3.2 — STORE should silently ignore it
    /// without setting any standard flag or adding it to CustomFlags.
    /// </summary>
    [Fact]
    public async Task Store_RecentFlag_IsSilentlyIgnored() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Flags = new MessageFlagsResponse() });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, @"a1 STORE 1 +FLAGS (\Recent)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        // \Recent should not set any standard flag or appear in CustomFlags
        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Seen == null && r.Flags.Answered == null && r.Flags.Flagged == null
                && r.Flags.Deleted == null && r.Flags.Draft == null
                && (r.Flags.CustomFlags == null || r.Flags.CustomFlags.Count == 0))),
            Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Store_RecentWithOtherFlags_OnlySetsOtherFlags() {
        _mockMessageService.Setup(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.IsAny<MessageUpdateRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1 });
        _mockMessageService.Setup(x => x.GetMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Flags = new MessageFlagsResponse { Seen = true } });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, @"a1 STORE 1 +FLAGS (\Recent \Seen)");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        // \Seen should be set, \Recent should be ignored
        _mockMessageService.Verify(x => x.UpdateMessageAsync(1, It.IsAny<int>(), It.Is<MessageUpdateRequest>(
            r => r.Flags.Seen == true
                && (r.Flags.CustomFlags == null || r.Flags.CustomFlags.Count == 0))),
            Times.Once);

        await CleanupAsync(client, stream, task);
    }

    // ── EXPUNGE ──

    [Fact]
    public async Task Expunge_BeforeSelect_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 EXPUNGE");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("Must have folder selected"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Expunge_OnReadOnlyFolder_ReturnsNo() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "s1 EXAMINE \"INBOX\"");
        await ReadUntilTaggedAsync(stream, "s1");

        await SendAsync(stream, "a1 EXPUNGE");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 NO") && l.Contains("read-only"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Expunge_NoDeletedMessages_ReturnsOk() {
        _mockMessageService.Setup(x => x.GetMessagesAsync(1, It.IsAny<MessageFilterRequest>()))
            .ReturnsAsync(new PaginatedInfo<MessageListItemResponse> {
                Items = [],
                TotalCount = 5
            });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 EXPUNGE");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.Contains("EXISTS"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("EXPUNGE completed"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Expunge_WithDeletedMessages_SendsExpungeResponses() {
        var deletedItems = new List<MessageListItemResponse> {
            new() { Id = 10, Subject = "Msg1" },
            new() { Id = 20, Subject = "Msg2" },
        };

        _mockMessageService.SetupSequence(x => x.GetMessagesAsync(1, It.IsAny<MessageFilterRequest>()))
            .ReturnsAsync(new PaginatedInfo<MessageListItemResponse> { Items = deletedItems, TotalCount = 2 })
            .ReturnsAsync(new PaginatedInfo<MessageListItemResponse> { Items = [], TotalCount = 3 });

        _mockMessageService.Setup(x => x.DeleteMessageAsync(1, It.IsAny<int>()))
            .ReturnsAsync(true);

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        await SendAsync(stream, "a1 EXPUNGE");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        // Should get EXPUNGE untagged responses (in reverse order per RFC 3501)
        Assert.Contains(resp, l => l.Contains("EXPUNGE"));
        Assert.Contains(resp, l => l.Contains("EXISTS"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        _mockMessageService.Verify(x => x.DeleteMessageAsync(1, It.IsAny<int>()), Times.Exactly(2));

        await CleanupAsync(client, stream, task);
    }

    // ── Unknown / bad commands ──

    [Fact]
    public async Task UnknownCommand_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 FROBNICATE");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("Unknown command"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task MalformedCommand_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        // Single word is not a valid IMAP command (needs tag + command)
        await SendAsync(stream, "onlyoneword");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.Contains("BAD"));

        await CleanupAsync(client, stream, task);
    }

    // ── CAPABILITY ──

    [Fact]
    public async Task Capability_ReturnsExpectedCapabilities() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 CAPABILITY");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("* CAPABILITY") && l.Contains("IMAP4rev1")
            && l.Contains("AUTH=PLAIN") && l.Contains("UIDPLUS"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK"));

        await CleanupAsync(client, stream, task);
    }

    // ── NOOP ──

    [Fact]
    public async Task Noop_ReturnsOk() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 NOOP");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l == "a1 OK NOOP completed");

        await CleanupAsync(client, stream, task);
    }

    // ── LOGOUT ──

    [Fact]
    public async Task Logout_SendsByeAndOk() {
        var (client, stream, task) = await CreateSessionAsync();
        await ReadLinesAsync(stream);

        await SendAsync(stream, "a1 LOGOUT");
        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("* BYE"));
        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("LOGOUT completed"));

        client.Close();
        await task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    // ── APPEND (without literal → BAD, with literal via protocol) ──

    [Fact]
    public async Task Append_WithoutLiteral_ReturnsBad() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 APPEND \"INBOX\"");
        var resp = await ReadLinesAsync(stream);

        Assert.Contains(resp, l => l.StartsWith("a1 BAD") && l.Contains("APPEND requires literal"));

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Append_WithLiteral_CreatesMessage() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 42, Subject = "Hello", Uid = 42 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var messageContent = "Subject: Hello\r\nTo: dest@example.com\r\n\r\nBody of the message";
        var literalSize = Utf8NoBom.GetByteCount(messageContent);

        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{literalSize}}}");
        // Read continuation "+ "
        var cont = await ReadLinesAsync(stream);
        Assert.Contains(cont, l => l.StartsWith("+"));

        // Send literal data
        var msgBytes = Utf8NoBom.GetBytes(messageContent + "\r\n");
        await stream.WriteAsync(msgBytes);
        await stream.FlushAsync();

        var resp = await ReadUntilTaggedAsync(stream, "a1");

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("APPEND"));

        _mockMessageService.Verify(x => x.CreateMessageAsync(1, It.Is<MessageRequest>(
            r => r.Subject == "Hello" && r.ToAddress == "dest@example.com"
                && r.Body.Contains("Body of the message"))), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Append_ToDrafts_UsesFolderId2() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Subject = "Draft", Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var messageContent = "Subject: Draft\r\nTo: a@b.com\r\n\r\nDraft body";
        var literalSize = Utf8NoBom.GetByteCount(messageContent);

        await SendAsync(stream, $"a1 APPEND \"Drafts\" {{{literalSize}}}");
        await ReadLinesAsync(stream); // continuation
        await stream.WriteAsync(Utf8NoBom.GetBytes(messageContent + "\r\n"));
        await stream.FlushAsync();

        await ReadUntilTaggedAsync(stream, "a1");

        // Drafts folder = ID 2
        _mockMessageService.Verify(x => x.CreateMessageAsync(2, It.IsAny<MessageRequest>()), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    /// <summary>
    /// Regression: a previous bug caused HandleAppendCommandWithLiteral to fall through
    /// after sending OK, resulting in a spurious "BAD Invalid APPEND syntax" response.
    /// This test verifies only one tagged response (OK) is sent and no BAD follows.
    /// </summary>
    [Fact]
    public async Task Append_WithLiteral_DoesNotSendSpuriousBadAfterOk() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Subject = "Test", Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var messageContent = "Subject: Test\r\nTo: a@b.com\r\n\r\nBody";
        var literalSize = Utf8NoBom.GetByteCount(messageContent);

        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{literalSize}}}");
        await ReadLinesAsync(stream); // continuation "+"

        await stream.WriteAsync(Utf8NoBom.GetBytes(messageContent + "\r\n"));
        await stream.FlushAsync();

        // Read tagged OK and then wait briefly for any trailing response
        var resp = await ReadUntilTaggedAsync(stream, "a1");
        var trailing = await ReadLinesAsync(stream, timeoutMs: 500);

        Assert.Contains(resp, l => l.StartsWith("a1 OK") && l.Contains("APPEND"));
        Assert.DoesNotContain(resp, l => l.Contains("BAD"));
        Assert.DoesNotContain(trailing, l => l.Contains("BAD"));

        // Verify exactly one tagged "a1" response was sent
        var taggedCount = resp.Concat(trailing).Count(l => l.StartsWith("a1 "));
        Assert.Equal(1, taggedCount);

        await CleanupAsync(client, stream, task);
    }

    // ── Header/body extraction (tested via APPEND interactions) ──

    [Fact]
    public async Task Append_ExtractsMultipleHeaders() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var content = "Subject: Test Subject\r\n" +
                      "To: recipient@example.com\r\n" +
                      "In-Reply-To: <original@example.com>\r\n" +
                      "References: <ref1@example.com> <ref2@example.com>\r\n" +
                      "\r\n" +
                      "The body content here";

        var size = Utf8NoBom.GetByteCount(content);
        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{size}}}");
        await ReadLinesAsync(stream);
        await stream.WriteAsync(Utf8NoBom.GetBytes(content + "\r\n"));
        await stream.FlushAsync();
        await ReadUntilTaggedAsync(stream, "a1");

        _mockMessageService.Verify(x => x.CreateMessageAsync(1, It.Is<MessageRequest>(
            r => r.Subject == "Test Subject"
                && r.ToAddress == "recipient@example.com"
                && r.InReplyTo == "<original@example.com>"
                && r.References == "<ref1@example.com> <ref2@example.com>"
                && r.Body == "The body content here")), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Append_MultipleToRecipients_FirstIsToAddress() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var content = "Subject: Multi\r\nTo: first@a.com, second@b.com\r\n\r\nBody";
        var size = Utf8NoBom.GetByteCount(content);
        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{size}}}");
        await ReadLinesAsync(stream);
        await stream.WriteAsync(Utf8NoBom.GetBytes(content + "\r\n"));
        await stream.FlushAsync();
        await ReadUntilTaggedAsync(stream, "a1");

        _mockMessageService.Verify(x => x.CreateMessageAsync(1, It.Is<MessageRequest>(
            r => r.ToAddress == "first@a.com" && r.CcAddress == "second@b.com")), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Append_NoToHeader_UsesDefaultAddress() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        var content = "Subject: NoTo\r\n\r\nBody only";
        var size = Utf8NoBom.GetByteCount(content);
        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{size}}}");
        await ReadLinesAsync(stream);
        await stream.WriteAsync(Utf8NoBom.GetBytes(content + "\r\n"));
        await stream.FlushAsync();
        await ReadUntilTaggedAsync(stream, "a1");

        _mockMessageService.Verify(x => x.CreateMessageAsync(1, It.Is<MessageRequest>(
            r => r.ToAddress == "unknown@localhost")), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    [Fact]
    public async Task Append_BodyWithLfLineEndings_ExtractsCorrectly() {
        _mockMessageService.Setup(x => x.CreateMessageAsync(It.IsAny<int>(), It.IsAny<MessageRequest>()))
            .ReturnsAsync(new MessageResponse { Id = 1, Uid = 1 });

        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        // Use LF-only line endings
        var content = "Subject: LfTest\nTo: a@b.com\n\nBody with LF";
        var size = Utf8NoBom.GetByteCount(content);
        await SendAsync(stream, $"a1 APPEND \"INBOX\" {{{size}}}");
        await ReadLinesAsync(stream);
        await stream.WriteAsync(Utf8NoBom.GetBytes(content + "\r\n"));
        await stream.FlushAsync();
        await ReadUntilTaggedAsync(stream, "a1");

        _mockMessageService.Verify(x => x.CreateMessageAsync(1, It.Is<MessageRequest>(
            r => r.Body.Contains("Body with LF"))), Times.Once);

        await CleanupAsync(client, stream, task);
    }

    // ── Select then re-select ──

    [Fact]
    public async Task Select_AfterPreviousSelect_Succeeds() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAndSelectAsync(stream);

        // Re-select a different folder
        await SendAsync(stream, "a2 SELECT \"Drafts\"");
        var resp = await ReadUntilTaggedAsync(stream, "a2");

        Assert.Contains(resp, l => l.StartsWith("a2 OK"));

        await CleanupAsync(client, stream, task);
    }

    // ── Multiple commands in sequence ──

    [Fact]
    public async Task MultipleCommands_InSequence_AllHandled() {
        var (client, stream, task) = await CreateSessionAsync();
        await LoginAsync(stream);

        await SendAsync(stream, "a1 NOOP");
        var r1 = await ReadLinesAsync(stream);
        Assert.Contains(r1, l => l == "a1 OK NOOP completed");

        await SendAsync(stream, "a2 CAPABILITY");
        var r2 = await ReadUntilTaggedAsync(stream, "a2");
        Assert.Contains(r2, l => l.StartsWith("a2 OK"));

        await SendAsync(stream, "a3 LIST \"\" \"*\"");
        var r3 = await ReadUntilTaggedAsync(stream, "a3");
        Assert.Contains(r3, l => l.StartsWith("a3 OK"));

        await CleanupAsync(client, stream, task);
    }
}

