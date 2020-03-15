using System;
using System.Threading.Tasks;

namespace MindLab.Messaging
{
    /// <summary>
    /// 消息发布器
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public interface IMessagePublisher<in TMessage>
    {
        /// <summary>
        /// 向使用指定路由键<paramref name="key"/>注册的订阅者发布消息
        /// </summary>
        /// <param name="key">路由key</param>
        /// <param name="message">消息对象</param>
        /// <exception cref="ArgumentNullException"><paramref name="message"/>为空</exception>
        Task<MessagePublishResult> PublishMessageAsync(string key, TMessage message);
    }
}