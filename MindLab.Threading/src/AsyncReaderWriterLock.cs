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
        private readonly object m_lock = new object();
        #endregion

        #region Public Methods

        /// <summary>
        /// 进入读锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象释放读锁</returns>
        public async Task<IDisposable> WaitForReadAsync(CancellationToken cancellation = default)
        {
            var waiter = new LockWaiterEvent();
            LinkedListNode<LockWaiterEvent> node;
            lock(m_lock)
            {
                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                    node = m_readingList.AddLast(waiter);
                    m_currentOperation = CurrentOperation.Reading;
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

            var disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitReadLock, node);

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
                    disposer.Dispose();
                    throw new OperationCanceledException(cancellation);
                }
            }

            return disposer;
        }

        /// <summary>
        /// 进入写锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IDisposable> WaitForWriteAsync(CancellationToken cancellation = default)
        {
            var waiter = new LockWaiterEvent();
            LinkedListNode<LockWaiterEvent> node;
            lock (m_lock)
            {
                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                }

                node = m_pendingWriterList.AddFirst(waiter);
                m_currentOperation = CurrentOperation.Writing;
            }

            var disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitWriteLock, node);

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
                    disposer.Dispose();
                    throw new OperationCanceledException(cancellation);
                }
                return disposer;
            }
        }

        /// <summary>
        /// 尝试进入读锁
        /// </summary>
        public bool TryEnterReadAsync(out IDisposable disposer)
        {
            disposer = null;
            var waiter = new LockWaiterEvent();

            lock (m_lock)
            {
                LinkedListNode<LockWaiterEvent> node;

                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                    node = m_readingList.AddLast(waiter);
                    m_currentOperation = CurrentOperation.Reading;
                    disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitReadLock, node);
                    return true;
                }

                if (m_currentOperation == CurrentOperation.Reading)
                {
                    if (m_pendingWriterList.Any())
                    {
                        return false;
                    }

                    waiter.SetResult(LockStatus.Activated);
                    node = m_readingList.AddLast(waiter);
                    disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitReadLock, node);
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// 尝试进入写锁
        /// </summary>
        /// <returns>如果不能立刻获得写锁, 则返回null</returns>
        public bool TryEnterWrite(out IDisposable disposer)
        {
            disposer = null;
            var waiter = new LockWaiterEvent();
            lock (m_lock)
            {
                if (m_currentOperation != CurrentOperation.None)
                {
                    return false;
                }

                waiter.SetResult(LockStatus.Activated);
                var node = m_pendingWriterList.AddFirst(waiter);
                m_currentOperation = CurrentOperation.Writing;
                disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitWriteLock, node);
                return true;
            }
        }

        #endregion

        #region Private methods

        private void InternalExitReadLock(LinkedListNode<LockWaiterEvent> node)
        {
            lock (m_lock)
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
        }

        private void InternalExitWriteLock(LinkedListNode<LockWaiterEvent> node)
        {
            lock (m_lock)
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

        #endregion
    }
}
