namespace SSC.Application.Abstractions;

public interface IBlobStore
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);

    string GetFullPath(string relativePath);

    bool Exists(string relativePath);

    bool Delete(string relativePath);
}
