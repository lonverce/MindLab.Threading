// =============================================
// 版权归属 : RDAPP
// 文件名称 : LockDisposer.cs
// 文件作者 : 李垄华
// 创建日期 : 2020-03-16
// =============================================

using System;
using System.Diagnostics.Contracts;
using System.Threading.Tasks;

namespace MindLab.Threading.Internals
{
    internal interface ILockDisposable
    {
        void InternalUnlock();
    }

    internal class LockDisposer : IDisposable
    {
        private readonly ILockDisposable m_locker;
        private readonly OnceFlag m_flag = new OnceFlag();

        public LockDisposer(ILockDisposable locker)
        {
            m_locker = locker;
        }

        ~LockDisposer()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (!m_flag.TrySet())
            {
                return;
            }

            m_locker.InternalUnlock();

            if (disposing)
            {
                GC.SuppressFinalize(this);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }
    }
}