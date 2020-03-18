using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MindLab.Messaging.Internals
{
    internal static class DelegateHelper
    {
        public static async Task<MessagePublishResult> InvokeHandlers<TMessage>(
            this IReadOnlyCollection<Registration<TMessage>> handlers,
            IMessageRouter<TMessage> router,
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
                var subscribers = handlers
                    .GroupBy(registration => registration.Handler)
                    .Select(grouping =>
                    {
                        var func = grouping.Key;
                        var bindings = grouping.Select(registration => registration.RegisterKey)
                            .ToArray();
                        var msgArgs = new MessageArgs<TMessage>(
                            router, key, 
                            new ReadOnlyCollection<string>(bindings), 
                            message);

                        return Task.Run(() => func(msgArgs));
                    })
                    .ToArray();

                result.ReceiverCount = (uint)subscribers.Length;
                await Task.WhenAll(subscribers);
            }
            catch (Exception e)
            {
                result.Exception = e;
            }

            return result;
        }

        public static async Task<MessagePublishResult> InvokeHandlersDirectly<TMessage>(
            this IReadOnlyCollection<Registration<TMessage>> handlers,
            IMessageRouter<TMessage> router,
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
            var bindings = new[]{key};

            try
            {
                var subscribers = handlers
                    .Select(reg =>
                    {
                        var func = reg.Handler;
                        var msgArgs = new MessageArgs<TMessage>(
                            router, key,
                            new ReadOnlyCollection<string>(bindings),
                            message);

                        return Task.Run(() => func(msgArgs));
                    })
                    .ToArray();

                await Task.WhenAll(subscribers);
            }
            catch (Exception e)
            {
                result.Exception = e;
            }

            return result;
        }
    }
}
