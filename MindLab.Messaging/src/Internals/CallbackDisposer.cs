using System;
using System.Threading.Tasks;
using MindLab.Threading;

namespace MindLab.Messaging.Internals
{
    internal class CallbackDisposer<TMessage> : IAsyncDisposable
    {
        private readonly WeakReference<ICallbackDisposable<TMessage>> m_router;
        private readonly string m_key;
        private readonly AsyncMessageHandler<TMessage> m_callback;
        private readonly OnceFlag m_flag = new OnceFlag();

        public CallbackDisposer(ICallbackDisposable<TMessage> router, string key, AsyncMessageHandler<TMessage> callback)
        {
            m_router = new WeakReference<ICallbackDisposable<TMessage>>(router);
            m_key = key;
            m_callback = callback;
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

            await router.DisposeCallback(m_key, m_callback);
        }
    }
}