namespace PodScrub.Domain;

public interface IFileSystem
{
    bool FileExists(string path);

    void DeleteFile(string path);

    void CopyFile(string sourcePath, string destinationPath);

    void CreateDirectory(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string content);
}
