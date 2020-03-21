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
            /// <summary>
            /// 表示当前没有任何读写操作，
            /// 此时<see cref="m_readingList"/>,
            /// <see cref="m_pendingWriterList"/>和
            /// <see cref="m_pendingReaderList"/>应该为空
            /// </summary>
            None,

            /// <summary>
            /// 表示当前有读操作，并且没有等待中的写操作，此时
            /// <see cref="m_readingList"/>应该不为空，而
            /// <see cref="m_pendingWriterList"/>和
            /// <see cref="m_pendingReaderList"/>应该为空
            /// </summary>
            Reading,

            /// <summary>
            /// 表示当前有读操作，并且有等待中的写操作，此时
            /// <see cref="m_readingList"/>和
            /// <see cref="m_pendingWriterList"/>应该不为空，而
            /// <see cref="m_pendingReaderList"/>随意
            /// </summary>
            PendingWrite,

            /// <summary>
            /// 表示当前有写操作，此时
            /// <see cref="m_pendingWriterList"/>应该不为空，
            /// <see cref="m_readingList"/>应该为空，而
            /// <see cref="m_pendingReaderList"/>随意
            /// </summary>
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
                    Contract.Assert(!m_pendingWriterList.Any());
                    waiter.SetResult(LockStatus.Activated);
                    node = m_readingList.AddLast(waiter);
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
                node = m_pendingWriterList.AddLast(waiter);

                if (m_currentOperation == CurrentOperation.None)
                {
                    waiter.SetResult(LockStatus.Activated);
                    m_currentOperation = CurrentOperation.Writing;
                }
                else if (m_currentOperation == CurrentOperation.Reading)
                {
                    Contract.Assert(m_pendingWriterList.Count == 1);
                    m_currentOperation = CurrentOperation.PendingWrite;
                }
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
            
            lock (m_lock)
            {
                if (m_currentOperation != CurrentOperation.None && m_currentOperation != CurrentOperation.Reading)
                {
                    return false;
                }
                Contract.Assert(m_pendingReaderList.Count == 0);
                Contract.Assert(m_pendingWriterList.Count == 0);

                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                var node = m_readingList.AddLast(waiter);
                m_currentOperation = CurrentOperation.Reading;
                disposer = new OnceDisposer<LinkedListNode<LockWaiterEvent>>(InternalExitReadLock, node);
                return true;
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
                Contract.Assert(m_pendingReaderList.Count == 0);
                Contract.Assert(m_pendingWriterList.Count == 0);
                Contract.Assert(m_readingList.Count == 0);

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
                
                if (m_currentOperation == CurrentOperation.PendingWrite)
                {
                    Contract.Assert(m_pendingWriterList.Count > 0);
                    m_currentOperation = CurrentOperation.Writing;
                    m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
                }
                else if(m_currentOperation == CurrentOperation.Reading)
                {
                    Contract.Assert(m_pendingReaderList.Count == 0);
                    Contract.Assert(m_pendingWriterList.Count == 0);

                    m_currentOperation = CurrentOperation.None;
                } 
            }
        }

        private void InternalExitWriteLock(LinkedListNode<LockWaiterEvent> node)
        {
            lock (m_lock)
            {
                Contract.Assert(m_currentOperation == CurrentOperation.Writing || m_currentOperation == CurrentOperation.PendingWrite);
                var shouldActiveNext = m_pendingWriterList.First == node;

                m_pendingWriterList.Remove(node);

                if (!shouldActiveNext)
                {
                    return;
                }

                if (m_currentOperation == CurrentOperation.Writing)
                {
                    Contract.Assert(m_readingList.Count == 0);

                    if (m_pendingWriterList.Any())
                    {
                        m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
                    }
                    else if(m_pendingReaderList.Count > 0)
                    {
                        UnsafeActivatePendingReaders();
                    }
                    else
                    {
                        m_currentOperation = CurrentOperation.None;
                    }
                }
                else
                {
                    Contract.Assert(m_currentOperation == CurrentOperation.PendingWrite);
                    Contract.Assert(m_readingList.Count != 0);
                    UnsafeMergePendingReaders();
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

        private void UnsafeMergePendingReaders()
        {
            while (m_pendingReaderList.Count != 0)
            {
                var first = m_pendingReaderList.First;
                m_pendingReaderList.Remove(first);
                m_readingList.AddLast(first);
                first.Value.TrySetResult(LockStatus.Activated);
            }

            m_currentOperation = CurrentOperation.Reading;
        }

        #endregion
    }
}
