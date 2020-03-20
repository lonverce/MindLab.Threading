using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MindLab.Threading.Tests
{
    [TestFixture]
    public class AsyncReaderWriterLockTest
    {
        [Test]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public async Task WaitForReadAsync_MultipleCall_OK(int callTimes)
        {
            var locker = new AsyncReaderWriterLock();
            for (int i = 0; i < callTimes; i++)
            {
                await locker.WaitForReadAsync();
            }
        }

        [Test]
        public async Task WaitForWriteAsync_TheSecondCallWouldBlocked()
        {
            var locker = new AsyncReaderWriterLock();

            await using (await locker.WaitForWriteAsync())
            {
                using var tokenSrc = new CancellationTokenSource(1000);
                Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForWriteAsync(tokenSrc.Token));
            }
        }

        [Test]
        public async Task WaitForWriteAsync_TheSecondReaderWouldBlocked()
        {
            var locker = new AsyncReaderWriterLock();

            await using (await locker.WaitForWriteAsync())
            {
                using (var tokenSrc = new CancellationTokenSource(1000))
                {
                    Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForReadAsync(tokenSrc.Token));
                }
            }
        }

        [Test]
        public async Task WaitForReadAsync_TheSecondWriterWouldBlock()
        {
            var locker = new AsyncReaderWriterLock();

            await using (await locker.WaitForReadAsync())
            {
                using (var tokenSrc = new CancellationTokenSource(1000))
                {
                    Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForWriteAsync(tokenSrc.Token));
                }
            }
        }

        [Test]
        public async Task AfterWriterExit_AllReadersWereActivated()
        {
            var locker = new AsyncReaderWriterLock();
            var writer = await locker.WaitForWriteAsync();
            var readers = Task.WhenAll(
                locker.WaitForReadAsync(), 
                locker.WaitForReadAsync());
            await Task.Delay(1000);

            Assert.IsFalse(readers.IsCompleted);

            await writer.DisposeAsync();

            await readers;
        }

        [Test]
        public async Task AfterAllReadersExit_FirstWriterWasActivated()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            var reader2 = await locker.WaitForReadAsync();

            var writer = locker.WaitForWriteAsync();

            await reader1.DisposeAsync();
            await reader2.DisposeAsync();

            await writer;
        }

        [Test]
        public async Task Reader_Writer_Reader_LastReaderBlock()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            var writer = locker.WaitForWriteAsync();
            await Task.Delay(100);
            using var tokenSrc = new CancellationTokenSource(1000);
            Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForReadAsync(tokenSrc.Token));
        }
    }
}
