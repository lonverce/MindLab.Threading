using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MindLab.Messaging.Internals
{
    internal static class DelegateHelper
    {
        public static async Task<MessagePublishResult> InvokeHandlers<TMessage>(
            this IReadOnlyCollection<AsyncMessageHandler<TMessage>> handlers,
            string key,
            TMessage message
            )
        {
            if (handlers.Count == 0)
            {
                return MessagePublishResult.None;
            }

            var result = new MessagePublishResult
            {
                ReceiverCount = (uint)handlers.Count
            };

            try
            {
                await Task.WhenAll(handlers.Distinct().Select(handler => handler(key, message)).ToArray());
            }
            catch (AggregateException e)
            {
                result.Exception = e;
            }

            return result;
        }
    }
}
