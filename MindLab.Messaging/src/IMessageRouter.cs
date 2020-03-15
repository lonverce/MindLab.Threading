using System;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Messaging
{
    /// <summary>
    /// 消息路由器
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    public interface IMessageRouter<out TMessage>
    {
        /// <summary>
        /// 订阅注册指定<paramref name="key"/>关联的回调处理
        /// </summary>
        /// <param name="key"></param>
        /// <param name="messageHandler"></param>
        /// <param name="cancellation"></param>
        /// <returns>通过此对象取消订阅</returns>
        /// <exception cref="ArgumentNullException">
        ///     <paramref name="messageHandler"/>为空 或 <paramref name="key"/>为空
        /// </exception>
        Task<IAsyncDisposable> RegisterCallbackAsync(string key, 
            AsyncMessageHandler<TMessage> messageHandler, 
            CancellationToken cancellation = default);
    }
    
    /// <summary>
    /// 消息处理委托
    /// </summary>
    /// <typeparam name="TMessage">消息类型</typeparam>
    /// <param name="key">路由key</param>
    /// <param name="message">消息体</param>
    public delegate Task AsyncMessageHandler<in TMessage>(string key, TMessage message);
}