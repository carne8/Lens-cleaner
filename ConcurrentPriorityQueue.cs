namespace System.Collections.Concurrent;

public sealed class BlockingPriorityQueue<T, TPriority>
{
    private readonly PriorityQueue<T, TPriority> queue = new();
    private readonly SemaphoreSlim items = new(0);
    private readonly Lock @lock = new();

    public void Enqueue(T item, TPriority priority)
    {
        lock (@lock) queue.Enqueue(item, priority);
        items.Release();
    }

    public async Task<T> DequeueAsync(CancellationToken ct = default)
    {
        await items.WaitAsync(ct);
        lock (@lock) return queue.Dequeue();
    }

    public void Clear()
    {
        int removedCount;

        lock (@lock)
        {
            removedCount = queue.Count;
            queue.Clear();
        }

        // Consume the semaphore permits corresponding
        // to the removed items.
        for (var i = 0; i < removedCount; i++) items.Wait(0);
    }
}
