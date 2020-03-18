using System;

namespace MindLab.Messaging
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public class Registration<TMessage>
    {
        /// <summary>
        /// 键值比较
        /// </summary>
        public static readonly StringComparison RegisterKeyComparison = StringComparison.CurrentCultureIgnoreCase;

        /// <summary>
        /// 创建注册数据
        /// </summary>
        /// <param name="registerKey">注册键</param>
        /// <param name="handler">回调</param>
        public Registration(string registerKey, AsyncMessageHandler<TMessage> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (handler.GetInvocationList().Length > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(handler), "Can not be broadcast delegate");
            }

            RegisterKey = registerKey ?? throw new ArgumentNullException(nameof(registerKey));
            Handler = handler;
        }

        /// <summary>
        /// 注册键
        /// </summary>
        public string RegisterKey { get; }

        /// <summary>
        /// 回调方法
        /// </summary>
        public AsyncMessageHandler<TMessage> Handler { get; }

        /// <summary>Determines whether the specified object is equal to the current object.</summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>
        /// <see langword="true" /> if the specified object  is equal to the current object; otherwise, <see langword="false" />.</returns>
        public override bool Equals(object obj)
        {
            if (obj is Registration<TMessage> registration)
            {
                return Equals(registration);
            }

            return false;
        }

        /// <summary>Serves as the default hash function.</summary>
        /// <returns>A hash code for the current object.</returns>
        public override int GetHashCode()
        {
#if NETSTANDARD2_1
            return HashCode.Combine(RegisterKey, Handler);
#else
            unchecked
            {
                return ((RegisterKey != null ? StringComparer.CurrentCultureIgnoreCase.GetHashCode(RegisterKey) : 0) * 397) ^ (Handler != null ? Handler.GetHashCode() : 0);
            }
#endif
        }

        /// <summary>
        /// 比较当前注册实例是否等价于另一个注册实例
        /// </summary>
        /// <param name="registration"></param>
        /// <returns></returns>
        public bool Equals(Registration<TMessage> registration)
        {
            if (registration == null)
            {
                return false;
            }

            return string.Equals(RegisterKey, registration.RegisterKey, RegisterKeyComparison)
                   && Handler == registration.Handler;
        }
    }
}
