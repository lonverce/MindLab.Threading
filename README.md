# MindLab.Threading

**MindLab.Threading**主要封装了一些在面向**async/await**异步编程时所使用的线程间同步对象。

## 使用
用户可从nuget中下载: [MindLab.Threading](https://www.nuget.org/packages/MindLab.Threading)

## 示例

### IAsyncLock接口
**IAsyncLock**表示一个支持异步操作的互斥锁对象接口，目前的实现类有三种，分别是**MonitorLock**、**CasLock**以及**SemaphoreLock**。下面的样例代码演示实现线程安全的自增/自减类。

**PS**:***这里只是为了演示需要，在实际的项目中，我们建议使用原子操作（Interlocked）实现线程安全的自增功能***

```csharp
public class MyClass
{
    // 此处可以替换为 new CasLock() 或 new SemaphoreLock()
    private readonly IAsyncLock m_lock = new MonitorLock();
    private int m_value;    
    
    public async Task IncreaseAsync(CancellationToken cancellation)
    {
        await using(await m_lock.LockAsync(cancellation))
        {
          m_value++;
        }
    }
     
    public async Task<bool> TryIncrease()
    {
        if(!m_lock.TryLock(out var locker))
        {
            return false;
        }
        m_value++;
        await locker.DisposeAsync();
        return true;
    }
    
    public async Task DecreaseAsync(CancellationToken cancellation)
    {
        await using(await m_lock.LockAsync(cancellation))
        {
            m_value--;
        }
    }
}
```

上述代码中，无论是使用**MonitorLock**、**CasLock**或者**SemaphoreLock**，其功能都是一致的，只是底层的实现方式不同，所以性能上不一样。

+ **MonitorLock** ：内部基于**System.Threading.Monitor**实现，从目前的测试数据来看，性能最好；

+ **CasLock** ： 内部基于原子操作的Lock-Free实现，不会导致调用线程的挂起堵塞；

+ **SemaphoreLock**：内部基于System.Threading.SemaphoreSlim实现异步处理；

### OnceFlag
在我们日常的应用场景中，尤其是在实现***Dispose***方法的时候，常常需要编写这种形式的代码：

```csharp
private bool m_disposed; // 指示当前对象是否已经被disposed
private readonly object m_disposeLock = new object();

public void Dispose()
{
    if(m_disposed)
    {
        return;
    }
    
    lock(m_disposeLock)
    {
        if(m_disposed)
        {
            return;
        }
        
        m_disposed = true;
    }
    
    //TODO: 释放资源 ...
}
```

上述代码是为了避免调用方会多线程地调用Dispose()方法，这种写法较为麻烦，所以我们提供了**OnceFlag**来处理类似的需求:

```csharp
private readonly OnceFlag m_disposeFlag = new OnceFlag();

public void Dispose()
{
    if(!m_disposeFlag.TrySet())
    {
        return;
    }
    //TODO: 释放资源 ...
}
```

**OnceFlag**内部采用Lock-Free实现，相比起直接用lock会更加高效。

### AsyncBlockingCollection
AsyncBlockingCollection 在功能上等价于原生的 **System.Collections.Concurrent.BlockingCollection**, 不同之处在于完全使用了基于async/await的异步接口。以下示例模拟了从文件中加载文本并异步输出到控制台的功能：

```csharp
private readonly AsyncBlockingCollection<string> m_queue = new AsyncBlockingCollection<string>(capacity:128);

public async Task RunAsync(string fileName, CancellationToken token)
{
    var readTask = LoadDataAsync(fileName, token);
    var writeTask = ShowDataAsync(token);
    Task.WhenAll(readTask, writeTask);
}

private async Task LoadDataAsync(string fileName, CancellationToken token)
{
    await using var fs = File.OpenRead(fileName);
    using var reader = new StreamReader(fs);
    
    while(!token.IsCancellationRequested)
    {
        var lineText = await reader.ReadLineAsync();
        await m_queue.AddAsync(lineText, token);
    }
}

private async Task ShowDataAsync(CancellationToken token)
{
    while(!token.IsCancellationRequested)
    {
        var lineText = await m_queue.TakeAsync(token);
        Console.WriteLine(lineText);
    }
}

```
