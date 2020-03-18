using System;
using System.Collections.Generic;
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
        private volatile HashSet<Registration<TMessage>> m_handlers = new HashSet<Registration<TMessage>>(
            EqualityComparer<Registration<TMessage>>.Default);
        private readonly IAsyncLock m_lock = new MonitorLock();

        /// <summary>
        /// 订阅注册回调
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="registration"/>为空
        /// </exception>
        public async Task<IAsyncDisposable> RegisterCallbackAsync(
            Registration<TMessage> registration, CancellationToken cancellation = default)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            await using (await m_lock.LockAsync(cancellation))
            {
                if (m_handlers.Contains(registration))
                {
                    throw new InvalidOperationException("Don't register again !");
                }

                m_handlers = new HashSet<Registration<TMessage>>(m_handlers.Append(registration));
                return new CallbackDisposer<TMessage>(this, registration);
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
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            var handlers = m_handlers;

            if (handlers.Count == 0)
            {
                return MessagePublishResult.None;
            }

            return await handlers.InvokeHandlers(this, key, message);
        }

        async Task ICallbackDisposable<TMessage>.DisposeCallback(Registration<TMessage> registration)
        {
            await using (await m_lock.LockAsync())
            {
                var handlers = m_handlers;
                if (!handlers.Contains(registration))
                {
                    return;
                }

                handlers = new HashSet<Registration<TMessage>>(m_handlers);
                handlers.Remove(registration);
                m_handlers = handlers;
            }
        }
    }
}
