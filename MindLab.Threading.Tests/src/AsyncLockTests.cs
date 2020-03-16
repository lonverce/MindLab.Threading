﻿using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;

namespace MindLab.Threading.Tests
{
    [TestClass]
    public class AsyncLockTests
    {
        [TestMethod]
        public async Task LockAsync_LockTwice_FirstOkButSecondBlocked()
        {
            var locker = new AsyncLock();
            await locker.LockAsync();

            Assert.IsFalse(locker.TryLock(out _));
        }

        [TestMethod]
        public async Task LockAsync_LockTwiceAndDisposeFirst_FirstOkAndThenSecondOk()
        {
            var locker = new AsyncLock();
            var disposer = await locker.LockAsync();
            var l2Task = locker.LockAsync();
            disposer.Dispose();
            Assert.IsTrue(l2Task.Wait(1000));
        }

        [TestMethod]
        public async Task LockAsync_LockAgainWithCancel_OperationCancelled()
        {
            var locker = new AsyncLock();
            using (await locker.LockAsync())
            {
                using var tokenSrc = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(async () => await locker.LockAsync(tokenSrc.Token));
            }
        }

        [TestMethod]
        public async Task LockAsync_SafeInMultiThreads()
        {
            int value = 0;
            var locker = new AsyncLock();

            async Task IncreaseAsync()
            {
                for (var i = 0; i < 10; i++)
                {
                    using (await locker.LockAsync())
                    {
                        value++;
                    } 
                }
            }

            await Task.WhenAll(Enumerable.Repeat((Func<Task>) IncreaseAsync, 10).Select(func => func()).ToArray());
            Assert.AreEqual(100, value);
        }
    }
}