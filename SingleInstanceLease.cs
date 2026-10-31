using System.Threading;

namespace ChihDesktop;

// Acquired and disposed on the WPF thread, like the original mutex implementation.
internal sealed class SingleInstanceLease : IDisposable
{
    private readonly Mutex mutex;
    private bool disposed;
    private SingleInstanceLease(Mutex mutex) => this.mutex = mutex;

    public static SingleInstanceLease? TryAcquire(string name = @"Local\ChihDesktop.DesktopPet")
    {
        var mutex = new Mutex(true, name, out bool created);
        if (created) return new(mutex);
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
