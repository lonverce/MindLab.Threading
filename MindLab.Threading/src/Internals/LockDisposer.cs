using System;
using System.Threading.Tasks;

namespace MindLab.Threading.Internals
{
    internal interface ILockDisposable
    {
        Task InternalUnlockAsync();
    }

    internal class LockDisposer : IAsyncDisposable
    {
        private readonly ILockDisposable m_locker;
        private readonly OnceFlag m_flag = new OnceFlag();

        public LockDisposer(ILockDisposable locker)
        {
            m_locker = locker;
        }

        ~LockDisposer()
        {
            DisposeAsync(false).AsTask().Wait();
        }

        private async ValueTask DisposeAsync(bool disposing)
        {
            if (!m_flag.TrySet())
            {
                return;
            }

            await m_locker.InternalUnlockAsync();

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            return DisposeAsync(true);
        }
    }
}