using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Telerik.JustMock;

namespace MindLab.Messaging.Tests
{
    [TestFixture]
    public class UnicastMessageRouterTests
    {
        [Test]
        [TestCase("1", "1", true)]
        [TestCase("1", "2", false)]
        public async Task PublishMessageAfterRegister_CallbackSucceed(string bindingKey, string publishKey, bool shouldBeReceived)
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = bindingKey;
            var message = 15;

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(shouldBeReceived ? 1:0);

            await router.RegisterCallbackAsync(new Registration<int>(publishKey, cb), CancellationToken.None);

            // Act
            await router.PublishMessageAsync(key, message);

            // Assert
            Mock.Assert(cb);
        }

        [Test]
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public async Task PublishMessageAfterRegister_RegisterMultipleTimes_ReceiverCountAsMuchAsRegisterTimes(int regTimes)
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
            var key = string.Empty;
            var message = 15;
            var randomValue = new Random(regTimes);

            for (int i = 0; i < regTimes; i++)
            {
                var val = randomValue.Next();
                await router.RegisterCallbackAsync(
                    new Registration<int>(string.Empty, args =>
                    {
                        val++;
                        return Task.CompletedTask;
                    }),
                    CancellationToken.None); 
            }

            // Act
            var result = await router.PublishMessageAsync(key, message);

            // Assert
            Assert.AreEqual(regTimes, result.ReceiverCount);
        }

        [Test]
        public async Task PublishMessageAfterRegister_NoException()
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
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
            var router = new UnicastMessageRouter<int>();
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
        public async Task RegisterSameActionAndKeyTwice_ExceptionThrown()
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
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
            var router = new UnicastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();
            var key = string.Empty;
            var message = 15;

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(1);

            await router.RegisterCallbackAsync(new Registration<int>(key, args => Task.FromException(new Exception())),
                CancellationToken.None);
            await router.RegisterCallbackAsync(new Registration<int>(key, cb), CancellationToken.None); // register twice

            // Act
            await router.PublishMessageAsync(key, message);

            // Assert
            Mock.Assert(cb);
        }

        [Test]
        public async Task PublishAfterDispose_NoCallback()
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
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

        [Test]
        [TestCase("123", 789)]
        [TestCase("", 789)]
        public async Task PublishMessage_MessageArgCorrect(string bindKey, int message)
        {
            // Arrange
            var router = new BroadcastMessageRouter<int>();

            Task Callback(MessageArgs<int> args)
            {
                Assert.IsTrue(args.BindingKey.Single() == bindKey);
                Assert.AreEqual(bindKey, args.PublishKey);
                Assert.AreSame(router, args.FromRouter);
                Assert.AreEqual(message, args.Payload);

                return Task.CompletedTask;
            }

            await router.RegisterCallbackAsync(new Registration<int>(bindKey, Callback));
            await router.PublishMessageAsync(bindKey, message);
        }

        [Test]
        [TestCase(10)]
        [TestCase(100)]
        [TestCase(1000)]
        public async Task PublishMessage_BindInMultipleThreads_TotalCallback(int times)
        {
            // Arrange
            var router = new UnicastMessageRouter<int>();
            var cb = Mock.Create<AsyncMessageHandler<int>>();

            Mock.Arrange(() => cb(Arg.IsAny<MessageArgs<int>>()))
                .Returns(Task.CompletedTask)
                .Occurs(times);

            await router.RegisterCallbackAsync(new Registration<int>(string.Empty, cb));
            var cancellationTokenSrc = new CancellationTokenSource();

            var bindTask = Task.Run(async () =>
            {
                var reg = new Registration<int>(string.Empty, args => Task.CompletedTask);

                while (!cancellationTokenSrc.IsCancellationRequested)
                {
                    await using (await router.RegisterCallbackAsync(reg, CancellationToken.None))
                    {
                        await Task.Delay(1, CancellationToken.None);
                    }
                }
            }, CancellationToken.None);

            for (int i = 0; i < times; i++)
            {
                await router.PublishMessageAsync(string.Empty, 1);
            }

            cancellationTokenSrc.Cancel();
            await bindTask;

            Mock.Assert(cb);
        }
    }
}
