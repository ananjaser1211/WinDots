using System.Collections.Concurrent;

namespace WinDots.Windows.Threading;

/// <summary>
/// A single background MTA thread that owns every Windows Runtime media object the adapters touch.
/// WinRT interface pointers obtained on one thread fault with RPC_E_WRONG_THREAD when used raw from another,
/// so all platform calls are funnelled here. It doubles as a <see cref="SynchronizationContext"/> so that
/// <c>await</c> inside queued work resumes on the same thread.
/// </summary>
public sealed class MediaDispatcher : SynchronizationContext, IDisposable
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private readonly Thread _thread;
    private volatile bool _disposed;

    public MediaDispatcher()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "WinDots.Media",
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public bool IsOnDispatcherThread => Thread.CurrentThread == _thread;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _queue.Add((d, state));
        }
        catch (InvalidOperationException)
        {
            // Queue completed during shutdown; drop the work.
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (IsOnDispatcherThread)
        {
            d(state);
            return;
        }

        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                done.Set();
            }
        }, null);
        done.Wait();
        if (error is not null)
        {
            throw new InvalidOperationException("Dispatcher work faulted.", error);
        }
    }

    public Task InvokeAsync(Func<Task> work) => InvokeAsync(async () =>
    {
        await work();
        return true;
    });

    public Task<T> InvokeAsync<T>(Func<T> work) => InvokeAsync(() => Task.FromResult(work()));

    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(async _ =>
        {
            try
            {
                tcs.SetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        if (!IsOnDispatcherThread)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        _queue.Dispose();
    }

    private void Loop()
    {
        SetSynchronizationContext(this);
        foreach (var (callback, state) in _queue.GetConsumingEnumerable())
        {
            try
            {
                callback(state);
            }
            catch (Exception)
            {
                // Callbacks own their error handling; a stray exception must not kill the media thread.
            }
        }
    }
}
