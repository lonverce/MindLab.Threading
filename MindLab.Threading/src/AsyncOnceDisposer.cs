using System;
using System.Threading.Tasks;

namespace MindLab.Threading
{
    /// <summary>
    /// 一次性释放器
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public sealed class AsyncOnceDisposer<T> : IAsyncDisposable
    {
        private readonly Func<T, Task> m_disposeFunc;
        private readonly T m_state;
        private readonly OnceFlag m_disposeFlag = new OnceFlag();

        /// <summary>
        /// 创建一次性释放器
        /// </summary>
        /// <param name="disposeFunc"></param>
        /// <param name="state"></param>
        public AsyncOnceDisposer(Func<T, Task> disposeFunc, T state)
        {
            m_disposeFunc = disposeFunc ?? throw new ArgumentNullException(nameof(disposeFunc));
            m_state = state;
        }

        /// <summary>
        /// 释放此对象
        /// </summary>
        ~AsyncOnceDisposer()
        {
            DisposeAsync(false).Wait();
        }

        private async Task DisposeAsync(bool disposing)
        {
            if (!m_disposeFlag.TrySet())
            {
                return;
            }

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }

            await m_disposeFunc(m_state);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.</summary>
        /// <returns>A task that represents the asynchronous dispose operation.</returns>
        public async ValueTask DisposeAsync()
        {
            await DisposeAsync(true);
        }
    }
}