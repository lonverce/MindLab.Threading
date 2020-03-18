using System;
using System.Collections.Concurrent;
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
        
        private readonly IAsyncLock m_lock = new MonitorLock();
        private readonly ConcurrentDictionary<string, Registration<TMessage>[]> m_subscribers 
            = new ConcurrentDictionary<string, Registration<TMessage>[]>(StringComparer.CurrentCultureIgnoreCase);

        #endregion

        #region Public Methods

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
            
            if (!m_subscribers.TryGetValue(key, out var handlers))
            {
                return MessagePublishResult.None;
            }

            return await handlers.InvokeHandlers(this, key, message);
        }

        /// <summary>
        /// 订阅注册回调
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="registration"/>为空
        /// </exception>
        public async Task<IAsyncDisposable> RegisterCallbackAsync(Registration<TMessage> registration,
            CancellationToken cancellation = default)
        {
            if (registration == null)
            {
                throw new ArgumentNullException(nameof(registration));
            }

            await using (await m_lock.LockAsync(cancellation))
            {
                m_subscribers.AddOrUpdate(registration.RegisterKey, key => new[]{registration},
                    (key, registrations) => registrations.Append(registration).ToArray());
            }
            
            return new CallbackDisposer<TMessage>(this, registration);
        }

        #endregion

        #region Private

        async Task ICallbackDisposable<TMessage>.DisposeCallback(Registration<TMessage> registration)
        {
            await using (await m_lock.LockAsync())
            {
                if (!m_subscribers.TryGetValue(registration.RegisterKey, out var existedHandlers))
                {
                    return;
                }

                var list = existedHandlers.ToList();
                if (!list.Remove(registration))
                {
                    return;
                }

                if (list.Count == 0)
                {
                    m_subscribers.TryRemove(registration.RegisterKey, out _);
                    return;
                }

                m_subscribers[registration.RegisterKey] = list.ToArray();
            }
        }

        #endregion
    }
}