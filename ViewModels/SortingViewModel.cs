using System.Collections.Concurrent;
using Arch.Core.Extensions;
using Avalonia.Media.Imaging;
using ImageMagick;
using LensCleaner.Models;

namespace LensCleaner.ViewModels;

public partial class LoadingBitmap(int idx) : ObservableObject, IDisposable
{
    [ObservableProperty]
    public partial WriteableBitmap? Bitmap { get; set; }

    private readonly TaskCompletionSource<WriteableBitmap> tcs = new();
    private Task<WriteableBitmap> LoadTask => tcs.Task;
    public bool IsLoading => Bitmap is null;

    public readonly int Index = idx;

    public void SetLoaded(WriteableBitmap bitmap)
    {
        if (tcs.TrySetResult(bitmap))
            Bitmap = bitmap;
        else
            bitmap.Dispose();
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Bitmap?.Dispose();
        LoadTask.ContinueWith(t =>
        {
            Bitmap?.Dispose();
            t.Dispose();
        });
        tcs.TrySetCanceled();
    }
}

internal class CacheManager : IDisposable
{
    private static readonly IEqualityComparer<PhotoFile> Comparer = EqualityComparer<PhotoFile>.Create(
        (p1, p2) => p1.Path == p2.Path,
        p => p.Path.GetHashCode()
    );

    private readonly CancellationTokenSource cts = new();
    private readonly BlockingPriorityQueue<PhotoFile, int> queue = new();
    public readonly ConcurrentDictionary<PhotoFile, LoadingBitmap> Items = new(Comparer);

    public CacheManager(int workerCount)
    {
        for (var i = 0; i < workerCount; i++)
            Task.Run(Worker);
    }

    private async void Worker()
    {
        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var file = await queue.DequeueAsync(cts.Token);
                if (!Items.TryGetValue(file, out var c)) continue;
                if (!c.IsLoading) continue;

                using var image = new MagickImage();
                await image.ReadAsync(file.Path, cts.Token);
                image.AutoOrient();

                var bmp = image.ToWriteableBitmap();
                if (Items.TryGetValue(file, out var cacheEntry))
                    cacheEntry.SetLoaded(bmp);
                else
                    bmp.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    public LoadingBitmap GetBitmap(PhotoFile file, int photoIdx, int loadPriority)
    {
        if (Items.TryGetValue(file, out var bmp)) return bmp;

        Items[file] = new LoadingBitmap(photoIdx);
        queue.Enqueue(file, loadPriority);
        return Items[file];
    }

    /// <summary>
    /// Enqueue image loading
    /// </summary>
    public void Prefetch(PhotoFile file, int photoIdx, int loadPriority)
    {
        if (Items.ContainsKey(file)) return;
        Items[file] = new LoadingBitmap(photoIdx);
        queue.Enqueue(file, loadPriority);
    }

    public void StopCurrentLoads()
    {
        queue.Clear();
        foreach (var kv in Items)
            if (kv.Value.IsLoading)
            {
                Items.Remove(kv.Key, out _);
                kv.Value.Dispose();
            }
    }

    /// <summary>
    /// Removes an item from cache
    /// </summary>
    public void Evict(PhotoFile file)
    {
        if (!Items.Remove(file, out var cacheEntry)) return;
        cacheEntry.Dispose();
    }

    public void Dispose()
    {
        cts.Dispose();
        foreach (var kv in Items)
        {
            Items.Remove(kv.Key, out _);
            kv.Value.Dispose();
        }
    }
}

public partial class SortingViewModel : ViewModelBase
{
    private readonly CacheManager cache;
    private const int CacheSizeAfter = 12;
    private const int CacheSizeBefore = 12;

    [ObservableProperty] public partial Photo[] Photos { get; set; }
    [ObservableProperty] public partial LoadingBitmap CurrentImage { get; private set; }

    private int currentImageIdx;
    public int CurrentImageIdx
    {
        get => currentImageIdx;
        set
        {
            currentImageIdx = value;
            LoadImage();
        }
    }

    public SortingViewModel(Photo[] photos)
    {
        Photos = photos;
        cache = new CacheManager(CacheSizeAfter);
        LoadImage();
    }

    public void NextImage()
    {
        currentImageIdx = int.Min(currentImageIdx + 1, Photos.Length - 1);
        OnPropertyChanged(nameof(CurrentImageIdx));
    }

    public void PreviousImage()
    {
        currentImageIdx = int.Max(currentImageIdx - 1, 0);
        OnPropertyChanged(nameof(CurrentImageIdx));
    }

    public void LoadImage()
    {
        if (currentImageIdx < 0 || Photos.Length <= currentImageIdx) return;
        cache.StopCurrentLoads();

        if (!GetPhotoFile(currentImageIdx, out var file)) throw new IndexOutOfRangeException();
        CurrentImage = cache.GetBitmap(file, currentImageIdx, int.MinValue);

        // Unload bitmaps
        foreach (var kv in cache.Items)
        {
            var photoIdx = kv.Value.Index;
            if (currentImageIdx - CacheSizeBefore <= photoIdx &&
                photoIdx <= currentImageIdx + CacheSizeAfter) continue;
            cache.Evict(kv.Key);
        }

        // Load adjacent bitmaps
        for (var i = 1; i <= CacheSizeAfter; i++)
        {
            var photoIdx = currentImageIdx + i;
            if (!GetPhotoFile(photoIdx, out var f)) break;
            cache.Prefetch(f, photoIdx, 2*i-1);
        }
        for (var i = 1; i <= CacheSizeBefore; i++)
        {
            var photoIdx = currentImageIdx - i;
            if (!GetPhotoFile(photoIdx, out var f)) break;
            cache.Prefetch(f, photoIdx, 2*i);
        }
    }

    private bool GetPhotoFile(int idx, out PhotoFile file)
    {
        file = default;
        if (idx < 0 || Photos.Length <= idx) return false;

        var fileEntity = Photos[idx].Files[0];
        if (!Photos[idx].Files[0].Has<PhotoFile>()) return false;

        file = fileEntity.Get<PhotoFile>();
        return true;
    }
}
