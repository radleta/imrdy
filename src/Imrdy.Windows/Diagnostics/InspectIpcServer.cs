using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Imrdy.Core;
using Imrdy.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Diagnostics;

/// <summary>
/// Named-pipe IPC server for the live-inspect feature. Accepts requests on
/// <c>Local\ImrdyInspect</c>, dispatches to registered verb handlers on the UI thread,
/// and returns JSON responses. Lifecycle: call <see cref="Start"/> once; dispose to stop.
/// </summary>
internal sealed class InspectIpcServer : IDisposable
{
    private const int MaxConcurrentConnections = 4;
    private const int MaxRequestBodyBytes = 4096;
    private const int ResponseBufferBytes = 262_144; // 256 KiB

    private readonly ILogger _logger;
    private readonly Control _uiControl;
    private readonly IReadOnlyDictionary<string, Func<InspectRequest, InspectResponse>> _handlers;

    /// <param name="loggerFactory">Used to create a typed logger.</param>
    /// <param name="uiControl">
    /// A WinForms <see cref="Control"/> whose native handle is guaranteed to exist on the UI thread.
    /// <see cref="Control.BeginInvoke"/> is used to marshal handler calls to the UI thread.
    /// Pass <c>controllerIcon.ContextMenuStrip</c> (handle forced at TrayApp ctor line ~167).
    /// </param>
    /// <param name="handlers">Verb → handler map; must include at least the <c>"ping"</c> verb.</param>
    public InspectIpcServer(
        ILoggerFactory loggerFactory,
        Control uiControl,
        IReadOnlyDictionary<string, Func<InspectRequest, InspectResponse>> handlers)
    {
        _logger = loggerFactory.CreateLogger<InspectIpcServer>();
        _uiControl = uiControl;
        _handlers = handlers;
    }

    /// <summary>
    /// Launches <see cref="MaxConcurrentConnections"/> accept-loop tasks on the thread pool.
    /// Each loop creates a fresh <see cref="NamedPipeServerStream"/> per accepted connection.
    /// Returns immediately; the loops run until <paramref name="ct"/> is cancelled.
    /// </summary>
    public void Start(CancellationToken ct)
    {
        PipeSecurity acl;
        try
        {
            acl = BuildAcl();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InspectIpc: could not build PipeSecurity ACL — IPC server not started");
            return;
        }

        for (var i = 0; i < MaxConcurrentConnections; i++)
        {
            var loopIndex = i;
            Task.Run(() => AcceptLoopAsync(loopIndex, acl, ct), ct);
        }

        _logger.LogDebug("InspectIpc: server listening on {Pipe} (max={Max})",
            ImrdyPaths.InspectPipeName, MaxConcurrentConnections);
        _logger.LogInformation("InspectIpc: server started");
    }

    private async Task AcceptLoopAsync(int loopIndex, PipeSecurity acl, CancellationToken ct)
    {
        var connectionCount = 0;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(
                    pipeName: "ImrdyInspect",
                    PipeDirection.InOut,
                    MaxConcurrentConnections,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: MaxRequestBodyBytes,
                    outBufferSize: ResponseBufferBytes,
                    pipeSecurity: acl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InspectIpc[{Loop}]: failed to create server stream", loopIndex);
                break;
            }

            try
            {
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                connectionCount++;
                _logger.LogDebug("InspectIpc[{Loop}]: accepted connection #{N}", loopIndex, connectionCount);
                await HandleConnectionAsync(server, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                server.Dispose();
                return; // exit without falling through to the outer loop
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "InspectIpc[{Loop}]: accept-loop error", loopIndex);
            }
            finally
            {
                // Disposes on every path except OperationCanceledException (already disposed above).
                // NamedPipeServerStream.Dispose is idempotent so the rare race is harmless.
                server.Dispose();
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        InspectResponse response;
        try
        {
            // Read 4-byte little-endian length prefix
            var lenBuf = new byte[4];
            await ReadExactlyAsync(server, lenBuf, ct).ConfigureAwait(false);
            var bodyLen = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);

            if (bodyLen <= 0 || bodyLen > MaxRequestBodyBytes)
            {
                // Verb is intrinsically empty for pre-deserialization errors — see docs/dashboard-inspect-schema.md.
                response = ErrorResponse("", $"request exceeds maximum size ({MaxRequestBodyBytes} bytes)");
                await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
                return;
            }

            // Read body
            var bodyBuf = new byte[bodyLen];
            await ReadExactlyAsync(server, bodyBuf, ct).ConfigureAwait(false);

            // Deserialize
            InspectRequest? req;
            try
            {
                req = JsonSerializer.Deserialize(bodyBuf, ImrdyJsonContext.Default.InspectRequest);
            }
            catch (JsonException ex)
            {
                response = ErrorResponse("", $"invalid request: {ex.Message}");
                await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
                return;
            }

            if (req is null)
            {
                response = ErrorResponse("", "invalid request: null body");
                await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
                return;
            }

            if (string.IsNullOrEmpty(req.Verb))
            {
                response = ErrorResponse("", "invalid request: missing verb");
                await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
                return;
            }

            if (!_handlers.TryGetValue(req.Verb, out var handler))
            {
                response = ErrorResponse(req.Verb, $"unknown verb: {req.Verb}");
                await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
                return;
            }

            // Dispatch to UI thread via BeginInvoke + TaskCompletionSource bridge (D2)
            var tcs = new TaskCompletionSource<InspectResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _uiControl.BeginInvoke(new Action(() =>
            {
                try { tcs.SetResult(handler(req)); }
                catch (Exception ex) { tcs.SetException(ex); }
            }));

            try
            {
                response = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                response = ErrorResponse(req.Verb, "timeout: handler exceeded 2 s");
                _ = tcs.Task.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        _logger.LogWarning(t.Exception, "IPC handler exception after timeout — slot was already recycled");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InspectIpc: request handling error");
            response = ErrorResponse("", $"internal error: {ex.Message}");
        }

        try
        {
            await WriteResponseAsync(server, response, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InspectIpc: failed to write response");
        }
    }

    private static async Task ReadExactlyAsync(PipeStream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Pipe closed before full message was received");
            offset += read;
        }
    }

    private static async Task WriteResponseAsync(PipeStream stream, InspectResponse response, CancellationToken ct)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, ImrdyJsonContext.Default.InspectResponse);
        var lenBuf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lenBuf, json.Length);
        await stream.WriteAsync(lenBuf, ct).ConfigureAwait(false);
        await stream.WriteAsync(json, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static PipeSecurity BuildAcl()
    {
        var ps = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User!;
        ps.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return ps;
    }

    private static InspectResponse ErrorResponse(string verb, string error) =>
        new InspectResponse("1", verb, error, null, null);

    public void Dispose()
    {
        // Accept loops stop when the CancellationToken passed to Start() is cancelled.
        // TrayApp cancels _shutdownCts before calling Dispose(), so loops are already
        // winding down by the time this is invoked.
    }
}
