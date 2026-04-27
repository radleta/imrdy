using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Diagnostics;

namespace Imrdy.Windows.Diagnostics;

/// <summary>
/// Thin synchronous client for the <c>Local\ImrdyInspect</c> named-pipe IPC server.
/// Stateless — every call opens a fresh connection and disposes it before returning.
/// </summary>
internal static class InspectIpcClient
{
    private const int MaxRequestBodyBytes = 4096;
    private const int MaxResponseBodyBytes = 262_144; // 256 KiB

    /// <summary>
    /// Sends <paramref name="request"/> to the tray's IPC server and returns the response.
    /// </summary>
    /// <param name="request">The request to send; serialized body must not exceed 4 096 bytes.</param>
    /// <param name="timeout">
    /// Total budget for the <see cref="NamedPipeClientStream.Connect(int)"/> call.
    /// The read/write phase uses the remaining wall-clock budget; timeouts there propagate raw.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no server is listening within <paramref name="timeout"/> (wraps the
    /// original <see cref="TimeoutException"/> with an actionable message).
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the serialized request body exceeds <see cref="MaxRequestBodyBytes"/>.
    /// </exception>
    public static InspectResponse Send(InspectRequest request, TimeSpan timeout)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(request, ImrdyJsonContext.Default.InspectRequest);
        if (body.Length > MaxRequestBodyBytes)
            throw new ArgumentException(
                $"Serialized request body is {body.Length} bytes, which exceeds the {MaxRequestBodyBytes}-byte maximum.",
                nameof(request));

        using var client = new NamedPipeClientStream(".", "ImrdyInspect", PipeDirection.InOut);

        try
        {
            client.Connect((int)timeout.TotalMilliseconds);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                "Tray not running. Start the tray with 'imrdy' (or check that diagnostics IPC is enabled in ~/.imrdy/config.json).",
                ex);
        }

        // Write length-prefixed request
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, body.Length);
        client.Write(lenBuf, 0, 4);
        client.Write(body, 0, body.Length);
        client.WaitForPipeDrain();

        // Read length-prefixed response
        ReadExactly(client, lenBuf, 4);
        var responseLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
        if (responseLen <= 0 || responseLen > MaxResponseBodyBytes)
            throw new InvalidDataException(
                $"Server returned an invalid response length: {responseLen}");

        var responseBuf = new byte[responseLen];
        ReadExactly(client, responseBuf, responseLen);

        return JsonSerializer.Deserialize(responseBuf, ImrdyJsonContext.Default.InspectResponse)
            ?? throw new InvalidDataException("Server returned a null response body");
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
                throw new EndOfStreamException("Pipe closed before full response was received");
            offset += read;
        }
    }
}
