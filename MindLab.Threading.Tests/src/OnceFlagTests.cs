using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MindLab.Threading.Tests
{
    [TestClass()]
    public class OnceFlagTests
    {
        [TestMethod()]
        public void IsSet_CreateAndGet_NotSetYet()
        {
            var flag = new OnceFlag();
            Assert.IsFalse(flag.IsSet);
        }

        [TestMethod()]
        public void TrySet_CreateAndSet_Ok()
        {
            var flag = new OnceFlag();
            Assert.IsTrue(flag.TrySet());
        }

        [TestMethod()]
        public void TrySet_SetAgain_Failed()
        {
            var flag = new OnceFlag();
            flag.TrySet();
            Assert.IsFalse(flag.TrySet());
        }

        [TestMethod()]
        public void TrySet_InParallel_OnlyOneSetOk()
        {
            var flag = new OnceFlag();
            var cnt = 0;

            Parallel.For(0, 10, (i) =>
            {
                if (flag.TrySet())
                {
                    Interlocked.Increment(ref cnt);
                }
            });

            Assert.AreEqual(1, cnt);
        }
    }
}