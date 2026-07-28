using Arch.Core;
using ImageMagick;

namespace LensCleaner.Models;

public struct Photo()
{
    public string Name { get; init; }
    public List<Entity> Files { get; } = [];
}

public struct PhotoFile
{
    public Entity Photo { get; private init; }
    public string Path { get; private init; }
    public uint Width { get; private init; }
    public uint Height { get; private init; }
    public long Size { get; private init; }

    public static bool TryParseFile(Entity photo, string path, out PhotoFile photoFile)
    {
        try
        {
            var image = new MagickImageInfo(path);
            var size = new FileInfo(path).Length;
            photoFile = new PhotoFile
            {
                Photo = photo,
                Path = path,
                Height = image.Height,
                Width = image.Width,
                Size = size
            };
            return true;
        }
        catch (Exception)
        {
            photoFile = new PhotoFile();
            return false;
        }
    }
}
