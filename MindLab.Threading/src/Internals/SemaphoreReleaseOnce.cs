using System;
using System.Threading;

namespace MindLab.Threading.Internals
{
    internal class SemaphoreReleaseOnce : IDisposable
    {
        private readonly SemaphoreSlim m_semaphore;
        private readonly OnceFlag m_flag = new OnceFlag();

        public SemaphoreReleaseOnce(SemaphoreSlim semaphore)
        {
            m_semaphore = semaphore;
        }

        ~SemaphoreReleaseOnce()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            m_semaphore.Release();

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            if (!m_flag.TrySet())
            {
                return;
            }

            Dispose(true);
        }
    }
}