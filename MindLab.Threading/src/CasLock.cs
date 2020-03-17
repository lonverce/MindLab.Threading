using System;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading.Core;

namespace MindLab.Threading
{
    /// <summary>提供基于CAS无锁实现的异步互斥锁</summary>
    /// <seealso cref="IAsyncLock"/>
    public sealed class CasLock : AbstractLock
    {
        #region Fields

        private volatile int m_status;
        private const int STA_FREE = 0, STA_BLOCKING = 1;
        private static bool IsSingleProcessor { get; } = Environment.ProcessorCount == 1;

        #endregion

        #region Override Methods

        /// <summary>
        /// 进入对象数据保护锁
        /// </summary>
        protected override async Task EnterLockAsync(CancellationToken cancellation)
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

        /// <summary>
        /// 尝试进入对象数据保护锁
        /// </summary>
        protected override bool TryEnterLock()
        {
            return Interlocked.CompareExchange(ref m_status, STA_BLOCKING, STA_FREE) == STA_FREE;
        }

        /// <summary>
        /// 退出对象数据保护锁
        /// </summary>
        protected override void ExitLock()
        {
            m_status = STA_FREE;
        }

        #endregion
    }
}
