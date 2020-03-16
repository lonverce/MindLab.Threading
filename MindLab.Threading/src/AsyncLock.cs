using System;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Threading
{
    using Internals;

    /// <summary>提供基于async/await异步阻塞的互斥锁</summary>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// public class MyClass
    /// {
    ///     private readonly AsyncLock m_lock = new AsyncLock();
    ///     private int m_value;    
    /// 
    ///     public async Task IncreaseAsync(CancellationToken cancellation)
    ///     {
    ///         using(await m_lock.LockAsync(cancellation))
    ///         {
    ///             m_value++;
    ///         }
    ///     }
    ///     
    ///     public bool TryIncrease()
    ///     {
    ///         if(!m_lock.TryLock(out var locker))
    ///         {
    ///             return false;
    ///         }
    ///         m_value++;
    ///         locker.Dispose();
    ///     }
    ///     
    ///     public async Task DecreaseAsync(CancellationToken cancellation)
    ///     {
    ///         using(await m_lock.LockAsync(cancellation))
    ///         {
    ///             m_value--;
    ///         }
    ///     }
    /// }
    /// ]]>
    /// </code>
    /// </example>
    public class AsyncLock
    {
        private readonly SemaphoreSlim m_semaphore = new SemaphoreSlim(1,1);

        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IDisposable> LockAsync(CancellationToken cancellation = default)
        {
            await m_semaphore.WaitAsync(cancellation);
            return new SemaphoreReleaseOnce(m_semaphore);
        }

        /// <summary>
        /// 尝试进入临界区
        /// </summary>
        /// <param name="lockDisposer"></param>
        /// <returns></returns>
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
