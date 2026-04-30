using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using Imrdy.Core;
using Imrdy.Core.Diagnostics;
using Imrdy.Windows.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Imrdy.Windows.Tests.Diagnostics;

/// <summary>
/// Verifies that <see cref="InspectIpcServer"/> returns a schema-conforming error response
/// (non-null <c>Error</c>, empty <c>Verb</c>) for pre-deserialization failures.
/// Each test spins up a real server on the well-known pipe and connects with a raw client.
/// Tests in this class run sequentially (xunit default for a single class) so pipe-name
/// conflicts between cases are not possible.
/// </summary>
public class InspectIpcServerTests
{
    // Pipe name must match the server constant — "." = local machine.
    private const string ServerName = ".";
    private const string PipeName = "ImrdyInspect";

    /// <summary>
    /// Runs <paramref name="testBody"/> on a fresh STA thread and re-raises any exception
    /// on the calling (xunit MTA) thread. Matches the pattern in <see cref="InspectServiceTests"/>.
    /// </summary>
    private static void RunOnSta(Action testBody)
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try { testBody(); }
            catch (Exception ex) { threadEx = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadEx is not null)
            throw new InvalidOperationException($"STA thread threw: {threadEx.Message}", threadEx);
    }

    /// <summary>
    /// Starts the server on an STA thread, runs <paramref name="exerciseClient"/>, then
    /// cancels and returns the parsed response. WinForms Control construction follows the
    /// project convention of occurring on an STA thread.
    /// </summary>
    private static InspectResponse RunWithServer(Action<NamedPipeClientStream> exerciseClient)
    {
        InspectResponse? response = null;

        RunOnSta(() =>
        {
            using var cts = new CancellationTokenSource();

            // Control construction on STA thread per project convention (see InspectServiceTests).
            using var uiControl = new System.Windows.Forms.Control();

            var emptyHandlers = new Dictionary<string, Func<InspectRequest, InspectResponse>>();
            using var server = new InspectIpcServer(NullLoggerFactory.Instance, uiControl, emptyHandlers);
            server.Start(cts.Token);

            // Retry-connect loop: attempt to connect up to 10 times with a short backoff so
            // the accept loop has time to create the pipe without a fixed sleep.
            NamedPipeClientStream? client = null;
            const int MaxAttempts = 10;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    client = new NamedPipeClientStream(ServerName, PipeName, PipeDirection.InOut);
                    client.Connect(timeout: 200);
                    break;
                }
                catch (TimeoutException)
                {
                    client?.Dispose();
                    client = null;
                    if (attempt == MaxAttempts - 1)
                        throw;
                    System.Threading.Thread.Sleep(20);
                }
            }

            var connectedClient = client!;
            using (connectedClient)
            {
                exerciseClient(connectedClient);
                response = ReadResponse(connectedClient);
            }

            cts.Cancel();
        });

        return response!;
    }

    /// <summary>Reads a length-prefixed JSON response from the pipe.</summary>
    private static InspectResponse ReadResponse(NamedPipeClientStream client)
    {
        var lenBuf = new byte[4];
        ReadExactly(client, lenBuf, 4);
        var bodyLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);

        bodyLen.Should().BeGreaterThan(0).And.BeLessThan(262_144, "response length must be within bounds");

        var bodyBuf = new byte[bodyLen];
        ReadExactly(client, bodyBuf, bodyLen);

        return JsonSerializer.Deserialize(bodyBuf, ImrdyJsonContext.Default.InspectResponse)
            ?? throw new InvalidDataException("Server returned a null response body");
    }

    private static void ReadExactly(Stream stream, byte[] buf, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buf, offset, count - offset);
            if (read == 0)
                throw new EndOfStreamException("Pipe closed before full message was received");
            offset += read;
        }
    }

    private static void WriteInt32Le(Stream stream, int value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        stream.Write(buf, 0, 4);
    }

    // ---- Tests ----

    [Fact]
    public void OversizedRequest_ReturnsErrorWithEmptyVerb()
    {
        // Arrange: send a length prefix that exceeds the 4 KiB limit.
        const int oversizedLen = 4097;

        var response = RunWithServer(client =>
        {
            // Send only the length prefix; no body (server rejects before reading body).
            WriteInt32Le(client, oversizedLen);
            client.WaitForPipeDrain();
        });

        // Assert
        response.Error.Should().NotBeNull("an oversized request must produce an error");
        response.Error!.Should().Contain("exceeds", "error message must mention the size constraint");
        response.Verb.Should().Be("", "verb is intrinsically empty before the body is read");
    }

    [Fact]
    public void MalformedJson_ReturnsErrorWithEmptyVerb()
    {
        // Arrange: send a well-formed length prefix but a body that is not valid JSON.
        var body = "not valid json {"u8.ToArray();

        var response = RunWithServer(client =>
        {
            WriteInt32Le(client, body.Length);
            client.Write(body, 0, body.Length);
            client.WaitForPipeDrain();
        });

        // Assert
        response.Error.Should().NotBeNull("a malformed-JSON body must produce an error");
        response.Error!.ToLowerInvariant()
            .Should().MatchRegex("json|invalid|parse",
                "error message must indicate a JSON parse failure");
        response.Verb.Should().Be("", "verb is empty when JSON deserialization fails");
    }

    [Fact]
    public void NullJsonBody_ReturnsErrorWithEmptyVerb()
    {
        // Arrange: send the JSON literal "null" which deserializes InspectRequest? to null.
        var body = "null"u8.ToArray();

        var response = RunWithServer(client =>
        {
            WriteInt32Le(client, body.Length);
            client.Write(body, 0, body.Length);
            client.WaitForPipeDrain();
        });

        // Assert
        response.Error.Should().NotBeNull("a null-deserializing body must produce an error");
        response.Error!.ToLowerInvariant()
            .Should().MatchRegex("null|empty|invalid",
                "error message must indicate the request was null");
        response.Verb.Should().Be("", "verb is empty when the deserialized request is null");
    }
}
