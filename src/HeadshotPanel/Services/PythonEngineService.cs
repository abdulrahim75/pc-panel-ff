using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace HeadshotPanel.Services;

public sealed class PythonEngineService : IDisposable
{
    private readonly object _processLock = new();
    private Process? _process;
    private TcpListener? _frameListener;
    private CancellationTokenSource? _frameCancellation;
    private Task? _frameReceiverTask;

    public bool IsRunning => _process is { HasExited: false };
    public string? LastError { get; private set; }

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler<int>? ProcessExited;
    public event EventHandler? StatusChanged;
    public event EventHandler<byte[]>? FrameReceived;
    public event EventHandler<string>? FrameErrorReceived;

    public bool Start(string videoPath, string modelPath)
    {
        lock (_processLock)
        {
            if (IsRunning)
            {
                LastError = "The Python engine is already running.";
                StatusChanged?.Invoke(this, EventArgs.Empty);
                return false;
            }

            var engineRoot = FindEngineRoot();
            var pythonExecutable = Path.Combine(engineRoot, ".venv", "Scripts", "python.exe");
            var scriptPath = Path.Combine(engineRoot, "main.py");

            if (!File.Exists(pythonExecutable))
            {
                LastError = $"Python environment was not found: {pythonExecutable}";
                ErrorReceived?.Invoke(this, LastError);
                return false;
            }

            if (!File.Exists(scriptPath))
            {
                LastError = $"Python engine entry point was not found: {scriptPath}";
                ErrorReceived?.Invoke(this, LastError);
                return false;
            }

            if (!File.Exists(videoPath))
            {
                LastError = $"Video file was not found: {videoPath}";
                ErrorReceived?.Invoke(this, LastError);
                return false;
            }

            if (!File.Exists(modelPath))
            {
                LastError = $"YOLO model was not found: {modelPath}";
                ErrorReceived?.Invoke(this, LastError);
                return false;
            }

            LastError = null;
            var framePort = StartFrameReceiver();
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExecutable,
                WorkingDirectory = engineRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(Path.GetFullPath(videoPath));
            startInfo.ArgumentList.Add("--model");
            startInfo.ArgumentList.Add(Path.GetFullPath(modelPath));
            startInfo.ArgumentList.Add("--frame-port");
            startInfo.ArgumentList.Add(framePort.ToString(System.Globalization.CultureInfo.InvariantCulture));

            try
            {
                var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                process.OutputDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                        OutputReceived?.Invoke(this, args.Data);
                };
                process.ErrorDataReceived += (_, args) =>
                {
                    if (!string.IsNullOrWhiteSpace(args.Data))
                    {
                        LastError = args.Data;
                        ErrorReceived?.Invoke(this, args.Data);
                    }
                };
                process.Exited += (_, _) =>
                {
                    lock (_processLock)
                    {
                        var exitCode = process.ExitCode;
                        _process = null;
                        StopFrameReceiver();
                        ProcessExited?.Invoke(this, exitCode);
                        StatusChanged?.Invoke(this, EventArgs.Empty);
                    }
                };

                if (!process.Start())
                    throw new InvalidOperationException("The Python process could not be started.");

                _process = process;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                StatusChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception exception)
            {
                StopFrameReceiver();
                LastError = exception.Message;
                ErrorReceived?.Invoke(this, exception.Message);
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_processLock)
        {
            if (_process is not { HasExited: false } process)
                return;

            try
            {
                process.CloseMainWindow();
                if (!process.WaitForExit(1500))
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
                _process = null;
                StopFrameReceiver();
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static string FindEngineRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "python-engine");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "The python-engine directory could not be located relative to the application.");
    }

    private int StartFrameReceiver()
    {
        _frameCancellation?.Dispose();
        _frameCancellation = new CancellationTokenSource();
        var cancellationToken = _frameCancellation.Token;
        var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _frameListener = new TcpListener(IPAddress.Loopback, 0);
        _frameListener.Start();
        var port = ((IPEndPoint)_frameListener.LocalEndpoint).Port;

        _frameReceiverTask = Task.Run(async () =>
        {
            try
            {
                using var client = await _frameListener.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                var header = new byte[sizeof(uint)];

                while (!cancellationToken.IsCancellationRequested)
                {
                    await ReadExactAsync(stream, header.AsMemory(), cancellationToken);
                    var length = BitConverter.ToUInt32(header.AsSpan()) <= 16 * 1024 * 1024
                        ? BitConverter.ToUInt32(header.AsSpan())
                        : throw new InvalidDataException("JPEG frame exceeded the 16 MB limit.");
                    var frame = new byte[length];
                    await ReadExactAsync(stream, frame.AsMemory(), cancellationToken);
                    await frames.Writer.WriteAsync(frame, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                FrameErrorReceived?.Invoke(this, exception.Message);
            }
            finally
            {
                frames.Writer.TryComplete();
                _frameListener?.Stop();
            }
        }, cancellationToken);

        _ = DispatchFramesAsync(frames.Reader, cancellationToken);
        return port;
    }

    private async Task DispatchFramesAsync(ChannelReader<byte[]> reader, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in reader.ReadAllAsync(cancellationToken))
            {
                FrameReceived?.Invoke(this, frame);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task ReadExactAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The Python frame stream closed unexpectedly.");
            offset += read;
        }
    }

    private void StopFrameReceiver()
    {
        _frameCancellation?.Cancel();
        _frameListener?.Stop();
        _frameListener = null;
    }
}
