using System;
using System.Threading.Tasks;
using MindLab.Threading;

namespace MindLab.Messaging.Internals
{
    internal class CallbackDisposer<TMessage> : IAsyncDisposable
    {
        private readonly Registration<TMessage> m_registration;
        private readonly WeakReference<ICallbackDisposable<TMessage>> m_router;
        private readonly OnceFlag m_flag = new OnceFlag();

        public CallbackDisposer(ICallbackDisposable<TMessage> router, Registration<TMessage> registration)
        {
            m_registration = registration;
            m_router = new WeakReference<ICallbackDisposable<TMessage>>(router);
        }

        public async ValueTask DisposeAsync()
        {
            if (!m_flag.TrySet())
            {
                return;
            }

            if (!m_router.TryGetTarget(out var router))
            {
                return;
            }

            await router.DisposeCallback(m_registration);
        }
    }
}