namespace PodScrub.Domain;

public interface IFileSystem
{
    bool FileExists(string path);

    void DeleteFile(string path);
}
