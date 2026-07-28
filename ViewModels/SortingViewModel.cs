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

internal partial class CacheManager : ObservableObject
{
    private readonly Photo[] photos;
    private readonly int cachedCountBefore;
    private readonly int cachedCountAfter;
    private readonly LinkedList<Task<WriteableBitmap>> beforeCache = [];
    private readonly LinkedList<Task<WriteableBitmap>> afterCache = [];

    [ObservableProperty] public partial int CurrentIndex { get; private set; } = -1;
    public Task<WriteableBitmap> CurrentBitmap
    {
        get;
        private set
        {
            OnPropertyChanging();
            field = null!; // Force !CurrentImage^ to update
            OnPropertyChanged();
            OnPropertyChanging();
            field = value;
            OnPropertyChanged();
        }
    }

    public CacheManager(Photo[] photos, int cachedCountBefore, int cachedCountAfter)
    {
        this.photos = photos;
        this.cachedCountBefore = cachedCountBefore;
        this.cachedCountAfter = cachedCountAfter;
        SelectImage(0);
    }

    public void NextImage()
    {
        if (afterCache.Count == 0) return;
        beforeCache.AddLast(CurrentBitmap);
        CurrentBitmap = afterCache.First();
        afterCache.RemoveFirst();
        CurrentIndex += 1;

        // Delete oldest
        if (beforeCache.Count > cachedCountBefore)
        {
            beforeCache.First().ContinueWith(t => t.Dispose());
            beforeCache.RemoveFirst();
        }

        // Load a new bitmap
        var idxToLoad = CurrentIndex + cachedCountAfter;
        if (photos.Length <= idxToLoad) return;
        var task = LoadBitmap(photos[idxToLoad].Files[0]);
        afterCache.AddLast(task);
    }

    public void PreviousImage()
    {
        if (beforeCache.Count == 0) return;
        afterCache.AddFirst(CurrentBitmap);
        CurrentBitmap = beforeCache.Last();
        beforeCache.RemoveLast();
        CurrentIndex -= 1;

        // Delete newest
        if (afterCache.Count > cachedCountAfter)
        {
            afterCache.Last().ContinueWith(t => t.Dispose());
            afterCache.RemoveLast();
        }

        // Load a new bitmap
        var idxToLoad = CurrentIndex - cachedCountBefore;
        if (idxToLoad < 0) return;
        var task = LoadBitmap(photos[idxToLoad].Files[0]);
        beforeCache.AddFirst(task);
    }

    public void SelectImage(int selectedIdx)
    {
        if (selectedIdx == CurrentIndex) return;

        var newBeforeCache = new Task<WriteableBitmap>?[cachedCountBefore];
        var newAfterCache = new Task<WriteableBitmap>?[cachedCountAfter];
        Task<WriteableBitmap>? newCurrentBitmap = null;

        while (beforeCache.Count > 0)
        {
            TryRecycle(
                CurrentIndex - beforeCache.Count,
                beforeCache.First()
            );
            beforeCache.RemoveFirst();
        }

        TryRecycle(CurrentIndex, CurrentBitmap);

        var k = 1;
        while (afterCache.Count > 0)
        {
            TryRecycle(CurrentIndex + k++, afterCache.First());
            afterCache.RemoveFirst();
        }

        beforeCache.Clear();
        for (var i = 0; i < newBeforeCache.Length; i++)
        {
            if (newBeforeCache[i] is { } t)
            {
                beforeCache.AddLast(t);
                continue;
            }

            var bmpIdx = selectedIdx - cachedCountBefore + i;
            if (bmpIdx < 0) continue;
            var tcs = LoadBitmap(photos[bmpIdx].Files[0]);
            beforeCache.AddLast(tcs);
        }

        afterCache.Clear();
        for (var i = 0; i < newAfterCache.Length; i++)
        {
            if (newAfterCache[i] is { } t)
            {
                afterCache.AddLast(t);
                continue;
            }

            var bmpIdx = selectedIdx + 1 + i;
            if (photos.Length <= bmpIdx) break;
            var tcs = LoadBitmap(photos[bmpIdx].Files[0]);
            afterCache.AddLast(tcs);
        }

        CurrentIndex = selectedIdx;
        CurrentBitmap = newCurrentBitmap ?? LoadBitmap(photos[selectedIdx].Files[0]);
        return;

        void TryRecycle(int bmpIdx, Task<WriteableBitmap> bmp)
        {
            if (bmpIdx < 0 || photos.Length <= bmpIdx) return;
            var goesInNewBeforeCache = selectedIdx - cachedCountBefore <= bmpIdx && bmpIdx < selectedIdx;
            var goesInNewAfterCache = selectedIdx < bmpIdx && bmpIdx <= selectedIdx + cachedCountAfter;
            var isNewCurrent = bmpIdx == selectedIdx;

            if (goesInNewBeforeCache)
                newBeforeCache[cachedCountBefore - selectedIdx + bmpIdx] = bmp;
            else if (goesInNewAfterCache)
                newAfterCache[bmpIdx - selectedIdx - 1] = bmp;
            else if (isNewCurrent)
                newCurrentBitmap = bmp;
            else
                bmp.ContinueWith(b => b.Dispose());
        }
    }

    private static Task<WriteableBitmap> LoadBitmap(Entity photoFile)
    {
        var path = photoFile.Get<PhotoFile>().Path;
        return Task.Run(() =>
        {
            using var image = new MagickImage(path);
            return Task.FromResult(image.ToWriteableBitmap());
        });
    }
}

public partial class SortingViewModel : ViewModelBase
{
    private CacheManager? cache;

    [ObservableProperty]
    public partial Photo[]? Photos { get; set; }

    public int SelectedPhotoIndex {
        get => cache?.CurrentIndex ?? 0;
        set => cache!.SelectImage(value);
    }

    public Task<WriteableBitmap>? CurrentImage => cache?.CurrentBitmap;

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

            cache = new CacheManager(Photos, 3, 3);
            cache.PropertyChanged += CacheOnPropertyChanged;
            OnPropertyChanged(nameof(CurrentImage));
            OnPropertyChanged(nameof(SelectedPhotoIndex));
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

    public void NextImage() => cache?.NextImage();
    public void PreviousImage() => cache?.PreviousImage();


    private void CacheOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(cache.CurrentBitmap)) OnPropertyChanged(nameof(CurrentImage));
        if (e.PropertyName == nameof(cache.CurrentIndex)) OnPropertyChanged(nameof(SelectedPhotoIndex));
    }
}
