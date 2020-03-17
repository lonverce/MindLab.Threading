namespace MindLab.Messaging
{
    /// <summary>
    /// 定义当消息队列满载时的处理行为
    /// </summary>
    public enum QueueFullBehaviour
    {
        /// <summary>
        /// 阻塞消息发送方, 直至队列中的消息被消费
        /// </summary>
        BlockPublisher,

        /// <summary>
        /// 丢弃当前收到的最新消息
        /// </summary>
        AbandonNew,

        /// <summary>
        /// 移除队列中最旧的消息
        /// </summary>
        RemoveOldest,
    }
}