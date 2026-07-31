using Arch.Core;
using ImageMagick;

namespace LensCleaner.Models;

public readonly struct Photo()
{
    public required Entity Entity { get; init; }
    public required string Name { get; init; }
    public List<Entity> Files { get; } = [];
}

public readonly struct PhotoFile
{
    public Entity Photo { get; private init; }
    public string Path { get; private init; }
    public uint Width { get; private init; }
    public uint Height { get; private init; }
    public DateTimeOffset? Date { get; private init; }
    public long Size { get; private init; }

    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);

    public static bool TryParseFile(Entity photo, string path, out PhotoFile photoFile)
    {
        try
        {
            using var image = new MagickImage();
            image.Ping(path);

            var dateString = image.GetAttribute("dng:create.date") ?? image.GetAttribute("date:modify");
            var date = DateTimeOffset.TryParse(dateString, out var d)
                ? d
                : (DateTimeOffset?)null;

            var size = new FileInfo(path).Length;
            photoFile = new PhotoFile
            {
                Photo = photo,
                Path = path,
                Height = image.Height,
                Width = image.Width,
                Date = date,
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
