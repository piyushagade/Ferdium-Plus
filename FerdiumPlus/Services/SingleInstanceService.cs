using System.Threading;

namespace FerdiumApp.Services;

/// <summary>
/// Enforces a single running instance per user session using a named Mutex and
/// signals the existing instance via an EventWaitHandle to restore &amp; focus its window.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activateEvent;
    private readonly RegisteredWaitHandle? _registeredWait;
    private readonly bool _ownsMutex;
    private bool _disposed;

    public bool IsPrimaryInstance => _ownsMutex;

    public SingleInstanceService(
        string appKey = "FerdiumPlus.SingleInstance",
        Action? onActivationRequested = null)
    {
        var mutexName = $@"Local\{appKey}.Mutex";
        var eventName = $@"Local\{appKey}.Activate";

        _mutex = new Mutex(initiallyOwned: true, name: mutexName, createdNew: out _ownsMutex);

        if (_ownsMutex)
        {
            _activateEvent = new EventWaitHandle(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: eventName);

            if (onActivationRequested is not null)
            {
                _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                    _activateEvent,
                    (_, _) => onActivationRequested(),
                    state: null,
                    millisecondsTimeOutInterval: Timeout.Infinite,
                    executeOnlyOnce: false);
            }
        }
        else
        {
            // Signal the running primary instance to restore/focus itself
            try
            {
                using var existingEvent = EventWaitHandle.OpenExisting(eventName);
                existingEvent.Set();
            }
            catch
            {
                // Primary instance may be starting up or shutting down
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _registeredWait?.Unregister(null);
        _activateEvent?.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch
            {
                // Ignore if already released
            }
        }

        _mutex.Dispose();
    }
}
