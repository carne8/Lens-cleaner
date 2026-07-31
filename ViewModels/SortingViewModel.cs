using System.Collections.Concurrent;
using System.ComponentModel;
using Arch.Core;
using Arch.Core.Extensions;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using ImageMagick;
using LensCleaner.Models;

namespace LensCleaner.ViewModels;

internal static class Extensions
{
    extension(IStorageFolder e)
    {
        public async Task<IStorageFile[]> GetFilesAsync(bool recursive = true)
        {
            var res = new List<IStorageFile>();
            await e.AddFilesToList(res, recursive);
            return res.ToArray();
        }

        private async Task AddFilesToList(List<IStorageFile> l, bool recursive = true)
        {
            await foreach (var f in e.GetItemsAsync())
            {
                if (f is IStorageFile file) l.Add(file);
                if (recursive && f is IStorageFolder folder) await folder.AddFilesToList(l);
            }
        }
    }
}

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
        {
            Console.WriteLine($"Loaded {Index}");
            Bitmap = bitmap;
        }
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

class PhotoLoader : IDisposable
{
    private static readonly IEqualityComparer<PhotoFile> Comparer = EqualityComparer<PhotoFile>.Create(
        (p1, p2) => p1.Path == p2.Path,
        p => p.Path.GetHashCode()
    );

    private readonly CancellationTokenSource cts = new();
    private readonly BlockingPriorityQueue<PhotoFile, int> queue = new();
    public readonly ConcurrentDictionary<PhotoFile, LoadingBitmap> Cache = new(Comparer);

    public PhotoLoader(int workerCount)
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
                if (!Cache.TryGetValue(file, out var c)) continue;
                if (!c.IsLoading) continue;

                using var image = new MagickImage();
                await image.ReadAsync(file.Path, cts.Token);
                image.AutoOrient();

                var bmp = image.ToWriteableBitmap();
                if (Cache.TryGetValue(file, out var cacheEntry))
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
        if (Cache.TryGetValue(file, out var bmp)) return bmp;

        Cache[file] = new LoadingBitmap(photoIdx);
        queue.Enqueue(file, loadPriority);
        return Cache[file];
    }

    /// <summary>
    /// Enqueue image loading
    /// </summary>
    public void Prefetch(PhotoFile file, int photoIdx, int loadPriority)
    {
        if (Cache.ContainsKey(file)) return;
        Cache[file] = new LoadingBitmap(photoIdx);
        queue.Enqueue(file, loadPriority);
    }

    public void StopCurrentLoads()
    {
        queue.Clear();
        foreach (var kv in Cache)
            if (kv.Value.IsLoading)
            {
                Cache.Remove(kv.Key, out _);
                kv.Value.Dispose();
            }
    }

    /// <summary>
    /// Removes an item from cache
    /// </summary>
    public void Evict(PhotoFile file)
    {
        if (!Cache.Remove(file, out var cacheEntry)) return;
        cacheEntry.Dispose();
    }

    public void Dispose()
    {
        cts.Dispose();
        foreach (var kv in Cache)
        {
            Cache.Remove(kv.Key, out _);
            kv.Value.Dispose();
        }
    }
}

internal partial class CacheManager : ObservableObject
{
    private readonly Photo[] photos;
    private readonly int cacheSizeBefore;
    private readonly int cacheSizeAfter;

    private readonly PhotoLoader loader;

    [ObservableProperty] public partial LoadingBitmap CurrentBitmap { get; private set; }

    public CacheManager(Photo[] photos, int cacheSizeBefore, int cacheSizeAfter)
    {
        this.photos = photos;
        this.cacheSizeBefore = cacheSizeBefore;
        this.cacheSizeAfter = cacheSizeAfter;
        loader = new PhotoLoader(cacheSizeAfter);
        SelectImage(0);
    }

    public void SelectImage(int selectedIdx)
    {
        if (selectedIdx < 0 || photos.Length <= selectedIdx) return;
        loader.StopCurrentLoads();

        if (!GetPhotoFile(selectedIdx, out var file)) throw new IndexOutOfRangeException();
        CurrentBitmap = loader.GetBitmap(file, selectedIdx, int.MinValue);

        // Unload bitmaps
        foreach (var kv in loader.Cache)
        {
            var photoIdx = kv.Value.Index;
            if (selectedIdx - cacheSizeBefore <= photoIdx &&
                photoIdx <= selectedIdx + cacheSizeAfter) continue;
            loader.Evict(kv.Key);
        }

        // Load adjacent bitmaps
        for (var i = 1; i <= cacheSizeAfter; i++)
        {
            var photoIdx = selectedIdx + i;
            if (!GetPhotoFile(photoIdx, out var f)) break;
            loader.Prefetch(f, photoIdx, 2*i-1);
        }
        for (var i = 1; i <= cacheSizeBefore; i++)
        {
            var photoIdx = selectedIdx - i;
            if (!GetPhotoFile(photoIdx, out var f)) break;
            loader.Prefetch(f, photoIdx, 2*i);
        }
    }

    private bool GetPhotoFile(int idx, out PhotoFile file)
    {
        file = default;
        if (idx < 0 || photos.Length <= idx) return false;

        var fileEntity = photos[idx].Files[0];
        if (!photos[idx].Files[0].Has<PhotoFile>()) return false;

        file = fileEntity.Get<PhotoFile>();
        return true;
    }
}

public partial class SortingViewModel : ViewModelBase
{
    private CacheManager? cache;

    [ObservableProperty]
    public partial Photo[] Photos { get; set; }

    private int selectedPhotoIndex;
    public int SelectedPhotoIndex
    {
        get => selectedPhotoIndex;
        set
        {
            selectedPhotoIndex = value;
            LoadBitmap();
        }
    }

    public LoadingBitmap? CurrentImage => cache?.CurrentBitmap;

    public SortingViewModel(IStorageFolder folder)
    {
        Task.Run(async () =>
        {
            var world = World.Create();
            var photosDict = new Dictionary<string, Entity>();
            await AddEntities(world, photosDict, folder);

            Photos = photosDict
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value.Get<Photo>())
                .ToArray();

            cache = new CacheManager(Photos, 6, 6);
            cache.PropertyChanged += CacheOnPropertyChanged;
            OnPropertyChanged(nameof(CurrentImage));
        });
    }

    private static async Task AddEntities(World world, Dictionary<string, Entity> nameToPhoto, IStorageFolder folder)
    {
        foreach (var file in await folder.GetFilesAsync())
        {
            var filename = Path.GetFileNameWithoutExtension(file.Name);
            var path = file.TryGetLocalPath();
            if (path is null) continue;

            // Get or add photo
            var photoExists = nameToPhoto.TryGetValue(filename, out var p);
            var photoEntity =
                photoExists
                    ? p
                    : world.Create(new Photo { Name = filename });
            if (!photoExists) nameToPhoto.Add(filename, photoEntity);

            if (!PhotoFile.TryParseFile(photoEntity, path, out var photoFile)) continue;
            var photoFileEntity = world.Create(photoFile);
            photoEntity.Get<Photo>().Files.Add(photoFileEntity);
        }
    }

    public void NextImage()
    {
        selectedPhotoIndex = int.Min(selectedPhotoIndex + 1, Photos.Length - 1);
        OnPropertyChanged(nameof(SelectedPhotoIndex));
    }

    public void PreviousImage()
    {
        selectedPhotoIndex = int.Max(selectedPhotoIndex - 1, 0);
        OnPropertyChanged(nameof(SelectedPhotoIndex));
    }

    public void LoadBitmap() => cache?.SelectImage(selectedPhotoIndex);


    private void CacheOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(cache.CurrentBitmap)) OnPropertyChanged(nameof(CurrentImage));
    }
}
