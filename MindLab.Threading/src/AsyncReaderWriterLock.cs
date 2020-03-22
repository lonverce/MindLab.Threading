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
                node = m_currentState.WaitForWrite();
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

                m_currentState.OnReadingListBecomeEmpty();
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

                m_currentState.OnTheFirstWriterDequeue();
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
            public abstract LinkedListNode<LockWaiterEvent> WaitForWrite();

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

            public virtual void OnTheFirstWriterDequeue() { }

            public virtual void OnReadingListBecomeEmpty() { }
        }

        /// <summary>
        /// 表示空闲状态, 没有任何的读写请求
        /// </summary>
        private sealed class NoneState : AbstractState
        {
            /// <summary>
            /// 表示空闲状态, 没有任何的读写请求
            /// </summary>
            public NoneState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingReaderList.Count == 0);
                Contract.Assert(owner.m_pendingWriterList.Count == 0);
                Contract.Assert(owner.m_readingList.Count == 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                // 因为当前没有任何读写操作, 所以可以立刻让调用方获得读锁
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                var node = Owner.m_readingList.AddLast(waiter);
                Owner.m_currentState = new ReadingState(Owner);
                return node;
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWrite()
            {
                // 因为当前没有任何读写操作, 所以可以立刻让调用方获得写锁
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                waiter.SetResult(LockStatus.Activated);
                Owner.m_currentState = new WritingState(Owner);
                return node;
            }

            public override bool TryEnterRead(out LinkedListNode<LockWaiterEvent> node)
            {
                // 因为当前没有任何读写操作, 所以可以立刻让调用方获得读锁
                node = WaitForRead();
                return true;
            }

            public override bool TryEnterWrite(out LinkedListNode<LockWaiterEvent> node)
            {
                // 因为当前没有任何读写操作, 所以可以立刻让调用方获得写锁
                node = WaitForWrite();
                return true;
            }

            // 在此状态下, 这个方法不可能被触发
            public override void OnReadingListBecomeEmpty()
            {
                throw new InvalidOperationException("m_readingList should be empty.");
            }

            // 在此状态下, 这个方法不可能被触发
            public override void OnTheFirstWriterDequeue()
            {
                throw new InvalidOperationException("m_pendingWriterList should be empty.");
            }
        }

        /// <summary>
        /// 表示当前处于读锁状态, 并且没有等待中的写锁请求
        /// </summary>
        private sealed class ReadingState : AbstractState
        {
            /// <summary>
            /// 表示当前处于读锁状态, 并且没有等待中的写锁请求
            /// </summary>
            public ReadingState(AsyncReaderWriterLock owner) : base(owner)
            {
                Contract.Assert(owner.m_pendingReaderList.Count == 0);
                Contract.Assert(owner.m_pendingWriterList.Count == 0);
                Contract.Assert(owner.m_readingList.Count != 0);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForRead()
            {
                // 因为当前已获得读锁, 而且没有等待中的写锁请求,
                // 所以可以让调用方立刻获得读锁
                var waiter = new LockWaiterEvent();
                waiter.SetResult(LockStatus.Activated);
                return Owner.m_readingList.AddLast(waiter);
            }

            public override LinkedListNode<LockWaiterEvent> WaitForWrite()
            {
                // 因为当前已获得读锁, 如果有新的写锁请求,
                // 则导致状态转移到 PendingWrite
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                Owner.m_currentState = new PendingWriteState(Owner);
                return node;
            }

            public override bool TryEnterRead(out LinkedListNode<LockWaiterEvent> node)
            {
                // 因为当前已获得读锁, 而且没有等待中的写锁请求,
                // 所以可以让调用方立刻获得读锁
                node = WaitForRead();
                return true;
            }

            public override void OnReadingListBecomeEmpty()
            {
                // 因为没有等待中的写锁请求, 所以可以回到空闲状态
                Owner.m_currentState = new NoneState(Owner);
            }

            public override void OnTheFirstWriterDequeue()
            {
                throw new InvalidOperationException("m_pendingWriterList should be empty.");
            }
        }

        /// <summary>
        /// 表示当前处于写锁状态
        /// </summary>
        private sealed class WritingState : AbstractState
        {
            /// <summary>
            /// 表示当前处于写锁状态
            /// </summary>
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

            public override LinkedListNode<LockWaiterEvent> WaitForWrite()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                return node;
            }

            public override void OnTheFirstWriterDequeue()
            {
                if (Owner.m_pendingWriterList.Any())
                {
                    // 如果存在下一个写锁请求, 则触发下一个写锁
                    Owner.m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
                }
                else if (Owner.m_pendingReaderList.Count > 0)
                {
                    // 如果没有下一个写锁请求, 而是存在多个读锁请求, 
                    // 则触发所有读锁, 然后状态转移
                    UnsafeActivatePendingReaders();
                }
                else
                {
                    // 如果啥请求都没有, 则回到空闲状态
                    Owner.m_currentState = new NoneState(Owner);
                }
            }

            public override void OnReadingListBecomeEmpty()
            {
                throw new InvalidOperationException("m_readingList should be empty.");
            }

            /// <summary>
            /// 触发所有等待中的读锁请求
            /// </summary>
            private void UnsafeActivatePendingReaders()
            {
                var tmp = Owner.m_readingList;
                Owner.m_readingList = Owner.m_pendingReaderList;
                Owner.m_pendingReaderList = tmp;
                Owner.m_currentState = new ReadingState(Owner);

                foreach (var pendingWaiter in Owner.m_readingList.ToArray())
                {
                    pendingWaiter.TrySetResult(LockStatus.Activated);
                }
            }
        }

        /// <summary>
        /// 表示当前处于读锁状态, 但存在等待中的写锁请求
        /// </summary>
        private sealed class PendingWriteState : AbstractState
        {
            /// <summary>
            /// 表示当前处于读锁状态, 但存在等待中的写锁请求
            /// </summary>
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

            public override LinkedListNode<LockWaiterEvent> WaitForWrite()
            {
                var waiter = new LockWaiterEvent();
                var node = Owner.m_pendingWriterList.AddLast(waiter);
                return node;
            }

            public override void OnReadingListBecomeEmpty()
            {
                // 当所有读锁都退出后, 立刻触发第一个写锁请求
                Owner.m_currentState = new WritingState(Owner);
                Owner.m_pendingWriterList.First.Value.TrySetResult(LockStatus.Activated);
            }

            public override void OnTheFirstWriterDequeue()
            {
                if (Owner.m_pendingWriterList.Any())
                {
                    return;
                }
                var evtList = new List<LockWaiterEvent>();

                // 由于所有等待中的写锁请求都被取消了,
                // 所以等待中的读锁请求可以立刻被合并到运行中的读锁
                while (Owner.m_pendingReaderList.Count != 0)
                {
                    var first = Owner.m_pendingReaderList.First;
                    Owner.m_pendingReaderList.Remove(first);
                    Owner.m_readingList.AddLast(first);
                    evtList.Add(first.Value);
                }

                Owner.m_currentState = new ReadingState(Owner);

                foreach(var evt in evtList)
                {
                    evt.TrySetResult(LockStatus.Activated);
                }
            }
        } 
        #endregion
    }
}
