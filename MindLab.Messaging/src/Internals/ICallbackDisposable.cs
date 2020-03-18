using System.Threading.Tasks;

namespace MindLab.Messaging.Internals
{
    internal interface ICallbackDisposable<TMessage>
    {
        Task DisposeCallback(Registration<TMessage> registration);
    }
}
