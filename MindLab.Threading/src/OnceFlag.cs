using System.Threading;

namespace MindLab.Threading
{
    public class OnceFlag
    {
        private int m_flag = FALSE;
        private const int TRUE = 1, FALSE = 0;

        public bool IsSet => m_flag == TRUE;

        public bool TrySet()
        {
            if (IsSet)
            {
                return false;
            }

            return Interlocked.CompareExchange(ref m_flag, TRUE, FALSE) == TRUE;
        }
    }
}