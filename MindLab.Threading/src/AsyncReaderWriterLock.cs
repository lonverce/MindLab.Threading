using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Internals;
using LockWaiterEvent = System.Threading.Tasks.TaskCompletionSource<MindLab.Threading.Internals.LockStatus>;

namespace MindLab.Threading
{
    /// <summary>
    /// 异步读写锁
    /// </summary>
    public class AsyncReaderWriterLock
    {
        private readonly LinkedList<LockWaiterEvent> m_readerQueue = new LinkedList<LockWaiterEvent>();
        private readonly LinkedList<LockWaiterEvent> m_writerQueue = new LinkedList<LockWaiterEvent>();
        private bool m_isWriting, m_isReading;
        private readonly IAsyncLock m_lock;

        /// <summary>
        /// 初始化一个读写锁
        /// </summary>
        public AsyncReaderWriterLock():this(new MonitorLock())
        {

        }

        /// <summary>
        /// 初始化一个读写锁
        /// </summary>
        /// <param name="internalLock">内部使用的异步锁</param>
        /// <exception cref="ArgumentNullException"><paramref name="internalLock"/>为空</exception>
        public AsyncReaderWriterLock(IAsyncLock internalLock)
        {
            m_lock = internalLock ?? throw new ArgumentNullException(nameof(internalLock));
        }

        /// <summary>
        /// 进入读锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象释放读锁</returns>
        public async Task<IAsyncDisposable> WaitForReadAsync(CancellationToken cancellation = default)
        {
            var waiter = new LockWaiterEvent();
            LinkedListNode<LockWaiterEvent> node;
            var isReading = false;
            await using (await m_lock.LockAsync(cancellation))
            {
                node = m_readerQueue.AddLast(waiter);
#if DEBUG
                Contract.Assert(node != null); 
#endif
                isReading = m_isReading;
            }

            if (isReading)
            {
                waiter.SetResult(LockStatus.Activated);
            }

#if NETSTANDARD2_1
            await  
#endif
            using (cancellation.Register(() => waiter.TrySetResult(LockStatus.Cancelled)))
            {
                if (cancellation.IsCancellationRequested)
                {
                    waiter.TrySetResult(LockStatus.Cancelled);
                }

                var status = await waiter.Task;

                if (status == LockStatus.Cancelled)
                {
                    await using (await m_lock.LockAsync(CancellationToken.None))
                    {
                        m_readerQueue.Remove(node);
                        
                    }
                }
            }

            throw new NotImplementedException();
        }

        /// <summary>
        /// 进入写锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IAsyncDisposable> WaitForWriteAsync(CancellationToken cancellation = default)
        {
            throw new NotImplementedException();
        }
    }
}
