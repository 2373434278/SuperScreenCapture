using System.Text;
using SSC.Application.Abstractions;

namespace SSC.Infrastructure.SingleInstance;

public sealed class FileLockSingleInstanceService : ISingleInstanceService
{
    private readonly string _lockPath;
    private FileStream? _stream;

    public FileLockSingleInstanceService(string lockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockPath);
        _lockPath = Path.GetFullPath(lockPath);
    }

    public bool TryAcquire()
    {
        if (_stream is not null)
        {
            return true;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
            _stream = new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);

            _stream.SetLength(0);
            var payload = Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
            _stream.Write(payload);
            _stream.Flush();
            return true;
        }
        catch (IOException)
        {
            _stream?.Dispose();
            _stream = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            _stream?.Dispose();
            _stream = null;
            return false;
        }
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream, null);
        stream?.Dispose();

        try
        {
            if (File.Exists(_lockPath))
            {
                File.Delete(_lockPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
