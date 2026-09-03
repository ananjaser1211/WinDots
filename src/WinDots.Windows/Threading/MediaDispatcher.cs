using System.Collections.Concurrent;

namespace WinDots.Windows.Threading;

/// <summary>
/// A single background MTA thread that owns every Windows Runtime media object the adapters touch.
/// WinRT interface pointers obtained on one thread fault with RPC_E_WRONG_THREAD when used raw from another,
/// so all platform calls are funnelled here. It doubles as a <see cref="SynchronizationContext"/> so that
/// <c>await</c> inside queued work resumes on the same thread.
/// </summary>
/// <remarks>
/// Shutdown contract: <see cref="Dispose"/> stops accepting new work and lets the thread drain what is already
/// queued. Work posted after that point (including <c>await</c> continuations of in-flight work) is not dropped
/// silently, which would leave callers of <see cref="InvokeAsync{T}(Func{Task{T}})"/> waiting forever; it runs on
/// the thread pool instead, where a platform call may fault and complete the caller's task with that fault.
/// </remarks>
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

    public bool IsDisposed => _disposed;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (TryEnqueue(d, state))
        {
            return;
        }

        // Shutting down: never strand a continuation. Run it off-thread so awaiting callers complete (possibly faulted).
        ThreadPool.UnsafeQueueUserWorkItem(static tuple => RunGuarded(tuple.Callback, tuple.State), (Callback: d, State: state), preferLocal: false);
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

    /// <summary>
    /// Runs <paramref name="work"/> on the dispatcher thread. Throws <see cref="ObjectDisposedException"/>
    /// synchronously when the dispatcher has been disposed; once queued the returned task always completes.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = TryEnqueue(async _ =>
        {
            try
            {
                tcs.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }, null);

        if (!queued)
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(MediaDispatcher)));
        }

        return tcs.Task;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Loop already tore the queue down.
        }

        if (!IsOnDispatcherThread)
        {
            // The loop disposes the queue when it finishes draining; disposing it here would fault the loop
            // if we are called from the dispatcher thread itself (a Dispose from inside queued work).
            _thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    private bool TryEnqueue(SendOrPostCallback d, object? state)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            _queue.Add((d, state));
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Queue completed or torn down during shutdown.
            return false;
        }
    }

    private static void RunGuarded(SendOrPostCallback callback, object? state)
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

    private void Loop()
    {
        SetSynchronizationContext(this);
        try
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                RunGuarded(callback, state);
            }
        }
        catch (ObjectDisposedException)
        {
            // Queue torn down underneath us; nothing left to run.
        }
        finally
        {
            _queue.Dispose();
        }
    }
}
