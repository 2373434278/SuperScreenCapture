using System.Security.Cryptography;
using SSC.Application.Abstractions;

namespace SSC.Infrastructure.Storage;

public sealed class FileBlobStore : IBlobStore
{
    private readonly string _rootDirectory;

    public FileBlobStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<string> SaveAsync(
        Stream content,
        string extension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedExtension = NormalizeExtension(extension);
        var tempPath = Path.Combine(_rootDirectory, $".incoming-{Guid.NewGuid():N}{normalizedExtension}");

        try
        {
            string hash;
            await using (var output = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hasher.AppendData(buffer, 0, read);
                }

                hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            }

            var relativePath = Path.Combine(
                hash[..2],
                string.Concat(hash.AsSpan(2), normalizedExtension));
            var fullPath = GetFullPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            if (File.Exists(fullPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, fullPath);
            }

            return relativePath.Replace('\\', '/');
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    public string GetFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path is required.", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, relativePath));
        var rootWithSeparator = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _rootDirectory
            : _rootDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException("Path escapes the blob store root.", nameof(relativePath));
        }

        return fullPath;
    }

    public bool Exists(string relativePath) => File.Exists(GetFullPath(relativePath));

    public bool Delete(string relativePath)
    {
        var fullPath = GetFullPath(relativePath);
        if (!File.Exists(fullPath))
        {
            return false;
        }

        File.Delete(fullPath);
        return true;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        var trimmed = extension.Trim().ToLowerInvariant();
        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        if (trimmed.Length is < 2 or > 12 || !trimmed[1..].All(char.IsLetterOrDigit))
        {
            throw new ArgumentException("Extension is not valid.", nameof(extension));
        }

        return trimmed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }
}
