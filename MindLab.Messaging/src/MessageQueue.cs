using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Messaging
{
    /// <summary>
    /// 异步消息队列
    /// </summary>
    /// <typeparam name="TMessage">消息结构</typeparam>
    public sealed class MessageQueue<TMessage>
    {
        #region Fields
        private readonly SemaphoreSlim m_chatMessageSemaphore, m_blankSemaphore;
        private readonly ConcurrentQueue<TMessage> m_chatMessages = new ConcurrentQueue<TMessage>();
        #endregion

        #region Constructor

        /// <summary>
        /// 初始化消息队列
        /// </summary>
        public MessageQueue()
        {
            m_chatMessageSemaphore = new SemaphoreSlim(0);
            m_blankSemaphore = null;
        }

        /// <summary>
        /// 初始化消息队列
        /// </summary>
        /// <param name="capacity">队列最大容量, 当队列满载时, 新消息的插入将导致旧消息被丢弃</param>
        public MessageQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "应该大于0");
            }

            m_chatMessageSemaphore = new SemaphoreSlim(0, capacity);
            m_blankSemaphore = new SemaphoreSlim(capacity, capacity);
        }

        #endregion

        #region Private Methods
        
        private Task EnqueueMessageWithCapacity(string key, TMessage msg)
        {
            if (msg == null)
            {
                return Task.CompletedTask;
            }

            // 尝试获取空位, 不成功时进入循环体
            while (!(m_blankSemaphore?.Wait(0) ?? true))
            {
                // 获取空位失败, 尝试移除队首元素
                TryTakeMessage(out _);
            }

            // 获取空位成功, 直接加入
            m_chatMessages.Enqueue(msg);
            m_chatMessageSemaphore.Release();
            return Task.CompletedTask;
        }

        private TMessage DequeueMessage()
        {
            if (!m_chatMessages.TryDequeue(out var msg))
            {
                throw new InvalidOperationException("Dequeue failed.");
            }
            m_blankSemaphore?.Release();
            return msg;
        }

        #endregion

        #region Methods

        /// <summary>
        /// 绑定此队列到消息路由器
        /// </summary>
        /// <param name="key"></param>
        /// <param name="messageRouter"></param>
        /// <param name="cancellation"></param>
        /// <returns>释放此对象以解除绑定</returns>
        /// <exception cref="ArgumentNullException"><paramref name="messageRouter"/>为空</exception>
        /// <remarks>每个队列对象可以同时绑定到多个消息路由器</remarks>
        public async Task<IAsyncDisposable> BindAsync(string key, IMessageRouter<TMessage> messageRouter, CancellationToken cancellation = default)
        {
            if (messageRouter == null)
            {
                throw new ArgumentNullException(nameof(messageRouter));
            }

            return await messageRouter.RegisterCallbackAsync(key, EnqueueMessageWithCapacity, cancellation);
        }

        /// <summary>
        /// 等待队列中的下一条消息
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<TMessage> TakeMessageAsync(CancellationToken token)
        {
            await m_chatMessageSemaphore.WaitAsync(token);
            return DequeueMessage();
        }

        /// <summary>
        /// 等待队列中的下一条消息，如果在限时内没有获取到消息，则返回默认结果<paramref name="defaultValue"/>
        /// </summary>
        /// <param name="timeout">等待时限</param>
        /// <param name="defaultValue">等待超时后返回的默认值</param>
        public async Task<TMessage> TakeMessageOrDefaultAsync(TimeSpan timeout, TMessage defaultValue)
        {
            if (await m_chatMessageSemaphore.WaitAsync(timeout))
            {
                return DequeueMessage();
            }

            return defaultValue;
        }

        /// <summary>
        /// 尝试获取队列中的消息, 如果队列中没有消息, 则立即返回false
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool TryTakeMessage(out TMessage message)
        {
            message = default;
            if (!m_chatMessageSemaphore.Wait(0))
            {
                return false;
            }

            message = DequeueMessage();
            return true;
        }

        #endregion
    }
}