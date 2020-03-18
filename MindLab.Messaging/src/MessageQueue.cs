using System;
using System.Threading;
using System.Threading.Tasks;
using MindLab.Threading;

namespace MindLab.Messaging
{
    /// <summary>
    /// 异步消息队列
    /// </summary>
    /// <typeparam name="TMessage">消息结构</typeparam>
    public sealed class MessageQueue<TMessage>
    {
        #region Fields
        private readonly AsyncBlockingCollection<MessageArgs<TMessage>> m_messageCollection;

        /// <summary>
        /// 队列默认容量
        /// </summary>
        public const int DEFAULT_CAPACITY = 8192;

        /// <summary>
        /// 队列默认行为
        /// </summary>
        public const QueueFullBehaviour DEFAULT_BEHAVIOUR = QueueFullBehaviour.BlockPublisher;

        #endregion

        #region Properties

        /// <summary>
        /// 获取队列最大容量
        /// </summary>
        public int Capacity => m_messageCollection.BoundaryCapacity;

        /// <summary>
        /// 获取队列中当前消息数量
        /// </summary>
        public int Count => m_messageCollection.Count;

        /// <summary>
        /// 获取当队列满载时的处理方式
        /// </summary>
        public QueueFullBehaviour FullBehaviour { get; }

        #endregion

        #region Constructor

        /// <summary>
        /// 初始化消息队列
        /// </summary>
        public MessageQueue()
            :this(DEFAULT_CAPACITY, DEFAULT_BEHAVIOUR)
        {
        }

        /// <summary>
        /// 初始化消息队列
        /// </summary>
        /// <param name="capacity">队列最大容量, 当队列满载时, 新消息的插入将导致旧消息被丢弃</param>
        /// <param name="behaviour"></param>
        public MessageQueue(int capacity, QueueFullBehaviour behaviour)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "应该大于0");
            }

            m_messageCollection = new AsyncBlockingCollection<MessageArgs<TMessage>>(capacity);
            FullBehaviour = behaviour;
        }

        #endregion

        #region Private Methods
        
        private async Task EnqueueMessageWithCapacity(MessageArgs<TMessage> msg)
        {
            if (m_messageCollection.TryAdd(msg) || FullBehaviour == QueueFullBehaviour.AbandonNew)
            {
                return;
            }

            if (FullBehaviour == QueueFullBehaviour.BlockPublisher)
            {
                await m_messageCollection.AddAsync(msg);
                return;
            }

#if DEBUG
            System.Diagnostics.Contracts.Contract.Assert(FullBehaviour == QueueFullBehaviour.RemoveOldest, 
                "FullBehaviour should be QueueFullBehaviour.RemoveOldest");            
#endif
            // 尝试获取空位, 不成功时进入循环体
            while (!(m_messageCollection.TryAdd(msg)))
            {
                // 获取空位失败, 尝试移除队首元素
                m_messageCollection.TryTake(out _);
            }
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

            return await messageRouter.RegisterCallbackAsync(
                new Registration<TMessage>(key, EnqueueMessageWithCapacity), 
                cancellation);
        }

        /// <summary>
        /// 等待队列中的下一条消息
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<MessageArgs<TMessage>> TakeMessageAsync(CancellationToken token)
        {
            return await m_messageCollection.TakeAsync(token);
        }

        /// <summary>
        /// 尝试获取队列中的消息, 如果队列中没有消息, 则立即返回false
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public bool TryTakeMessage(out MessageArgs<TMessage> message)
        {
            return m_messageCollection.TryTake(out message);
        }

        #endregion
    }
}