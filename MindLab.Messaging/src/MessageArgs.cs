using System;
using System.Collections.Generic;
using System.Linq;

namespace MindLab.Messaging
{
    /// <summary>
    /// 消息参数
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public class MessageArgs<TMessage>
    {
        /// <summary>
        /// 指示消息从何处发出
        /// </summary>
        public readonly IMessageRouter<TMessage> FromRouter;

        /// <summary>
        /// 指示消息发布时使用的Key
        /// </summary>
        public readonly string PublishKey;

        /// <summary>
        /// 指示队列绑定到<seealso cref="FromRouter"/>时所使用的Key
        /// </summary>
        public readonly IReadOnlyCollection<string> BindingKey;

        /// <summary>
        /// 消息体
        /// </summary>
        public readonly TMessage Payload;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="fromRouter"></param>
        /// <param name="publishKey"></param>
        /// <param name="bindingKey"></param>
        /// <param name="payload"></param>
        public MessageArgs(
            IMessageRouter<TMessage> fromRouter, 
            string publishKey, 
            IReadOnlyCollection<string> bindingKey, 
            TMessage payload)
        {
            FromRouter = fromRouter ?? throw new ArgumentNullException(nameof(fromRouter));
            PublishKey = publishKey ?? throw new ArgumentNullException(nameof(publishKey));
            BindingKey = bindingKey ?? throw new ArgumentNullException(nameof(bindingKey));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }
    }
}