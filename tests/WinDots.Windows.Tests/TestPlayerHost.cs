using System.Diagnostics;

namespace WinDots.Windows.Tests;

/// <summary>Launches WinDots.TestPlayer as a child process and exposes its stdin/stdout for scripted playback.</summary>
public sealed class TestPlayerHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly List<string> _lines = new();
    private readonly object _gate = new();

    private TestPlayerHost(Process process)
    {
        _process = process;
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            lock (_gate)
            {
                _lines.Add(e.Data);
            }
        };
        process.BeginOutputReadLine();
    }

    public static async Task<TestPlayerHost> StartAsync(CancellationToken ct)
    {
        var exe = Path.Combine(AppContext.BaseDirectory, "WinDots.TestPlayer.exe");
        Assert.True(File.Exists(exe), $"Test player not found at {exe}. Build tests/WinDots.TestPlayer first.");

        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the test player.");
        var host = new TestPlayerHost(process);
        await host.WaitForLineAsync(l => l == "[ready]", TimeSpan.FromSeconds(15), ct);
        return host;
    }

    public async Task SendAsync(string command)
    {
        await _process.StandardInput.WriteLineAsync(command);
        await _process.StandardInput.FlushAsync();
    }

    public async Task<string> WaitForLineAsync(Func<string, bool> predicate, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        var scanned = 0;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                for (; scanned < _lines.Count; scanned++)
                {
                    if (predicate(_lines[scanned]))
                    {
                        return _lines[scanned];
                    }
                }
            }

            await Task.Delay(50, ct);
        }

        string dump;
        lock (_gate)
        {
            dump = string.Join(Environment.NewLine, _lines);
        }

        throw new TimeoutException($"Test player did not print the expected line within {timeout}. Output so far:{Environment.NewLine}{dump}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await SendAsync("quit");
                if (!_process.WaitForExit(5000))
                {
                    _process.Kill();
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            _process.Dispose();
        }
    }
}
