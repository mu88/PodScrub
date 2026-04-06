using System.Diagnostics.CodeAnalysis;
using PodScrub.Domain;

namespace PodScrub.Infrastructure;

[ExcludeFromCodeCoverage]
public sealed class FileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public void DeleteFile(string path) => File.Delete(path);
}
