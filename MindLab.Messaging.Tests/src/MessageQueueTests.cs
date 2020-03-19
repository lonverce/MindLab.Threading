using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Telerik.JustMock;

namespace MindLab.Messaging.Tests
{
    [TestFixture]
    public class MessageQueueTests
    {
        [Test]
        public void MessageQueue_CreateWithDefaultParameters()
        {
            var mq = new MessageQueue<int>();
            Assert.AreEqual(MessageQueue<int>.DEFAULT_BEHAVIOUR, mq.FullBehaviour);
            Assert.AreEqual(MessageQueue<int>.DEFAULT_CAPACITY, mq.Capacity);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(-10)]
        public void MessageQueue_CreateWithWrongCapacity_ExceptionThrown(int capacity)
        {
            Assert.Catch<ArgumentOutOfRangeException>(() =>
                new MessageQueue<int>(capacity, QueueFullBehaviour.BlockPublisher));
        }

        [Test]
        public void BindAsync_WithNullKey_ExceptionThrown()
        {
            var mq = new MessageQueue<int>();
            Assert.CatchAsync<ArgumentNullException>(() =>
                mq.BindAsync(null, 
                    Mock.Create<IMessageRouter<int>>(), CancellationToken.None));
        }

        [Test]
        public void BindAsync_WithNullRouter_ExceptionThrown()
        {
            var mq = new MessageQueue<int>();
            Assert.CatchAsync<ArgumentNullException>(() =>
                mq.BindAsync("123",null, CancellationToken.None));
        }

        [Test]
        public async Task BindAsync_WithCorrectParameters_RouterMethodCalled()
        {
            var mq = new MessageQueue<int>();
            var router = Mock.Create<IMessageRouter<int>>();
            var disposer = Mock.Create<IAsyncDisposable>();
            var key = "123";

            Mock.Arrange(() => router.RegisterCallbackAsync(Arg.IsAny<Registration<int>>(),
                    Arg.IsAny<CancellationToken>()))
                .Returns((Registration<int> reg, CancellationToken token) =>
                {
                    Assert.AreEqual(key, reg.RegisterKey);
                    Assert.IsNotNull(reg.Handler);
                    return Task.FromResult(disposer);
                })
                .OccursOnce();

            Mock.Arrange(() => disposer.DisposeAsync())
                .Returns(async () => await Task.CompletedTask)
                .OccursOnce();

            await using (await mq.BindAsync(key, router, CancellationToken.None))
            {
                Mock.Assert(router);
            }

            Mock.Assert(disposer);
        }

        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        [TestCase(50)]
        public async Task BlockingPublisher(int capacity)
        {
            var mq = new MessageQueue<int>(capacity, QueueFullBehaviour.BlockPublisher);
            var router = Mock.Create<IMessageRouter<int>>();
            AsyncMessageHandler<int> handler = null;
            var keys = new[] {string.Empty};

            Mock.Arrange(() => router.RegisterCallbackAsync(
                    Arg.IsAny<Registration<int>>(), 
                    Arg.IsAny<CancellationToken>()))
                .DoInstead((Registration<int> reg, CancellationToken token) =>
                {
                    handler = reg.Handler;
                })
                .Returns(Task.FromResult(Mock.Create<IAsyncDisposable>()))
                .OccursOnce();

            await mq.BindAsync(string.Empty, router);
            Mock.Assert(router);

            var watch = new Stopwatch();
            watch.Start();
            for (int i = 0; i < capacity; i++)
            {
                await handler(new MessageArgs<int>(router, string.Empty, keys, 1));
            }
            watch.Stop();

            var task = handler(new MessageArgs<int>(router, string.Empty, keys, 1));
            Assert.IsFalse(task.Wait(watch.Elapsed+TimeSpan.FromMilliseconds(500)));
            await mq.TakeMessageAsync();
            Assert.IsTrue(task.Wait(100));
        }

        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        [TestCase(50)]
        public async Task AbandonNewest(int capacity)
        {
            var mq = new MessageQueue<int>(capacity, QueueFullBehaviour.AbandonNew);
            var router = Mock.Create<IMessageRouter<int>>();
            AsyncMessageHandler<int> handler = null;
            var keys = new[] { string.Empty };

            Mock.Arrange(() => router.RegisterCallbackAsync(
                    Arg.IsAny<Registration<int>>(),
                    Arg.IsAny<CancellationToken>()))
                .DoInstead((Registration<int> reg, CancellationToken token) =>
                {
                    handler = reg.Handler;
                })
                .Returns(Task.FromResult(Mock.Create<IAsyncDisposable>()))
                .OccursOnce();

            await mq.BindAsync(string.Empty, router);
            Mock.Assert(router);

            for (int i = 0; i < capacity; i++)
            {
                await handler(new MessageArgs<int>(router, string.Empty, keys, i));
            }

            // should be abandoned
            await handler(new MessageArgs<int>(router, string.Empty, keys, capacity));

            for (int i = 0; i < capacity; i++)
            {
                var msg = await mq.TakeMessageAsync(CancellationToken.None);
                Assert.AreEqual(i, msg.Payload);
            }

            Assert.IsFalse(mq.TryTakeMessage(out _));
        }

        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(10)]
        [TestCase(50)]
        public async Task RemoveOldest(int capacity)
        {
            var mq = new MessageQueue<int>(capacity, QueueFullBehaviour.RemoveOldest);
            var router = Mock.Create<IMessageRouter<int>>();
            AsyncMessageHandler<int> handler = null;
            var keys = new[] { string.Empty };

            Mock.Arrange(() => router.RegisterCallbackAsync(
                    Arg.IsAny<Registration<int>>(),
                    Arg.IsAny<CancellationToken>()))
                .DoInstead((Registration<int> reg, CancellationToken token) =>
                {
                    handler = reg.Handler;
                })
                .Returns(Task.FromResult(Mock.Create<IAsyncDisposable>()))
                .OccursOnce();

            await mq.BindAsync(string.Empty, router);
            Mock.Assert(router);

            for (int i = 0; i < capacity; i++)
            {
                await handler(new MessageArgs<int>(router, string.Empty, keys, i));
            }

            // should be added in
            await handler(new MessageArgs<int>(router, string.Empty, keys, capacity));

            for (int i = 0; i < capacity; i++)
            {
                var msg = await mq.TakeMessageAsync(CancellationToken.None);
                Assert.AreEqual(i+1, msg.Payload);
            }

            Assert.IsFalse(mq.TryTakeMessage(out _));
        }
    }
}
