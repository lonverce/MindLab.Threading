using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Core;

namespace MindLab.Threading
{
    /// <summary>提供基于<see cref="Monitor"/>实现的异步互斥锁</summary>
    /// <seealso cref="IAsyncLock"/>
    public sealed class MonitorLock : AbstractLock
    {
        private readonly object m_locker = new object();

        /// <summary>
        /// 进入对象数据保护锁
        /// </summary>
        protected override Task EnterLockAsync(CancellationToken cancellation)
        {
            Monitor.Enter(m_locker);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 尝试进入对象数据保护锁
        /// </summary>
        protected override bool TryEnterLock()
        {
            return Monitor.TryEnter(m_locker);
        }

        /// <summary>
        /// 退出对象数据保护锁
        /// </summary>
        protected override void ExitLock()
        {
            Monitor.Exit(m_locker);
        }
    }
}
