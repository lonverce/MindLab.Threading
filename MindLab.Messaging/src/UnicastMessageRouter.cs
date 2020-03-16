using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading;

namespace MindLab.Messaging
{
    using Internals;

    /// <summary>
    /// 单播消息路由器
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    public sealed class UnicastMessageRouter<TMessage> : IMessageRouter<TMessage>, 
        IMessagePublisher<TMessage>, ICallbackDisposable<TMessage>
    {
        #region Fields
        
        private readonly CasLock m_lock = new CasLock();
        private readonly Dictionary<string, AsyncMessageHandler<TMessage>[]> m_subscribers 
            = new Dictionary<string, AsyncMessageHandler<TMessage>[]>(StringComparer.CurrentCultureIgnoreCase);

        #endregion

        #region Public Methods

        /// <summary>
        /// 向使用指定<paramref name="key"/>注册的订阅者发布消息
        /// </summary>
        /// <param name="key">路由key</param>
        /// <param name="message">消息对象</param>
        /// <returns>发布的消息数量</returns>
        /// <exception cref="ArgumentNullException"><paramref name="message"/>为空 或 <paramref name="key"/>为空</exception>
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
            
            if (!m_subscribers.TryGetValue(key, out var handlers))
            {
                return MessagePublishResult.None;
            }

            return await handlers.InvokeHandlers(key, message);
        }

        /// <summary>
        /// 订阅注册指定<paramref name="key"/>关联的回调处理
        /// </summary>
        /// <param name="key"></param>
        /// <param name="messageHandler"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException"><paramref name="messageHandler"/>为空 或 <paramref name="key"/>为空</exception>
        public async Task<IAsyncDisposable> RegisterCallbackAsync(string key, 
            AsyncMessageHandler<TMessage> messageHandler,
            CancellationToken cancellation = default)
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
                if (!m_subscribers.TryGetValue(key, out var handlers))
                {
                    m_subscribers.Add(key, new[] { messageHandler });
                }
                else
                {
                    // copy on write
                    m_subscribers[key] = handlers.Append(messageHandler).ToArray();
                }
            }
            
            return new CallbackDisposer<TMessage>(this, key, messageHandler);
        }

        #endregion

        #region Private

        async Task ICallbackDisposable<TMessage>.DisposeCallback(string key, AsyncMessageHandler<TMessage> messageHandler)
        {
            using (await m_lock.LockAsync())
            {
                if (!m_subscribers.TryGetValue(key, out var existedHandlers))
                {
                    return;
                }

                var list = existedHandlers.ToList();
                if (!list.Remove(messageHandler))
                {
                    return;
                }

                if (list.Count == 0)
                {
                    m_subscribers.Remove(key);
                    return;
                }

                m_subscribers[key] = list.ToArray();
            }
        }

        #endregion
    }
}