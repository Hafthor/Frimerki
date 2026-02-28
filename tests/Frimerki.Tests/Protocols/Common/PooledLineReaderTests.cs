using System.Text;
using Frimerki.Protocols.Common;

namespace Frimerki.Tests.Protocols.Common;

public class PooledLineReaderTests : IDisposable {
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private MemoryStream _stream;
    private PooledLineReader _reader;

    public void Dispose() {
        _reader?.Dispose();
        _stream?.Dispose();
    }

    private PooledLineReader CreateReader(string content, Encoding encoding = null) {
        encoding ??= Utf8NoBom;
        _stream = new MemoryStream(encoding.GetBytes(content));
        _reader = new PooledLineReader(_stream, encoding);
        return _reader;
    }

    [Fact]
    public async Task ReadLineAsync_SingleLine_ReturnsLine() {
        var reader = CreateReader("Hello, World!\r\n");

        var line = await reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal("Hello, World!", line);
    }

    [Fact]
    public async Task ReadLineAsync_MultipleLines_ReturnsEachLine() {
        var reader = CreateReader("Line 1\r\nLine 2\r\nLine 3\r\n");

        Assert.Equal("Line 1", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 2", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 3", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_UnixLineEndings_ReturnsLines() {
        var reader = CreateReader("Line 1\nLine 2\nLine 3\n");

        Assert.Equal("Line 1", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 2", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 3", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_NoTrailingNewline_ReturnsLastLine() {
        var reader = CreateReader("Line 1\r\nLine 2");

        Assert.Equal("Line 1", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 2", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_EmptyStream_ReturnsNull() {
        var reader = CreateReader("");

        var line = await reader.ReadLineAsync(CancellationToken.None);

        Assert.Null(line);
    }

    [Fact]
    public async Task ReadLineAsync_EmptyLines_ReturnsEmptyStrings() {
        var reader = CreateReader("\r\n\r\nContent\r\n");

        Assert.Equal("", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Content", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_ReturnsNullAtEndOfStream() {
        var reader = CreateReader("Only line\r\n");

        Assert.Equal("Only line", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_Utf8BomStripped_FirstLine() {
        // UTF8 with BOM: the BOM bytes (EF BB BF) should be stripped from first line
        var encoding = new UTF8Encoding(true);
        var reader = CreateReader("EHLO test.com\r\n", encoding);

        var line = await reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal("EHLO test.com", line);
    }

    [Fact]
    public async Task ReadLineAsync_Utf8BomStripped_OnlyFirstLine() {
        var encoding = new UTF8Encoding(true);
        // BOM is only in the byte stream preamble, so only first line could be affected
        var reader = CreateReader("First\r\nSecond\r\n", encoding);

        Assert.Equal("First", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Second", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_BomOnlyContent_ReturnsEmptyAfterStripping() {
        // Stream contains only a BOM followed by a newline
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'\r', (byte)'\n' };
        _stream = new MemoryStream(bytes);
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var line = await _reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal("", line);
    }

    [Fact]
    public async Task ReadLineAsync_BomWithNoTrailingNewline_StrippedOnEofPath() {
        // BOM + content but no trailing newline — exercises the end-of-stream BOM stripping path
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i' };
        _stream = new MemoryStream(bytes);
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var line = await _reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal("Hi", line);
    }

    [Fact]
    public async Task ReadLineAsync_NoBom_FirstLineUnchanged() {
        var reader = CreateReader("HELO localhost\r\n");

        var line = await reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal("HELO localhost", line);
    }

    [Fact]
    public async Task ReadLineAsync_LongLine_HandlesCorrectly() {
        var longContent = new string('A', 8192); // Longer than default buffer size
        var reader = CreateReader(longContent + "\r\n");

        var line = await reader.ReadLineAsync(CancellationToken.None);

        Assert.Equal(longContent, line);
    }

    [Fact]
    public async Task ReadLineAsync_LongLineFollowedByShort_HandlesCorrectly() {
        var longContent = new string('X', 10000);
        var reader = CreateReader(longContent + "\r\nShort\r\n");

        Assert.Equal(longContent, await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Short", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_MixedLineEndings_HandledCorrectly() {
        var reader = CreateReader("Windows\r\nUnix\nMac\rNext\r\n");

        Assert.Equal("Windows", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Unix", await reader.ReadLineAsync(CancellationToken.None));
        // \r without \n is not treated as a line ending by PooledLineReader
        // "Mac\rNext" should be returned as one line (only \n is the delimiter)
        Assert.Equal("Mac\rNext", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_SmtpConversation_ParsesCorrectly() {
        var conversation = "EHLO test.example.com\r\n" +
                           "MAIL FROM:<user@example.com>\r\n" +
                           "RCPT TO:<dest@example.com>\r\n" +
                           "DATA\r\n" +
                           "QUIT\r\n";
        var reader = CreateReader(conversation);

        Assert.Equal("EHLO test.example.com", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("MAIL FROM:<user@example.com>", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("RCPT TO:<dest@example.com>", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("DATA", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("QUIT", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Null(await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_CancellationToken_ThrowsOnCancellation() {
        // Create a stream that blocks on read
        var slowStream = new SlowStream();
        _reader = new PooledLineReader(slowStream, Utf8NoBom);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _reader.ReadLineAsync(cts.Token));
    }

    [Fact]
    public async Task ReadLineAsync_SmallBufferSize_StillWorksCorrectly() {
        // Use a tiny buffer to force multiple reads per line
        _stream = new MemoryStream(Utf8NoBom.GetBytes("Hello World\r\nSecond Line\r\n"));
        _reader = new PooledLineReader(_stream, Utf8NoBom, bufferSize: 4);

        Assert.Equal("Hello World", await _reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Second Line", await _reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_UnicodeContent_PreservedCorrectly() {
        var reader = CreateReader("Halló heimur 🌍\r\nFrímerki\r\n");

        Assert.Equal("Halló heimur 🌍", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Frímerki", await reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadLineAsync_AsciiEncoding_WorksCorrectly() {
        _stream = new MemoryStream(Encoding.ASCII.GetBytes("+OK POP3 ready\r\nUSER test\r\n"));
        _reader = new PooledLineReader(_stream, Encoding.ASCII);

        Assert.Equal("+OK POP3 ready", await _reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("USER test", await _reader.ReadLineAsync(CancellationToken.None));
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes() {
        var reader = CreateReader("test\r\n");

        reader.Dispose();
        reader.Dispose(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes() {
        var reader = CreateReader("test\r\n");

        await reader.DisposeAsync();
        await reader.DisposeAsync(); // Should not throw
    }

    [Fact]
    public async Task ReadLineAsync_DotStuffedEmailBody_ReturnsLinesIntact() {
        // Email DATA with dot-stuffing
        var body = "Subject: Test\r\n" +
                   "\r\n" +
                   "Line 1\r\n" +
                   "..Dot-stuffed line\r\n" +
                   ".\r\n";
        var reader = CreateReader(body);

        Assert.Equal("Subject: Test", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("Line 1", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal("..Dot-stuffed line", await reader.ReadLineAsync(CancellationToken.None));
        Assert.Equal(".", await reader.ReadLineAsync(CancellationToken.None));
    }

    // ── ReadDotTerminatedBytesAsync ──

    [Fact]
    public async Task ReadDotTerminatedBytes_BasicMessage_ReturnsBodyWithoutTerminator() {
        var content = "Subject: Test\r\nTo: a@b.com\r\n\r\nHello world\r\n.\r\n";
        // No prior ReadLineAsync — go straight to dot-terminated read
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Subject: Test", result);
        Assert.Contains("Hello world", result);
        Assert.DoesNotContain("\r\n.\r\n", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_DotUnstuffing_RemovesExtraDot() {
        var content = "Line 1\r\n..Dot-stuffed\r\n.\r\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Line 1", result);
        Assert.Contains(".Dot-stuffed", result);
        Assert.DoesNotContain("..Dot-stuffed", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_EmptyBody_ReturnsEmpty() {
        var content = ".\r\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);

        Assert.Empty(bytes);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_StreamEndsWithoutTerminator_ReturnsAccumulatedData() {
        var content = "Some data\r\nMore data";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Some data", result);
        Assert.Contains("More data", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_AfterReadLineAsync_ConsumesLeftover() {
        // Simulate SMTP: ReadLineAsync reads commands, then ReadDotTerminatedBytesAsync reads body
        var content = "DATA\r\nBody line 1\r\nBody line 2\r\n.\r\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        // Read the command line first
        var command = await _reader.ReadLineAsync(CancellationToken.None);
        Assert.Equal("DATA", command);

        // Now read dot-terminated body
        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Body line 1", result);
        Assert.Contains("Body line 2", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_UnixLineEndings_Handled() {
        var content = "Line 1\nLine 2\n.\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Line 1", result);
        Assert.Contains("Line 2", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_SmallBuffer_StillWorksCorrectly() {
        var content = "Hello\r\n..Stuffed\r\n.\r\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom, bufferSize: 4);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        var result = Utf8NoBom.GetString(bytes);

        Assert.Contains("Hello", result);
        Assert.Contains(".Stuffed", result);
        Assert.DoesNotContain("..Stuffed", result);
    }

    [Fact]
    public async Task ReadDotTerminatedBytes_ContinuesReadingAfterTerminator() {
        // After dot-terminated body, we should be able to read the next line
        var content = "Body\r\n.\r\nNEXT COMMAND\r\n";
        _stream = new MemoryStream(Utf8NoBom.GetBytes(content));
        _reader = new PooledLineReader(_stream, Utf8NoBom);

        var bytes = await _reader.ReadDotTerminatedBytesAsync(CancellationToken.None);
        Assert.Equal("Body", Utf8NoBom.GetString(bytes));

        // The reader should be able to continue reading lines after the terminator
        var nextLine = await _reader.ReadLineAsync(CancellationToken.None);
        Assert.Equal("NEXT COMMAND", nextLine);
    }

    /// <summary>
    /// A stream that always cancels — used to test cancellation behavior.
    /// </summary>
    private class SlowStream : Stream {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) {
            cancellationToken.ThrowIfCancellationRequested();
            // Simulate a stream that never has data
            return new ValueTask<int>(Task.Delay(Timeout.Infinite, cancellationToken).ContinueWith(_ => 0));
        }
    }
}

