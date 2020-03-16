using System.Threading;

namespace MindLab.Threading
{
    /// <summary>线程安全的一次性标记变量</summary>
    /// <example>
    /// <code>
    /// <![CDATA[
    /// class SomeObject : IDisposable
    /// {
    ///     private readonly OnceFlag m_disposeFlag = new OnceFlag();
    ///
    ///     public void Dispose()
    ///     {
    ///         if(!m_disposeFlag.TrySet())
    ///         {
    ///             return; // this object had been disposed, we don't need to do it again.
    ///         }
    /// 
    ///         // disposing code ...
    ///     }
    /// }
    /// ]]>
    /// </code>
    /// </example>
    public class OnceFlag
    {
        private int m_flag = FALSE;
        private const int TRUE = 1, FALSE = 0;

        /// <summary>
        /// 获取指示此标记是否已经被设置
        /// </summary>
        public bool IsSet => m_flag == TRUE;

        /// <summary>
        /// 尝试设置此标记, 若此标记未被设置, 则此操作将设置此标记, 并返回<value>true</value>; 否则直接返回<value>false</value>
        /// </summary>
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