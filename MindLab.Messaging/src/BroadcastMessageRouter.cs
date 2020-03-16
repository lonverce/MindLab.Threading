using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Messaging.Internals;
using MindLab.Threading;

namespace MindLab.Messaging
{
    /// <summary>
    /// 广播式消息路由器
    /// </summary>
    public class BroadcastMessageRouter<TMessage> : IMessageRouter<TMessage>,
        IMessagePublisher<TMessage>, ICallbackDisposable<TMessage>
    {
        private volatile AsyncMessageHandler<TMessage>[] m_handlers = Array.Empty<AsyncMessageHandler<TMessage>>();
        private readonly IAsyncLock m_lock = new MonitorLock();

        /// <summary>
        /// 订阅注册指定<paramref name="key"/>关联的回调处理
        /// </summary>
        /// <param name="key"></param>
        /// <param name="messageHandler"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="messageHandler"/>为空 或 <paramref name="key"/>为空
        /// </exception>
        public async Task<IAsyncDisposable> RegisterCallbackAsync(
            string key, AsyncMessageHandler<TMessage> messageHandler, CancellationToken cancellation)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (messageHandler == null)
            {
                throw new ArgumentNullException(nameof(messageHandler));
            }

            if (messageHandler.GetInvocationList().Length > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(messageHandler), "Can not be broadcast delegate");
            }

            using (await m_lock.LockAsync(cancellation))
            {
                var handlers = m_handlers;
                m_handlers = handlers.Append(messageHandler).ToArray();
                return new CallbackDisposer<TMessage>(this, key, messageHandler);
            }
        }

        /// <summary>
        /// 向使用指定路由键<paramref name="key"/>注册的订阅者发布消息
        /// </summary>
        /// <param name="key">路由key</param>
        /// <param name="message">消息对象</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/>为空</exception>
        public async Task<MessagePublishResult> PublishMessageAsync(string key, TMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key));
            }

            var handlers = m_handlers;

            if (handlers.Length == 0)
            {
                return MessagePublishResult.None;
            }

            return await handlers.InvokeHandlers(key, message);
        }

        async Task ICallbackDisposable<TMessage>.DisposeCallback(string key, AsyncMessageHandler<TMessage> messageHandler)
        {
            using (await m_lock.LockAsync())
            {
                var handlers = m_handlers;

                var list = handlers.ToList();
                if (!list.Remove(messageHandler))
                {
                    return;
                }

                m_handlers = list.ToArray();
            }
        }
    }
}
