using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoXlrControl.Hardware;

public sealed class NamedPipeTransport : IAsyncDisposable
{
    public const string DefaultPipeName = "@goxlr.socket";

    private NamedPipeClientStream? _stream;

    public bool IsConnected => _stream?.IsConnected == true;

    public async Task ConnectAsync(string pipeName = DefaultPipeName, int timeoutMs = 2000, CancellationToken ct = default)
    {
        _stream?.Dispose();
        _stream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _stream.ConnectAsync(timeoutMs, ct).ConfigureAwait(false);
    }

    public async Task<JsonNode> SendAsync(JsonNode request, CancellationToken ct = default)
    {
        if (_stream is null || !_stream.IsConnected)
            throw new InvalidOperationException("Named pipe is not connected.");

        var payload = Encoding.UTF8.GetBytes(request.ToJsonString());
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(lengthBytes, (uint)payload.Length);

        await _stream.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);

        var responseLengthBytes = new byte[4];
        await ReadExactAsync(_stream, responseLengthBytes, ct).ConfigureAwait(false);
        var responseLength = BinaryPrimitives.ReadUInt32BigEndian(responseLengthBytes);
        if (responseLength is 0 or > 32 * 1024 * 1024)
            throw new InvalidDataException($"Ungültige Pipe-Antwortlänge: {responseLength}");

        var responseBytes = new byte[responseLength];
        await ReadExactAsync(_stream, responseBytes, ct).ConfigureAwait(false);
        var json = Encoding.UTF8.GetString(responseBytes);
        return JsonNode.Parse(json) ?? throw new InvalidDataException("Leere JSON-Antwort.");
    }

    public async Task<JsonNode> GetStatusAsync(CancellationToken ct = default)
    {
        // DaemonRequest::GetStatus serializes as the string "GetStatus"
        var request = JsonNode.Parse("\"GetStatus\"")!;
        var response = await SendAsync(request, ct).ConfigureAwait(false);
        return response;
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _stream = null;
        return ValueTask.CompletedTask;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Pipe geschlossen während des Lesens.");
            offset += read;
        }
    }
}
