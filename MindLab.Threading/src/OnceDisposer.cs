using System;

namespace MindLab.Threading
{
    /// <summary>
    /// 一次性释放器
    /// </summary>
    public sealed class OnceDisposer<T> : IDisposable
    {
        private readonly Action<T> m_disposeFunc;
        private readonly T m_state;
        private readonly OnceFlag m_disposeFlag = new OnceFlag();

        /// <summary>
        /// 创建一次性释放器
        /// </summary>
        public OnceDisposer(Action<T> disposeFunc, T state)
        {
            m_disposeFunc = disposeFunc ?? throw new ArgumentNullException(nameof(disposeFunc));
            m_state = state;
        }

        /// <summary>
        /// 释放此对象
        /// </summary>
        ~OnceDisposer()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (!m_disposeFlag.TrySet())
            {
                return;
            }

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }

            m_disposeFunc(m_state);
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            Dispose(true);
        }
    }
}