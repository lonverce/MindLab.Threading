using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Telerik.JustMock;

namespace MindLab.Messaging.Tests
{
    [TestFixture]
    public class BroadcastMessageRouterTests
    {
        [Test]
        public async Task PublishMessageAfterRegister_CallbackSucceed()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>(); 
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = string.Empty;
            var message = 15;

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .MustBeCalled();
            await router.RegisterCallbackAsync(new Registration<int>(string.Empty, cb), CancellationToken.None);

            // Act
            await router.PublishMessageAsync(key, message);

            // Assert
            Mock.Assert(cb);
        }

        [Test]
        public async Task PublishMessageAfterRegister_OneReceiver()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var key = string.Empty;
            var message = 15;
            await router.RegisterCallbackAsync(
                new Registration<int>(string.Empty, args => Task.CompletedTask), 
                CancellationToken.None);

            // Act
            var result = await router.PublishMessageAsync(key, message);

            // Assert
            Assert.AreEqual(1, result.ReceiverCount);
            Assert.IsNull(result.Exception);
        }

        [Test]
        public async Task PublishMessageAfterRegister_NoException()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var key = string.Empty;
            var message = 15;
            await router.RegisterCallbackAsync(
                new Registration<int>(string.Empty, args => Task.CompletedTask),
                CancellationToken.None);

            // Act
            var result = await router.PublishMessageAsync(key, message);

            // Assert
            Assert.IsNull(result.Exception);
        }

        [Test]
        public async Task PublishMessageAfterRegister_HandlerThrowException_ResultContainException()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var key = string.Empty;
            var message = 15;
            await router.RegisterCallbackAsync(
                new Registration<int>(string.Empty, args => Task.FromException(new Exception())),
                CancellationToken.None);

            // Act
            var result = await router.PublishMessageAsync(key, message);

            // Assert
            Assert.IsNotNull(result.Exception);
        }

        [Test]
        public async Task RegisterSameActionTwice_CallbackOnlyOnce()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>(); 
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = string.Empty;
            var message = 15;

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(1);

            // Act
            await router.RegisterCallbackAsync(new Registration<int>("1", cb), CancellationToken.None);
            await router.RegisterCallbackAsync(new Registration<int>("2", cb), CancellationToken.None); // register twice
            await router.PublishMessageAsync(key, message);

            // Assert
            Mock.Assert(cb);
        }

        [Test]
        public async Task RegisterSameActionAndKeyTwice_ExceptionThrown()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = string.Empty;

            // Act
            await router.RegisterCallbackAsync(new Registration<int>(key, cb), CancellationToken.None);

            // Assert
            Assert.CatchAsync<InvalidOperationException>(() =>
                router.RegisterCallbackAsync(new Registration<int>(key, cb), CancellationToken.None));
        }

        [Test]
        public async Task PublishMessage_FirstHandlerThrow_SecondHandlerInvoked()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = string.Empty;
            var message = 15;

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(1);
            await router.RegisterCallbackAsync(new Registration<int>("1", args => Task.FromException(new Exception())), 
                CancellationToken.None);
            await router.RegisterCallbackAsync(new Registration<int>("2", cb), CancellationToken.None); // register twice

            // Act
            await router.PublishMessageAsync(key, message);

            // Assert
            Mock.Assert(cb);
        }

        [Test]
        public async Task PublishAfterDispose_NoCallback()
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(1);

            await using (await router.RegisterCallbackAsync(new Registration<int>(string.Empty, cb), CancellationToken.None))
            {
                // Act
                await router.PublishMessageAsync(string.Empty, 1);
            }

            // Act
            await router.PublishMessageAsync(string.Empty, 1);

            // Assert
            Mock.Assert(cb);
        }
    }
}