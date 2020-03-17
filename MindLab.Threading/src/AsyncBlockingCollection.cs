using System;
using System.Collections.Concurrent;
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

        #region Constructors

        /// <summary>
        /// 初始化一个无上限的异步阻塞集合
        /// </summary>
        /// <param name="collection"></param>
        public AsyncBlockingCollection(IProducerConsumerCollection<T> collection)
        {
            m_collection = collection ?? throw new ArgumentNullException(nameof(collection));
            m_fullSemaphoreSlim = new SemaphoreSlim(m_collection.Count);
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

        #endregion
    }
}
