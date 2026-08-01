using Arch.Core;
using Arch.Core.Extensions;
using Avalonia.Platform.Storage;
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

public partial class LoadingSortingViewModel : ViewModelBase
{
    [ObservableProperty] public partial double Progress { get; set; }

    public LoadingSortingViewModel(IStorageFolder folder, MainViewModel mainViewModel) =>
        Task.Run(async () =>
        {
            var world = World.Create();
            var photosDict = new Dictionary<string, Entity>();
            await AddEntities(world, photosDict, folder);

            var photos = photosDict
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value.Get<Photo>())
                .ToArray();

            mainViewModel.OpenSortingView(photos);
        });

    private async Task AddEntities(World world, Dictionary<string, Entity> nameToPhoto, IStorageFolder folder)
    {
        var files = await folder.GetFilesAsync();
        for (var i = 0; i < files.Length; i++)
        {
            var file = files[i];
            var filename = Path.GetFileNameWithoutExtension(file.Name);
            var path = file.TryGetLocalPath();
            if (path is null) continue;

            // Get or add photo
            var photoExists = nameToPhoto.TryGetValue(filename, out var p);
            var photoEntity = photoExists ? p : world.Create();

            if (!photoExists)
            {
                photoEntity.Add(new Photo
                {
                    Name = filename,
                    Entity = photoEntity
                });
                nameToPhoto.Add(filename, photoEntity);
            }

            if (!PhotoFile.TryParseFile(photoEntity, path, out var photoFile))
            {
                if (!photoExists)
                {
                    world.Destroy(photoEntity);
                    nameToPhoto.Remove(filename);
                }
                continue;
            }
            var photoFileEntity = world.Create(photoFile);
            photoEntity.Get<Photo>().Files.Add(photoFileEntity);

            // Update progress
            Progress = (double)i / files.Length;
        }
    }
}
