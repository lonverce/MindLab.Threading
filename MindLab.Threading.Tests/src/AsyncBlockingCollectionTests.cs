using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace MindLab.Threading.Tests
{
    [TestFixture]
    public class AsyncBlockingCollectionTests
    {
        [Test]
        public void AsyncBlockingCollection_WithNullCollection_ExceptionThrown()
        {
            Assert.Catch<ArgumentNullException>(() => new AsyncBlockingCollection<int>(null));
        }
        
        [Test]
        public void AsyncBlockingCollectionTest_WithDefaultCollection_InitiallyZeroCount()
        {
            var collection = new AsyncBlockingCollection<int>();
            Assert.AreEqual(0, collection.Count);
        }

        [Test]
        public void AsyncBlockingCollectionTest_WithNoBoundarySetting_MaxCapacity()
        {
            var collection = new AsyncBlockingCollection<int>();
            Assert.AreEqual(int.MaxValue, collection.BoundaryCapacity);
        }

        [Test]
        public void AsyncBlockingCollectionTest_WithNegativeBoundary_ExceptionThrown()
        {
            Assert.Catch<ArgumentOutOfRangeException>(() => new AsyncBlockingCollection<int>(-1));
        }

        [Test]
        public void AsyncBlockingCollectionTest_WithZeroBoundary_ExceptionThrown()
        {
            Assert.Catch<ArgumentOutOfRangeException>(() => new AsyncBlockingCollection<int>(0));
        }

        [Test]
        public void AsyncBlockingCollectionTest_WithBoundaryLessThanExistingCount_ExceptionThrown()
        {
            var queue = new ConcurrentQueue<int>(new []{1,2,3});
            Assert.Catch<ArgumentOutOfRangeException>(() => new AsyncBlockingCollection<int>(queue, queue.Count-1));
        }

        [Test]
        public async Task AddAsync_IfNotFull_OK()
        {
            var collection = new AsyncBlockingCollection<int>(1);
            await collection.AddAsync(1);
            Assert.Pass();
        }

        [Test]
        public void AddAsync_IfFull_Blocked()
        {
            var queue = new ConcurrentQueue<int>(new[] { 1, 2, 3 });
            var collection = new AsyncBlockingCollection<int>(queue, queue.Count);
            using var tokenSrc = new CancellationTokenSource(1000);
            Assert.CatchAsync<OperationCanceledException>(async () => await collection.AddAsync(0, tokenSrc.Token));
            Assert.Pass();
        }

        [Test]
        public void TryAdd_IfNotFull_OK()
        {
            var collection = new AsyncBlockingCollection<int>(1);
            Assert.IsTrue(collection.TryAdd(1));
        }

        [Test]
        public void TryAdd_IfFull_Failed()
        {
            var queue = new ConcurrentQueue<int>(new[] { 1, 2, 3 });
            var collection = new AsyncBlockingCollection<int>(queue, queue.Count);
            Assert.IsFalse(collection.TryAdd(1));
        }

        [Test]
        public async Task TakeAsync_InCaseOfAdequate_OK()
        {
            var queue = new ConcurrentQueue<int>(new[] { 1, 2, 3 });
            var collection = new AsyncBlockingCollection<int>(queue, queue.Count);
            await collection.TakeAsync();
            Assert.Pass();
        }

        [Test]
        public void TakeAsync_InCaseOfEmpty_Timeout()
        {
            var collection = new AsyncBlockingCollection<int>();
            using var tokenSrc = new CancellationTokenSource(1000);
            Assert.CatchAsync<OperationCanceledException>(async () => await collection.TakeAsync(tokenSrc.Token));
            Assert.Pass();
        }

        [Test]
        public void TryTake_InCaseOfAdequate_OK()
        {
            var queue = new ConcurrentQueue<int>(new[] { 1, 2, 3 });
            var collection = new AsyncBlockingCollection<int>(queue, queue.Count);
            Assert.IsTrue(collection.TryTake(out _));
        }

        [Test]
        public void TryTake_InCaseOfEmpty_Failed()
        {
            var collection = new AsyncBlockingCollection<int>();
            Assert.IsFalse(collection.TryTake(out _));
        }

        [Test]
        public async Task GetConsumingEnumerableTest()
        {
            var queue = new ConcurrentQueue<int>(new[] { 1, 2, 3 });
            var collection = new AsyncBlockingCollection<int>(queue, queue.Count);
            var enumerable = collection.GetConsumingEnumerable();
            using var tokenSrc = new CancellationTokenSource(1000);
            await using var enumerator = enumerable.GetAsyncEnumerator(tokenSrc.Token);

            for (int i = 0; i < 3; i++)
            {
                await enumerator.MoveNextAsync();
            }

            Assert.CatchAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        }
    }
}