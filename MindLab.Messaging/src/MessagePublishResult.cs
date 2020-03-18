using System;

namespace MindLab.Messaging
{
    /// <summary>
    /// 消息发布结果
    /// </summary>
    public struct MessagePublishResult
    {
        /// <summary>
        /// 接收到此消息的订阅者数量
        /// </summary>
        public uint ReceiverCount;

        /// <summary>
        /// 每个订阅者接收到此消息时产生的异常，如果没有异常，则为null
        /// </summary>
        public Exception Exception;

        /// <summary>
        /// 表示无异常且无消费的发布结果
        /// </summary>
        public static MessagePublishResult None { get; } = new MessagePublishResult(){ReceiverCount = 0, Exception = null};
    }
}