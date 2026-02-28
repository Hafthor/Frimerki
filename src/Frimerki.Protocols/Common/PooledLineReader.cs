using System.Buffers;
using System.Text;

namespace Frimerki.Protocols.Common;

public sealed class PooledLineReader : IAsyncDisposable, IDisposable {
    private readonly Stream _stream;
    private readonly Decoder _decoder;
    private readonly byte[] _byteBuffer;
    private readonly char[] _charBuffer;
    private readonly StringBuilder _lineBuilder = new();
    private int _charPos;
    private int _charLen;
    private bool _disposed;
    private bool _firstLine = true;

    public PooledLineReader(Stream stream, Encoding encoding, int bufferSize = 4096) {
        _stream = stream;
        _decoder = encoding.GetDecoder();
        _byteBuffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        _charBuffer = ArrayPool<char>.Shared.Rent(encoding.GetMaxCharCount(bufferSize));
    }

    public async Task<string> ReadLineAsync(CancellationToken cancellationToken) {
        while (true) {
            if (_charPos < _charLen) {
                var newlineIndex = Array.IndexOf(_charBuffer, '\n', _charPos, _charLen - _charPos);
                if (newlineIndex >= 0) {
                    var length = newlineIndex - _charPos;
                    if (length > 0 && _charBuffer[newlineIndex - 1] == '\r') {
                        length--;
                    }

                    if (length > 0) {
                        _lineBuilder.Append(_charBuffer, _charPos, length);
                    }

                    _charPos = newlineIndex + 1;
                    var line = _lineBuilder.ToString();
                    _lineBuilder.Clear();
                    if (_firstLine) {
                        _firstLine = false;
                        if (line.Length > 0 && line[0] == '\uFEFF') {
                            line = line[1..];
                        }
                    }
                    return line;
                }

                _lineBuilder.Append(_charBuffer, _charPos, _charLen - _charPos);
                _charPos = _charLen;
            }

            var bytesRead = await _stream.ReadAsync(_byteBuffer, cancellationToken);
            if (bytesRead == 0) {
                if (_lineBuilder.Length == 0) {
                    return null;
                }

                var line = _lineBuilder.ToString();
                _lineBuilder.Clear();
                if (_firstLine) {
                    _firstLine = false;
                    if (line.Length > 0 && line[0] == '\uFEFF') {
                        line = line[1..];
                    }
                }
                return line;
            }

            _decoder.Convert(
                _byteBuffer,
                0,
                bytesRead,
                _charBuffer,
                0,
                _charBuffer.Length,
                false,
                out _,
                out _charLen,
                out _);
            _charPos = 0;
        }
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

