using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
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
        private enum CurrentOperation
        {
            None,
            Reading,
            Writing,
        }

        #region Fields
        private LinkedList<LockWaiterEvent> m_readingList = new LinkedList<LockWaiterEvent>();
        private LinkedList<LockWaiterEvent> m_pendingReaderList = new LinkedList<LockWaiterEvent>();
        private readonly LinkedList<LockWaiterEvent> m_pendingWriterList = new LinkedList<LockWaiterEvent>();

        private CurrentOperation m_currentOperation = CurrentOperation.None;
        private readonly IAsyncLock m_lock;
        #endregion

        #region Constructor
        /// <summary>
        /// 初始化一个读写锁
        /// </summary>
        public AsyncReaderWriterLock() : this(new MonitorLock())
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
        #endregion

        #region Public Methods

        /// <summary>
        /// 进入读锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象释放读锁</returns>
        public async Task<IAsyncDisposable> WaitForReadAsync(CancellationToken cancellation = default)
        {
            var waiter = new LockWaiterEvent();
            LinkedListNode<LockWaiterEvent> node;
            await using (await m_lock.LockAsync(cancellation))
            {
                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                    node = m_readingList.AddLast(waiter);
                }
                else if (m_currentOperation == CurrentOperation.Reading)
                {
                    if (m_pendingWriterList.Any())
                    {
                        node = m_pendingReaderList.AddLast(waiter);
                    }
                    else
                    {
                        waiter.SetResult(LockStatus.Activated);
                        node = m_readingList.AddLast(waiter);
                    }
                }
                else
                {
                    node = m_pendingReaderList.AddLast(waiter);
                }
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
                        UnsafeRemoveReader(node);
                        throw new OperationCanceledException(cancellation);
                    }
                }
            }

            return new AsyncOnceDisposer<LinkedListNode<LockWaiterEvent>>(ReaderDisposeFunc, node);
        }

        /// <summary>
        /// 进入写锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IAsyncDisposable> WaitForWriteAsync(CancellationToken cancellation = default)
        {
            var waiter = new LockWaiterEvent();
            LinkedListNode<LockWaiterEvent> node;
            await using (await m_lock.LockAsync(cancellation))
            {
                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                }

                node = m_pendingWriterList.AddFirst(waiter);
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
                        UnsafeRemoveWriter(node);
                    }

                    throw new OperationCanceledException(cancellation);
                }
            }

            return new AsyncOnceDisposer<LinkedListNode<LockWaiterEvent>>(WriterDisposeFunc, node);
        } 

        #endregion

        #region Private methods

        private async Task ReaderDisposeFunc(LinkedListNode<LockWaiterEvent> listNode)
        {
            await using (await m_lock.LockAsync(CancellationToken.None))
            {
                UnsafeRemoveReader(listNode);
            }
        }

        private async Task WriterDisposeFunc(LinkedListNode<LockWaiterEvent> listNode)
        {
            await using (await m_lock.LockAsync(CancellationToken.None))
            {
                UnsafeRemoveWriter(listNode);
            }
        }

        private void UnsafeRemoveReader(LinkedListNode<LockWaiterEvent> node)
        {
            var shouldActiveNext = (node.List == m_readingList)
                                   && (m_readingList.Count == 1);
            
            node.List.Remove(node);

            if (!shouldActiveNext)
            {
                return;
            }

            Contract.Assert(m_readingList.Count == 0);

            if (m_pendingWriterList.Any())
            {
                m_currentOperation = CurrentOperation.Writing;
                m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
            }
            else if (m_pendingReaderList.Any())
            {
                UnsafeActivatePendingReaders();
            }
            else
            {
                m_currentOperation = CurrentOperation.None;
            }
        }

        private void UnsafeActivatePendingReaders()
        {
            var tmp = m_readingList;
            m_readingList = m_pendingReaderList;
            m_pendingReaderList = tmp;
            m_currentOperation = CurrentOperation.Reading;

            foreach (var pendingWaiter in m_readingList)
            {
                pendingWaiter.TrySetResult(LockStatus.Activated);
            }
        }

        private void UnsafeRemoveWriter(LinkedListNode<LockWaiterEvent> node)
        {
            var shouldActiveNext = m_pendingWriterList.First == node
                                   && m_currentOperation == CurrentOperation.Writing;

            m_pendingWriterList.Remove(node);

            if (!shouldActiveNext)
            {
                return;
            }

            if (m_pendingWriterList.Any())
            {
                m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
            }
            else if (m_pendingReaderList.Any())
            {
                UnsafeActivatePendingReaders();
            }
            else
            {
                m_currentOperation = CurrentOperation.None;
            }
        } 

        #endregion
    }
}
