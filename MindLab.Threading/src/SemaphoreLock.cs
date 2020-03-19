using System;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Internals;

namespace MindLab.Threading
{
    /// <summary>
    /// 提供基于<see cref="SemaphoreSlim"/>实现的异步互斥锁
    /// </summary>
    public class SemaphoreLock : IAsyncLock, ILockDisposable
    {
        private readonly SemaphoreSlim m_semaphore = new SemaphoreSlim(1,1);

        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IAsyncDisposable> LockAsync(CancellationToken cancellation = default)
        {
            await m_semaphore.WaitAsync(cancellation);
            return new LockDisposer(this);
        }

        /// <summary>
        /// 尝试进入临界区
        /// </summary>
        /// <param name="lockDisposer"></param>
        /// <returns></returns>
        public bool TryLock(out IAsyncDisposable lockDisposer)
        {
            lockDisposer = null;
            if (!m_semaphore.Wait(0))
            {
                return false;
            }

            lockDisposer = new LockDisposer(this);
            return true;
        }

        private void InternalUnlock()
        {
            m_semaphore.Release();
        }

        Task ILockDisposable.InternalUnlockAsync()
        {
            InternalUnlock();
            return Task.CompletedTask;
        }
    }
}
