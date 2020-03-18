using System;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Messaging
{
    /// <summary>
    /// 消息路由器
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    public interface IMessageRouter<TMessage>
    {
        /// <summary>
        /// 订阅注册回调
        /// </summary>
        /// <param name="registration"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="registration"/>为空
        /// </exception>
        Task<IAsyncDisposable> RegisterCallbackAsync(Registration<TMessage> registration,
            CancellationToken cancellation = default);
    }

    /// <summary>
    /// 消息处理委托
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    public delegate Task AsyncMessageHandler<TMessage>(MessageArgs<TMessage> messageArgs);
}