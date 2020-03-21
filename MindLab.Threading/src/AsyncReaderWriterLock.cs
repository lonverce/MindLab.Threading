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
        #region Fields
        private LinkedList<LockWaiterEvent> m_readingList = new LinkedList<LockWaiterEvent>();
        private LinkedList<LockWaiterEvent> m_pendingReaderList = new LinkedList<LockWaiterEvent>();
        private readonly LinkedList<LockWaiterEvent> m_pendingWriterList = new LinkedList<LockWaiterEvent>();
        private readonly object m_lock = new object();
        private AbstractState m_currentState;
        #endregion

        #region Constructor

        /// <summary>
        /// 初始化异步读写锁
        /// </summary>
        public AsyncReaderWriterLock()
        {
            m_currentState = new NoneState(this);
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 进入读锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象释放读锁</returns>
        public async Task<IDisposable> WaitForReadAsync(CancellationToken cancellation = default)
        {
            LinkedListNode<LockWaiterEvent> node;
            lock(m_lock)
            {
                node = m_currentState.WaitForRead();
            }
            var waiter = node.Value;

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

                if (status != LockStatus.Cancelled)
                {
                    return disposer;
                }

                disposer.Dispose();
                throw new OperationCanceledException(cancellation);
            }
        }

        /// <summary>
        /// 进入写锁
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IDisposable> WaitForWriteAsync(CancellationToken cancellation = default)
        {
            LinkedListNode<LockWaiterEvent> node;
            lock (m_lock)
            {
                node = m_currentState.WaitForWriteAsync();
            }

            var waiter = node.Value;
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

                if (status != LockStatus.Cancelled)
                {
                    return disposer;
                }

                disposer.Dispose();
                throw new OperationCanceledException(cancellation);
            }
        }

        /// <summary>
        /// 尝试进入读锁
        /// </summary>
        public bool TryEnterRead(out IDisposable disposer)
        {
            disposer = null;
            
            lock (m_lock)
            {
                if (!m_currentState.TryEnterRead(out var node))
                {
                    return false;
                }
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
            lock (m_lock)
            {
                if (!m_currentState.TryEnterWrite(out var node))
                {
                    return false;
                }

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

                m_currentState.ActiveNextOnReaderExited();
            }
        }

        private void InternalExitWriteLock(LinkedListNode<LockWaiterEvent> node)
        {
            lock (m_lock)
            {
                var shouldActiveNext = m_pendingWriterList.First == node;

                m_pendingWriterList.Remove(node);

                if (!shouldActiveNext)
                {
                    return;
                }

                m_currentState.ActiveNextOnWriterExited();
            }
        }

        #endregion

        #region States
        private abstract class AbstractState
        {
            protected readonly AsyncReaderWriterLock Owner;

            protected AbstractState(AsyncReaderWriterLock owner)
            {
                Owner = owner;
            }

            public abstract LinkedListNode<LockWaiterEvent> WaitForRead();
            public abstract LinkedListNode<LockWaiterEvent> WaitForWriteAsync();

            public virtual bool TryEnterWrite(out LinkedListNode<LockWaiterEvent> node)
            {
                node = null;
                return false;
            }

            public virtual bool TryEnterRead(out LinkedListNode<LockWaiterEvent> node)
            {
                node = null;
                return false;
            }

            public virtual void ActiveNextOnWriterExited() { }

            public virtual void ActiveNextOnReaderExited() { }
        }

        private sealed class NoneState : AbstractState
        {
            public NoneState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingReaderList.Count == 0);
                Contract.Assert(owner.m_pendingWriterList.Count == 0);
                Contract.Assert(owner.m_readingList.Count == 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                var node = Owner.m_readingList.AddLast(waiter);
                Owner.m_currentState = new ReadingState(Owner);
                return node;
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWriteAsync()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                waiter.SetResult(LockStatus.Activated);
                Owner.m_currentState = new WritingState(Owner);
                return node;
            }

            public override bool TryEnterRead(out LinkedListNode<LockWaiterEvent> node)
            {
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                node = Owner.m_readingList.AddLast(waiter);
                Owner.m_currentState = new ReadingState(Owner);
                return true;
            }

            public override bool TryEnterWrite(out LinkedListNode<LockWaiterEvent> node)
            {
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                node = Owner.m_pendingWriterList.AddLast(waiter);
                Owner.m_currentState = new WritingState(Owner);
                return true;
            }

            public override void ActiveNextOnReaderExited()
            {
                throw new InvalidOperationException("m_readingList should be empty.");
            }

            public override void ActiveNextOnWriterExited()
            {
                throw new InvalidOperationException("m_pendingWriterList should be empty.");
            }
        }

        private sealed class ReadingState : AbstractState
        {
            public ReadingState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingReaderList.Count == 0);
                Contract.Assert(owner.m_pendingWriterList.Count == 0);
                Contract.Assert(owner.m_readingList.Count != 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                return Owner.m_readingList.AddLast(waiter);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWriteAsync()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                Owner.m_currentState = new PendingWriteState(Owner);
                return node;
            }

            public override bool TryEnterRead(out LinkedListNode<LockWaiterEvent> node)
            {
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                node = Owner.m_readingList.AddLast(waiter);
                return true;
            }

            public override void ActiveNextOnReaderExited()
            {
                Owner.m_currentState = new NoneState(Owner);
            }

            public override void ActiveNextOnWriterExited()
            {
                throw new InvalidOperationException("m_pendingWriterList should be empty.");
            }
        }

        private sealed class WritingState : AbstractState
        {
            public WritingState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingWriterList.Count != 0);
                Contract.Assert(owner.m_readingList.Count == 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                var waiter = new LockWaiterEvent();
                return Owner.m_pendingReaderList.AddLast(waiter);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWriteAsync()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                return node;
            }

            public override void ActiveNextOnWriterExited()
            {
                if (Owner.m_pendingWriterList.Any())
                {
                    Owner.m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
                }
                else if (Owner.m_pendingReaderList.Count > 0)
                {
                    UnsafeActivatePendingReaders();
                    Owner.m_currentState = new ReadingState(Owner);
                }
                else
                {
                    Owner.m_currentState = new NoneState(Owner);
                }
            }

            public override void ActiveNextOnReaderExited()
            {
                throw new InvalidOperationException("m_readingList should be empty.");
            }

            private void UnsafeActivatePendingReaders()
            {
                var tmp = Owner.m_readingList;
                Owner.m_readingList = Owner.m_pendingReaderList;
                Owner.m_pendingReaderList = tmp;
                foreach (var pendingWaiter in Owner.m_readingList)
                {
                    pendingWaiter.TrySetResult(LockStatus.Activated);
                }
            }
        }

        private sealed class PendingWriteState : AbstractState
        {
            public PendingWriteState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingWriterList.Count != 0);
                Contract.Assert(owner.m_readingList.Count != 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                var waiter = new LockWaiterEvent();
                return Owner.m_pendingReaderList.AddLast(waiter);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWriteAsync()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                return node;
            }

            public override void ActiveNextOnReaderExited()
            {
                Owner.m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
                Owner.m_currentState = new WritingState(Owner);
            }

            public override void ActiveNextOnWriterExited()
            {
                while (Owner.m_pendingReaderList.Count != 0)
                {
                    var first = Owner.m_pendingReaderList.First;
                    Owner.m_pendingReaderList.Remove(first);
                    Owner.m_readingList.AddLast(first);
                    first.Value.TrySetResult(LockStatus.Activated);
                }

                Owner.m_currentState = new ReadingState(Owner);
            }
        } 
        #endregion
    }
}
