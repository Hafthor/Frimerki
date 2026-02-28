using System.Buffers;
using System.Text;

namespace Frimerki.Protocols.Common;

public sealed class PooledLineReader(Stream stream, Encoding encoding, int bufferSize = 4096)
    : IAsyncDisposable, IDisposable {
    private readonly Decoder _decoder = encoding.GetDecoder();
    private readonly byte[] _byteBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
    private readonly char[] _charBuffer = ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(bufferSize));
    private readonly StringBuilder _lineBuilder = new();
    private int _charPos;
    private int _charLen;
    private int _bytePos;
    private int _byteLen;
    private int _lineCount;
    private bool _disposed;
    private const char ByteOrderMark = '\uFEFF';

    public async Task<string> ReadLineAsync(CancellationToken cancellationToken) {
        while (true) {
            if (_charPos < _charLen) {
                var newlineIndex = Array.IndexOf(_charBuffer, '\n', _charPos, _charLen - _charPos);
                if (newlineIndex >= 0) {
                    var length = newlineIndex - _charPos;
                    if (length > 0 && _charBuffer[newlineIndex - 1] == '\r') {
                        length--;
                    }

                    _lineBuilder.Append(_charBuffer, _charPos, length);

                    _charPos = newlineIndex + 1;
                    var line = _lineBuilder.ToString();
                    _lineBuilder.Clear();
                    if (_lineCount++ == 0 && line.Length > 0 && line[0] == ByteOrderMark) {
                        line = line[1..];
                    }
                    return line;
                }

                _lineBuilder.Append(_charBuffer, _charPos, _charLen - _charPos);
                _charPos = _charLen;
            }

            var bytesRead = _bytePos < _byteLen
                ? ConsumeLeftoverBytes()
                : await stream.ReadAsync(_byteBuffer, cancellationToken);
            if (bytesRead == 0) {
                if (_lineBuilder.Length == 0) {
                    return null;
                }

                var line = _lineBuilder.ToString();
                _lineBuilder.Clear();
                if (_lineCount++ == 0 && line.Length > 0 && line[0] == ByteOrderMark) {
                    line = line[1..];
                }
                return line;
            }

            _decoder.Convert(
                _byteBuffer, 0, bytesRead,
                _charBuffer, 0, _charBuffer.Length,
                flush: false,
                out _,
                charsUsed: out _charLen,
                out _);
            _charPos = 0;
        }
    }

    /// <summary>
    /// Shifts any unconsumed raw bytes in _byteBuffer to position 0 and returns the count.
    /// Used when switching back to ReadLineAsync after ReadDotTerminatedBytesAsync left
    /// unconsumed bytes in the buffer.
    /// </summary>
    private int ConsumeLeftoverBytes() {
        var remaining = _byteLen - _bytePos;
        Buffer.BlockCopy(_byteBuffer, _bytePos, _byteBuffer, 0, remaining);
        _bytePos = 0;
        _byteLen = 0;
        return remaining;
    }

    /// <summary>
    /// Reads SMTP dot-terminated message data as raw bytes, handling dot-unstuffing.
    /// Scans for the CRLF.CRLF terminator at the byte level to avoid decode→string→re-encode overhead.
    /// Any leftover bytes in the internal buffer from a prior ReadLineAsync are consumed first.
    /// Returns the raw message bytes (without the terminating dot line).
    /// </summary>
    public async Task<byte[]> ReadDotTerminatedBytesAsync(CancellationToken cancellationToken) {
        using var result = new MemoryStream();
        bool atLineStart = true;

        // Helper: flush any buffered bytes from _byteBuffer[_bytePos.._byteLen]
        // that were left over from the char-decoding path.
        void DrainLeftover() {
            if (_charPos < _charLen) {
                // There are decoded chars we haven't consumed. We need to re-encode them
                // back to bytes so the raw stream stays consistent. This only happens for
                // the small tail of the last ReadLineAsync buffer — typically a few bytes.
                var leftoverChars = _charLen - _charPos;
                var maxBytes = encoding.GetMaxByteCount(leftoverChars);
                var temp = ArrayPool<byte>.Shared.Rent(maxBytes);
                try {
                    var written = encoding.GetBytes(_charBuffer, _charPos, leftoverChars, temp, 0);
                    _bytePos = 0;
                    _byteLen = written;
                    Buffer.BlockCopy(temp, 0, _byteBuffer, 0, written);
                } finally {
                    ArrayPool<byte>.Shared.Return(temp);
                }
                _charPos = _charLen = 0;
            }
        }

        DrainLeftover();

        while (true) {
            // Refill buffer if exhausted
            if (_bytePos >= _byteLen) {
                _byteLen = await stream.ReadAsync(_byteBuffer.AsMemory(0, _byteBuffer.Length), cancellationToken);
                _bytePos = 0;
                if (_byteLen == 0) {
                    break; // stream ended
                }
            }

            // Process bytes one at a time, scanning for dot-terminator
            while (_bytePos < _byteLen) {
                var b = _byteBuffer[_bytePos++];

                if (b == (byte)'\n') {
                    result.WriteByte(b);
                    atLineStart = true;
                    continue;
                }

                if (atLineStart && b == (byte)'.') {
                    // Peek at next byte to check for terminator or dot-stuffing
                    if (_bytePos >= _byteLen) {
                        _byteLen = await stream.ReadAsync(_byteBuffer.AsMemory(0, _byteBuffer.Length), cancellationToken);
                        _bytePos = 0;
                        if (_byteLen == 0) {
                            // Stream ended after lone dot — treat as terminator
                            return result.ToArray();
                        }
                    }

                    var next = _byteBuffer[_bytePos];
                    if (next == (byte)'\r' || next == (byte)'\n') {
                        // This is ".\r\n" or ".\n" — the terminator. Consume the line ending.
                        _bytePos++; // skip \r or \n
                        if (next == (byte)'\r') {
                            // Consume the \n after \r
                            if (_bytePos >= _byteLen) {
                                _byteLen = await stream.ReadAsync(_byteBuffer.AsMemory(0, _byteBuffer.Length), cancellationToken);
                                _bytePos = 0;
                            }
                            if (_bytePos < _byteLen && _byteBuffer[_bytePos] == (byte)'\n') {
                                _bytePos++;
                            }
                        }
                        // Trim trailing \r\n from the result if present
                        var bytes = result.ToArray();
                        var len = bytes.Length;
                        if (len > 0 && bytes[len - 1] == (byte)'\n') {
                            len--;
                        }

                        if (len > 0 && bytes[len - 1] == (byte)'\r') {
                            len--;
                        }

                        return bytes.AsSpan(0, len).ToArray();
                    }

                    if (next == (byte)'.') {
                        // Dot-stuffing: ".." at line start → write single "."
                        _bytePos++; // skip the second dot
                        result.WriteByte((byte)'.');
                        atLineStart = false;
                        continue;
                    }

                    // Lone dot followed by other content — write the dot
                    result.WriteByte((byte)'.');
                    atLineStart = false;
                    continue;
                }

                result.WriteByte(b);
                atLineStart = b == (byte)'\r' ? atLineStart : false; // \r doesn't reset line start
            }
        }

        return result.ToArray();
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        ArrayPool<byte>.Shared.Return(_byteBuffer);
        ArrayPool<char>.Shared.Return(_charBuffer);
    }

    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

