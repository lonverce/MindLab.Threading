using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MindLab.Threading.Tests
{
    [TestFixture]
    public class AsyncLockTests
    {
        private IAsyncLock CreateLock(Type lockerType)
        {
            return Activator.CreateInstance(lockerType) as IAsyncLock;
        }

        [Test, TestCase(typeof(CasLock)), TestCase(typeof(MonitorLock))]
        [TestCase(typeof(SemaphoreLock))]
        public async Task LockAsync_LockTwice_FirstOkButSecondBlocked(Type lockerType)
        {
            var locker = CreateLock(lockerType);
            await locker.LockAsync();

            Assert.IsFalse(locker.TryLock(out _));
        }

        [Test, TestCase(typeof(CasLock)), TestCase(typeof(MonitorLock))]
        [TestCase(typeof(SemaphoreLock))]
        public async Task LockAsync_LockTwiceAndDisposeFirst_FirstOkAndThenSecondOk(Type lockerType)
        {
            var locker = CreateLock(lockerType);
            var disposer = await locker.LockAsync();
            var l2Task = locker.LockAsync();
            disposer.Dispose();
            Assert.IsTrue(l2Task.Wait(1000));
        }

        [Test, TestCase(typeof(CasLock)), TestCase(typeof(MonitorLock))]
        [TestCase(typeof(SemaphoreLock))]
        public async Task LockAsync_LockAgainWithCancel_OperationCancelled(Type lockerType)
        {
            var locker = CreateLock(lockerType);
            using (await locker.LockAsync())
            {
                using var tokenSrc = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                
                Assert.CatchAsync<OperationCanceledException>(async () => await locker.LockAsync(tokenSrc.Token));
            }
        }

        [Test, TestCase(typeof(CasLock)), TestCase(typeof(MonitorLock))]
        [TestCase(typeof(SemaphoreLock))]
        public async Task LockAsync_SafeInMultiThreads(Type lockerType)
        {
            int value = 0;
            var locker = CreateLock(lockerType);

            async Task IncreaseAsync()
            {
                for (var i = 0; i < 1000; i++)
                {
                    using (await locker.LockAsync())
                    {
                        value++;
                    } 
                }
            }

            await Task.WhenAll(Enumerable.Repeat((Func<Task>) IncreaseAsync, 20).Select(func => Task.Run(func)).ToArray());
            Assert.AreEqual(20000, value);
        }
    }
}