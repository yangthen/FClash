using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FClash.Domain;

/// <summary>One GUI owns one Core, with no discovery, detach or reconnect path.</summary>
public sealed class CoreClient : IAsyncDisposable
{
    public const int MaxFrame = 32 * 1024 * 1024;
    private NamedPipeServerStream? pipe;
    private Process? process;
    private readonly SemaphoreSlim writer = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> pending = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? reader;
    private bool closing;
    public event Action<JsonObject>? Message;
    public event Action<Exception>? Disconnected;
    public bool Connected => pipe?.IsConnected == true && !lifetime.IsCancellationRequested;
    public int? ProcessId => process?.Id;

    public async Task StartAsync(string executable, CancellationToken cancellationToken = default)
    {
        if (pipe != null) throw new InvalidOperationException("Core 已启动");
        var name = $"FClashNative_{Environment.ProcessId}_{Guid.NewGuid():N}";
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            process = Process.Start(new ProcessStartInfo(executable)
            {
                ArgumentList = { @"\\.\pipe\" + name },
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!,
            }) ?? throw new IOException("无法启动 Core");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(timeout.Token);
            reader = ReadLoopAsync();
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task<JsonNode?> CallAsync(string method, object? data = null, CancellationToken cancellationToken = default)
    {
        if (!Connected) throw new IOException("Core 未连接");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, data });
            await writer.WaitAsync(timeout.Token);
            try { await WriteFrameAsync(pipe!, payload, timeout.Token); }
            finally { writer.Release(); }
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }

    public Task<JsonNode?> JsonCallAsync(string method, object data, CancellationToken token = default) =>
        CallAsync(method, JsonSerializer.Serialize(data), token);

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var bytes = await ReadFrameAsync(pipe!, lifetime.Token);
                var result = JsonNode.Parse(bytes)!.AsObject();
                var id = result["id"].Text();
                if (id.Length == 0)
                {
                    if (result["data"] is JsonObject message) Message?.Invoke(message);
                }
                else if (pending.TryRemove(id, out var completion))
                {
                    if (result.Number("code") != 0) completion.TrySetException(new IOException(result["data"].Text()));
                    else completion.TrySetResult(result["data"].Copy());
                }
            }
        }
        catch (Exception e)
        {
            foreach (var completion in pending.Values) completion.TrySetException(e);
            pending.Clear();
            if (!lifetime.IsCancellationRequested && !closing) Disconnected?.Invoke(e);
        }
    }

    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token = default)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length > MaxFrame) throw new InvalidDataException("Core frame exceeds limit");
        var data = new byte[length];
        await stream.ReadExactlyAsync(data, token);
        return data;
    }

    public static async Task WriteFrameAsync(Stream stream, byte[] data, CancellationToken token = default)
    {
        if (data.Length > MaxFrame) throw new InvalidDataException("Core frame exceeds limit");
        var frame = new byte[4 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)data.Length);
        data.CopyTo(frame, 4);
        await stream.WriteAsync(frame, token);
        await stream.FlushAsync(token);
    }

    public async ValueTask DisposeAsync()
    {
        if (lifetime.IsCancellationRequested) return;
        closing = true;
        if (Connected)
        {
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await CallAsync("terminateCore", cancellationToken: stop.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
        lifetime.Cancel();
        if (pipe != null) await pipe.DisposeAsync();
        if (reader != null) await reader;
        if (process != null)
        {
            try
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(stop.Token);
            }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process.Dispose();
        }
    }
}
