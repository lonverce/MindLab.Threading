﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MindLab.Threading
{
    /// <summary>
    /// 提供<see cref="BlockingCollection{T}"/>的异步版本
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class AsyncBlockingCollection<T>
    {
        #region Fields
        private readonly SemaphoreSlim m_fullSemaphoreSlim;
        private readonly SemaphoreSlim m_emptySemaphoreSlim;
        private readonly IProducerConsumerCollection<T> m_collection;
        #endregion

        #region Properties

        /// <summary>
        /// 获取当前集合中的元素数量
        /// </summary>
        public int Count => m_fullSemaphoreSlim.CurrentCount;

        /// <summary>
        /// 获取集合容量, 如果没有设置最大容量, 则返回<c>int.MaxValue</c>
        /// </summary>
        public int BoundaryCapacity { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// 初始化一个无上限的异步阻塞集合
        /// </summary>
        /// <param name="collection"></param>
        public AsyncBlockingCollection(IProducerConsumerCollection<T> collection)
        {
            m_collection = collection ?? throw new ArgumentNullException(nameof(collection));
            m_fullSemaphoreSlim = new SemaphoreSlim(m_collection.Count);
            BoundaryCapacity = int.MaxValue;
        }

        /// <summary>
        /// 初始化一个无上限的异步阻塞集合，内部使用<see cref="ConcurrentQueue{T}"/>作为存储结构
        /// </summary>
        public AsyncBlockingCollection():this(new ConcurrentQueue<T>()) { }

        /// <summary>
        /// 初始化一个上限为<paramref name="boundary"/>的异步阻塞集合
        /// </summary>
        /// <param name="collection"></param>
        /// <param name="boundary"></param>
        public AsyncBlockingCollection(IProducerConsumerCollection<T> collection, int boundary)
            : this(collection)
        {
            if (collection.Count > boundary || boundary < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(boundary));
            }

            m_emptySemaphoreSlim = new SemaphoreSlim(boundary - collection.Count, boundary);
            BoundaryCapacity = boundary;
        }

        /// <summary>
        /// 初始化一个上限为<paramref name="boundary"/>的异步阻塞集合，内部使用<see cref="ConcurrentQueue{T}"/>作为存储结构
        /// </summary>
        public AsyncBlockingCollection(int boundary):this(new ConcurrentQueue<T>(), boundary) { }

        #endregion

        #region Methods

        /// <summary>
        /// 向集合中添加一个元素, 如果当前集合已满, 则异步阻塞直到有空位可供使用.
        /// </summary>
        /// <param name="item">被添加的元素</param>
        /// <param name="cancellation"></param>
        public async Task AddAsync(T item, CancellationToken cancellation = default)
        {
            if (m_emptySemaphoreSlim != null)
            {
                await m_emptySemaphoreSlim.WaitAsync(cancellation);
            }

            if (!m_collection.TryAdd(item))
            {
                m_emptySemaphoreSlim?.Release();
                return;
            }

            m_fullSemaphoreSlim.Release();
        }

        /// <summary>
        /// 向集合中添加一个元素, 如果当前集合已满, 则立即放弃并返回
        /// </summary>
        /// <param name="item">被添加的元素</param>
        /// <returns>是否添加成功</returns>
        public bool TryAdd(T item)
        {
            if (m_emptySemaphoreSlim != null)
            {
                if (!m_emptySemaphoreSlim.Wait(0))
                {
                    return false;
                }
            }

            if (!m_collection.TryAdd(item))
            {
                m_emptySemaphoreSlim?.Release();
                return false;
            }

            m_fullSemaphoreSlim.Release();
            return true;
        }

        /// <summary>
        /// 从集合中取出一个元素, 如果当前集合为空, 则异步阻塞直到有元素可供取用.
        /// </summary>
        /// <param name="cancellation"></param>
        /// <returns>取出的元素</returns>
        public async Task<T> TakeAsync(CancellationToken cancellation = default)
        {
            await m_fullSemaphoreSlim.WaitAsync(cancellation);

            try
            {
                if (!m_collection.TryTake(out var item))
                {
                    throw new InvalidDataException();
                }
                return item;
            }
            finally
            {
                m_emptySemaphoreSlim?.Release();
            }
        }

        /// <summary>
        /// 从集合中取出一个元素, 如果当前集合为空, 则立即放弃并返回
        /// </summary>
        /// <param name="item"></param>
        /// <returns>是否成功取出</returns>
        public bool TryTake(out T item)
        {
            item = default;
            if (!m_fullSemaphoreSlim.Wait(0))
            {
                return false;
            }

            try
            {
                if (!m_collection.TryTake(out item))
                {
                    throw new InvalidDataException();
                }

                return true;
            }
            finally
            {
                m_emptySemaphoreSlim?.Release();
            }
        }

        /// <summary>
        /// 获取一个用于循环消费集合对象的异步枚举器
        /// </summary>
        public IAsyncEnumerable<T> GetConsumingEnumerable()
        {
            return new ConsumingEnumerable(this);
        }

        #endregion

        #region Enumerable

        /// <summary>
        /// 枚举器
        /// </summary>
        public class ConsumingEnumerable : IAsyncEnumerable<T>
        {
            private readonly AsyncBlockingCollection<T> m_collection;

            /// <summary>
            /// 创建枚举器
            /// </summary>
            /// <param name="collection"></param>
            public ConsumingEnumerable(AsyncBlockingCollection<T> collection)
            {
                m_collection = collection ?? throw new ArgumentNullException(nameof(collection));
            }

            /// <summary>Returns an enumerator that iterates asynchronously through the collection.</summary>
            /// <param name="cancellationToken">A <see cref="T:System.Threading.CancellationToken" /> that may be used
            /// to cancel the asynchronous iteration.</param>
            /// <returns>An enumerator that can be used to iterate asynchronously through the collection.</returns>
            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = new CancellationToken())
            {
                return new ConsumingEnumerator(m_collection, cancellationToken);
            }
        }

        private class ConsumingEnumerator : IAsyncEnumerator<T>
        {
            private readonly AsyncBlockingCollection<T> m_collection;
            private readonly CancellationTokenSource m_disposeToken;
            private int m_status = STA_FREE;
            private const int STA_FREE = 0, STA_WORKING = 1;

            public ConsumingEnumerator(AsyncBlockingCollection<T> collection, CancellationToken cancellation)
            {
                m_collection = collection;
                m_disposeToken = cancellation.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellation) 
                    : new CancellationTokenSource();
            }

            public async ValueTask DisposeAsync()
            {
                m_disposeToken.Cancel();
                await Task.CompletedTask;
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                if (m_disposeToken.IsCancellationRequested)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }

                if (Interlocked.CompareExchange(ref m_status, STA_WORKING, STA_FREE) != STA_FREE)
                {
                    throw new InvalidOperationException("Don't call MoveNextAsync in parallel");
                }

                try
                {
                    Current = await m_collection.TakeAsync(m_disposeToken.Token);
                    return true;
                }
                finally
                {
                    m_status = STA_FREE;
                }
            }

            public T Current { get; private set; }
        }

        #endregion
    }
}
