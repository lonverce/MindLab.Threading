using System;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Threading
{
    using Internals;

    public class AsyncLock
    {
        private readonly SemaphoreSlim m_semaphore = new SemaphoreSlim(1,1);

        public async Task<IDisposable> LockAsync(CancellationToken cancellation = default)
        {
            await m_semaphore.WaitAsync(cancellation);
            return new SemaphoreReleaseOnce(m_semaphore);
        }

        public bool TryLock(out IDisposable lockDisposer)
        {
            if (!m_semaphore.Wait(0))
            {
                lockDisposer = null;
                return false;
            }

            lockDisposer = new SemaphoreReleaseOnce(m_semaphore);
            return true;
        }
    }
}
