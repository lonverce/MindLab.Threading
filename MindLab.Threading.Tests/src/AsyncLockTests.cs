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
    }
}