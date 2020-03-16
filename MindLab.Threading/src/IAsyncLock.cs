using System;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Threading
{
    /// <summary>提供基于async/await异步阻塞的互斥锁</summary>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// public class MyClass
    /// {
    ///     private readonly IAsyncLock m_lock;
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
    public interface IAsyncLock
    {
        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        Task<IDisposable> LockAsync(CancellationToken cancellation = default);

        /// <summary>
        /// 尝试进入临界区
        /// </summary>
        /// <param name="lockDisposer"></param>
        /// <returns></returns>
        bool TryLock(out IDisposable lockDisposer);
    }
}
