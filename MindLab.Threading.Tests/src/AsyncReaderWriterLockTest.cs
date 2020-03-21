using System;
using System.Collections.Generic;
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
            var readers = new List<IDisposable>();

            for (int i = 0; i < callTimes; i++)
            {
                readers.Add(await locker.WaitForReadAsync());
                Assert.IsTrue(locker.TryEnterRead(out var reader));
                readers.Add(reader);
            }
        }

        [Test]
        public async Task WaitForWriteAsync_TheSecondCallWouldBlocked()
        {
            var locker = new AsyncReaderWriterLock();

            using (await locker.WaitForWriteAsync())
            {
                Assert.IsFalse(locker.TryEnterWrite(out _));
                using var tokenSrc = new CancellationTokenSource(1000);
                Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForWriteAsync(tokenSrc.Token));
            }
        }

        [Test]
        public async Task WaitForWriteAsync_TheSecondReaderWouldBlocked()
        {
            var locker = new AsyncReaderWriterLock();

            using (await locker.WaitForWriteAsync())
            {
                Assert.IsFalse(locker.TryEnterRead(out _));
                using var tokenSrc = new CancellationTokenSource(1000);
                Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForReadAsync(tokenSrc.Token));
            }
        }

        [Test]
        public async Task WaitForReadAsync_TheSecondWriterWouldBlock()
        {
            var locker = new AsyncReaderWriterLock();

            using (await locker.WaitForReadAsync())
            {
                Assert.IsFalse(locker.TryEnterWrite(out _));
                using var tokenSrc = new CancellationTokenSource(1000);
                Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForWriteAsync(tokenSrc.Token));
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

            writer.Dispose();

            Assert.IsTrue(locker.TryEnterRead(out _));
            await readers;
        }

        [Test]
        public async Task AfterAllReadersExit_FirstWriterWasActivated()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            var reader2 = await locker.WaitForReadAsync();

            var writer = locker.WaitForWriteAsync();

            reader1.Dispose();
            reader2.Dispose();

            await writer;
        }

        [Test]
        public async Task Reader_Writer_Reader_LastReaderBlock()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            var writer = locker.WaitForWriteAsync();
            await Task.Delay(100);

            Assert.IsFalse(locker.TryEnterRead(out _));
            using var tokenSrc = new CancellationTokenSource(1000);
            Assert.CatchAsync<OperationCanceledException>(() => locker.WaitForReadAsync(tokenSrc.Token));
        }

        [Test]
        public async Task PendingReadersWillBeMerged_AfterPendingWriterExit()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            using var tokenSrc = new CancellationTokenSource(1000);
            var pendingWriter = locker.WaitForWriteAsync(tokenSrc.Token);
            await Task.Delay(100, CancellationToken.None);
            var pendingReader = locker.WaitForReadAsync(CancellationToken.None);
            await Task.Delay(100, CancellationToken.None);

            Assert.IsFalse(pendingWriter.IsCompleted);
            Assert.IsFalse(pendingReader.IsCompleted);

            tokenSrc.Cancel();
            await Task.Delay(100, CancellationToken.None);
            Assert.IsTrue(pendingWriter.IsCompleted);
            Assert.IsTrue(pendingReader.IsCompletedSuccessfully);
        }

        [Test]
        public async Task PendingReadersWillNotBeMerged_UntilAllPendingWritersRemoved()
        {
            var locker = new AsyncReaderWriterLock();
            var reader1 = await locker.WaitForReadAsync();
            using var tokenSrc = new CancellationTokenSource(1000);
            var pendingWriter = locker.WaitForWriteAsync(tokenSrc.Token);
            var pendingWriter2 = locker.WaitForWriteAsync(CancellationToken.None);

            await Task.Delay(100, CancellationToken.None);
            var pendingReader = locker.WaitForReadAsync(CancellationToken.None);
            await Task.Delay(100, CancellationToken.None);

            Assert.IsFalse(pendingWriter.IsCompleted);
            Assert.IsFalse(pendingWriter2.IsCompleted);
            Assert.IsFalse(pendingReader.IsCompleted);

            tokenSrc.Cancel();
            await Task.Delay(100, CancellationToken.None);
            Assert.IsTrue(pendingWriter.IsCompleted);
            Assert.IsFalse(pendingWriter2.IsCompleted);
            Assert.IsFalse(pendingReader.IsCompleted);
        }
    }
}
