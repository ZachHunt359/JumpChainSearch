namespace JumpChainSearch.Services;

public sealed class DriveScanCoordinator
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _stateLock = new();
    private string? _activeOperation;

    public string? ActiveOperation
    {
        get
        {
            lock (_stateLock)
            {
                return _activeOperation;
            }
        }
    }

    public bool TryAcquire(string operation, out IDisposable? lease)
    {
        if (!_scanGate.Wait(0))
        {
            lease = null;
            return false;
        }

        lock (_stateLock)
        {
            _activeOperation = operation;
        }

        lease = new ScanLease(this);
        return true;
    }

    private void Release()
    {
        lock (_stateLock)
        {
            _activeOperation = null;
        }

        _scanGate.Release();
    }

    private sealed class ScanLease : IDisposable
    {
        private DriveScanCoordinator? _coordinator;

        public ScanLease(DriveScanCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _coordinator, null)?.Release();
        }
    }
}