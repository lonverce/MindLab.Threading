namespace MindLab.Messaging
{
    /// <summary>
    /// 消息参数
    /// </summary>
    /// <typeparam name="TMessage"></typeparam>
    public struct MessageArgs<TMessage>
    {
        /// <summary>
        /// 指示消息从何处发出
        /// </summary>
        public IMessageRouter<TMessage> FromRouter;

        /// <summary>
        /// 指示消息发布时使用的Key
        /// </summary>
        public string PublishKey;

        /// <summary>
        /// 指示队列绑定到<seealso cref="FromRouter"/>时所使用的Key
        /// </summary>
        public string BindingKey;

        /// <summary>
        /// 消息体
        /// </summary>
        public TMessage Payload;
    }
}