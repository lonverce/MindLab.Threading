using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Internals;

namespace MindLab.Threading
{
    /// <summary>提供基于CAS无锁实现的异步互斥锁</summary>
    /// <seealso cref="IAsyncLock"/>
    public class CasLock : IAsyncLock, ILockDisposable
    {
        #region Fields

        private readonly LinkedList<TaskCompletionSource<LockStatus>> m_subscriberList = new LinkedList<TaskCompletionSource<LockStatus>>();
        private volatile int m_status;
        private const int STA_FREE = 0, STA_BLOCKING = 1;
        private static bool IsSingleProcessor { get; } = Environment.ProcessorCount == 1;
        #endregion

        #region Private Methods

        private async Task EnterLockAsync(CancellationToken cancellation)
        {
            const int YIELD_THRESHOLD = 10;
            const int SLEEP_0_EVERY_HOW_MANY_TIMES = 5;
            const int SLEEP_1_EVERY_HOW_MANY_TIMES = 20;

            int count = 0;

            do
            {
                cancellation.ThrowIfCancellationRequested();
                if (Interlocked.CompareExchange(ref m_status, STA_BLOCKING, STA_FREE) != STA_FREE)
                {
                    if (count > YIELD_THRESHOLD || IsSingleProcessor)
                    {
                        int yieldsSoFar = (count >= YIELD_THRESHOLD ? count - YIELD_THRESHOLD : count);

                        if ((yieldsSoFar % SLEEP_1_EVERY_HOW_MANY_TIMES) == (SLEEP_1_EVERY_HOW_MANY_TIMES - 1))
                        {
                            await Task.Delay(1, cancellation);
                        }
                        else if ((yieldsSoFar % SLEEP_0_EVERY_HOW_MANY_TIMES) == (SLEEP_0_EVERY_HOW_MANY_TIMES - 1))
                        {
                            await Task.Delay(0, cancellation);
                        }
                        else
                        {
                            await Task.Yield();
                        }
                    }

                    ++count;
                }
                else
                {
                    break;
                }
            } while (true);
        }

        private bool TryEnterLock()
        {
            return Interlocked.CompareExchange(ref m_status, STA_BLOCKING, STA_FREE) == STA_FREE;
        }

        private void ExitLock()
        {
            m_status = STA_FREE;
        }

        async Task ILockDisposable.InternalUnlockAsync()
        {
            TaskCompletionSource<LockStatus> next = null;
            await EnterLockAsync(CancellationToken.None);

            try
            {
                m_subscriberList.RemoveFirst();

                if (m_subscriberList.Any())
                {
                    next = m_subscriberList.First.Value;
                }
            }
            finally
            {
                ExitLock();
            }

            next?.TrySetResult(LockStatus.Activated);
        }

        private async Task<IAsyncDisposable> InternalLock(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            var completion = new TaskCompletionSource<LockStatus>();
            var isFirst = false;

            await EnterLockAsync(cancellation);

            try
            {
                cancellation.ThrowIfCancellationRequested();
                m_subscriberList.AddLast(completion);
                if (m_subscriberList.Count == 1)
                {
                    isFirst = true;
                }
            }
            finally
            {
                ExitLock();
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

                await EnterLockAsync(CancellationToken.None);
                try
                {
                    var activateNext = m_subscriberList.First.Value == completion;
                    m_subscriberList.Remove(completion);
                    if (activateNext)
                    {
                        next = m_subscriberList.First.Value;
                    }
                }
                finally
                {
                    ExitLock();
                }

                next?.TrySetResult(LockStatus.Activated);
                throw new OperationCanceledException(cancellation);
            }
        }

        private bool InternalTryLock(out IAsyncDisposable lockDisposer)
        {
            lockDisposer = null;
            if (!TryEnterLock())
            {
                return false;
            }

            try
            {
                if (m_subscriberList.Any())
                {
                    return false;
                }

                var completion = new TaskCompletionSource<LockStatus>();
                completion.SetResult(LockStatus.Activated);
                m_subscriberList.AddFirst(completion);
                lockDisposer = new LockDisposer(this);
            }
            finally
            {
                ExitLock();
            }

            return true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IAsyncDisposable> LockAsync(CancellationToken cancellation = default)
        {
            if (TryLock(out var disposer))
            {
                return disposer;
            }

            return await InternalLock(cancellation);
        }

        /// <summary>
        /// 尝试进入临界区
        /// </summary>
        /// <param name="lockDisposer"></param>
        /// <returns></returns>
        public bool TryLock(out IAsyncDisposable lockDisposer)
        {
            return InternalTryLock(out lockDisposer);
        } 
        #endregion
    }
}
