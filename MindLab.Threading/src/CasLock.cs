using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
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
        private volatile IReadOnlyList<TaskCompletionSource<LockStatus>> m_subscribers 
            = Array.Empty<TaskCompletionSource<LockStatus>>();
        #endregion

        #region Private Methods

        private delegate IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateDelegate(
            IReadOnlyList<TaskCompletionSource<LockStatus>> src, 
            object state);

        private IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateSubscribers(
            UpdateDelegate updateFunc,
            object state,
            CancellationToken cancellation = default)
        {
            IReadOnlyList<TaskCompletionSource<LockStatus>> currentSubscribers;
            IReadOnlyList<TaskCompletionSource<LockStatus>> prevSubscribers = m_subscribers;

            do
            {
                cancellation.ThrowIfCancellationRequested();
                currentSubscribers = prevSubscribers;

                // copy on write
                var nextVersionSubscribers = updateFunc(currentSubscribers, state);
                if (nextVersionSubscribers == null)
                {
                    return null;
                }

                prevSubscribers = Interlocked.CompareExchange(
                    ref m_subscribers,
                    nextVersionSubscribers,
                    currentSubscribers);

                Contract.Assert(prevSubscribers != null);
            } while (!Equals(prevSubscribers, currentSubscribers));

            return prevSubscribers;
        }

        private static IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateCancel(IReadOnlyList<TaskCompletionSource<LockStatus>> src,
            object state)
        {
            return src.Where(source => source != state).ToArray();
        }

        private static IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateLock(IReadOnlyList<TaskCompletionSource<LockStatus>> list,
            object state)
        {
            var src = (TaskCompletionSource<LockStatus>[])list;
            var dest = new TaskCompletionSource<LockStatus>[src.Length + 1];
            Array.Copy(src, 0, dest, 0, src.Length);
            dest[src.Length] = (TaskCompletionSource<LockStatus>)state;
            return dest;
        }

        private static IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateUnlock(IReadOnlyList<TaskCompletionSource<LockStatus>> sources,
            object state)
        {
            var src = (TaskCompletionSource<LockStatus>[])sources;
            var dest = new TaskCompletionSource<LockStatus>[src.Length - 1];
            Array.Copy(src, 1, dest, 0, dest.Length);
            return dest;
        }

        private static IReadOnlyList<TaskCompletionSource<LockStatus>> UpdateTryLock(IReadOnlyList<TaskCompletionSource<LockStatus>> src,
            object state)
        {
            return src.Any() ? null : (IReadOnlyList<TaskCompletionSource<LockStatus>>)state;
        }

        private void InternalCancelLock(TaskCompletionSource<LockStatus> completion)
        {
            Contract.Assert(completion.Task.IsCompleted && completion.Task.Result == LockStatus.Cancelled);
            var prevSubscribers = UpdateSubscribers(
                UpdateCancel, completion);
            
            if (prevSubscribers[0] == completion && prevSubscribers.Count > 1)
            {
                // 触发下一个锁
                prevSubscribers[1].TrySetResult(LockStatus.Activated);
            }
        }

        void ILockDisposable.InternalUnlock()
        {
            var prevSubscribers = UpdateSubscribers(UpdateUnlock, null);
            if (prevSubscribers.Count > 1)
            {
                // 触发下一个锁
                prevSubscribers[1].TrySetResult(LockStatus.Activated);
            }
        }

        private async Task<IDisposable> InternalLock(CancellationToken cancellation)
        {
            var completion = new TaskCompletionSource<LockStatus>();
            var prevSubscribers = UpdateSubscribers(UpdateLock, completion, cancellation);

            // 如果当前没有其他等候者, 则表示我们成功进入临界区; 否则, 表示我们进入等候区
            if (prevSubscribers.Count == 0)
            {
                completion.TrySetResult(LockStatus.Activated);
            }

            await using (cancellation.Register(CancelCompletion, completion, false))
            {
                if (cancellation.IsCancellationRequested)
                {
                    completion.TrySetResult(LockStatus.Cancelled);
                }

                var result = await completion.Task;
                if (result == LockStatus.Activated)
                {
                    return new LockDisposer(this);
                }

                InternalCancelLock(completion);
                throw new OperationCanceledException(cancellation);
            }
        }

        private static void CancelCompletion(object state)
        {
            ((TaskCompletionSource<LockStatus>)state).TrySetResult(LockStatus.Cancelled);
        }

        private bool InternalTryLock(out IDisposable lockDisposer)
        {
            var completion = new TaskCompletionSource<LockStatus>();
            completion.SetResult(LockStatus.Activated);
            var cps = new[] { completion };
            var prev = UpdateSubscribers(UpdateTryLock, cps);

            if (prev == null)
            {
                lockDisposer = null;
                return false;
            }

            lockDisposer = new LockDisposer(this);
            return true;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// 等待进入临界区
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns></returns>
        public async Task<IDisposable> LockAsync(CancellationToken cancellation = default)
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
        public bool TryLock(out IDisposable lockDisposer)
        {
            return InternalTryLock(out lockDisposer);
        } 
        #endregion
    }
}
