using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Internals;

namespace MindLab.Threading
{
    /// <summary>提供基于<see cref="Monitor"/>实现的异步互斥锁</summary>
    /// <seealso cref="IAsyncLock"/>
    public class MonitorLock : IAsyncLock, ILockDisposable
    {
        private readonly object m_locker = new object();
        private readonly LinkedList<TaskCompletionSource<LockStatus>> m_subscribers = new LinkedList<TaskCompletionSource<LockStatus>>();

        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IAsyncDisposable> LockAsync(CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<LockStatus>();
            var isFirst = false;

            lock (m_locker)
            {
                cancellation.ThrowIfCancellationRequested();
                m_subscribers.AddLast(completion);
                if (m_subscribers.Count == 1)
                {
                    isFirst = true;
                }
            }

            if (isFirst)
            {
                completion.SetResult(LockStatus.Activated);
            }
            
            await using (cancellation.Register(() => completion.TrySetResult(LockStatus.Cancelled)))
            {
                if (cancellation.IsCancellationRequested)
                {
                    completion.TrySetResult(LockStatus.Cancelled);
                }

                var status = await completion.Task;
                if (status == LockStatus.Activated)
                {
                    return new LockDisposer(this);
                }

                TaskCompletionSource<LockStatus> next = null;
                lock (m_locker)
                {
                    var activateNext = m_subscribers.First.Value == completion;
                    m_subscribers.Remove(completion);
                    if (activateNext)
                    {
                        next = m_subscribers.First.Value;
                    }
                }

                next?.TrySetResult(LockStatus.Activated);
                throw new OperationCanceledException(cancellation);
            }
        }

        /// <summary>
        /// 尝试进入临界区
        /// </summary>
        /// <param name="lockDisposer"></param>
        /// <returns></returns>
        public bool TryLock(out IAsyncDisposable lockDisposer)
        {
            lockDisposer = null;
            if (!Monitor.TryEnter(m_locker))
            {
                return false;
            }

            try
            {
                if (m_subscribers.Any())
                {
                    return false;
                }

                var completion = new TaskCompletionSource<LockStatus>();
                completion.SetResult(LockStatus.Activated);
                m_subscribers.AddFirst(completion);
                lockDisposer = new LockDisposer(this);
            }
            finally
            {
                Monitor.Exit(m_locker);
            }

            return true;
        }

        Task ILockDisposable.InternalUnlockAsync()
        {
            TaskCompletionSource<LockStatus> next = null;
            lock (m_locker)
            {
                m_subscribers.RemoveFirst();

                if (m_subscribers.Any())
                {
                    next = m_subscribers.First.Value;
                }
            }

            next?.TrySetResult(LockStatus.Activated);
            return Task.CompletedTask;
        }
    }
}
