using System.Threading.Tasks;

namespace MindLab.Messaging.Internals
{
    internal interface ICallbackDisposable<out TMessage>
    {
        Task DisposeCallback(string key, AsyncMessageHandler<TMessage> messageHandler);
    }
}
