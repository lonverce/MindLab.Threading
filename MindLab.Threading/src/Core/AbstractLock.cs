using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Internals;

namespace MindLab.Threading.Core
{
    /// <summary>
    /// 抽象锁实现
    /// </summary>
    public abstract class AbstractLock : IAsyncLock, ILockDisposable
    {
        #region Fields

        private readonly LinkedList<TaskCompletionSource<LockStatus>> m_subscriberList = new LinkedList<TaskCompletionSource<LockStatus>>();

        #endregion

        #region Abstract Methods

        /// <summary>
        /// 进入对象数据保护锁
        /// </summary>
        protected abstract Task EnterLockAsync(CancellationToken cancellation);

        /// <summary>
        /// 尝试进入对象数据保护锁
        /// </summary>
        protected abstract bool TryEnterLock();

        /// <summary>
        /// 退出对象数据保护锁
        /// </summary>
        protected abstract void ExitLock();

        #endregion

        #region Private Methods

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

#if NETSTANDARD2_1
            await
#endif
                using (cancellation.Register(() => completion.TrySetResult(LockStatus.Cancelled)))
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
