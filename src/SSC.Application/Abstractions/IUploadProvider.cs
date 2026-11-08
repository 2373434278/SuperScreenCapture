using SSC.Application.Settings;

namespace SSC.Application.Abstractions;

public sealed record UploadRequest(byte[] Data, string FileName, string ContentType);

public sealed record UploadResult(
    string Provider,
    string Url,
    string? DeleteUrl = null,
    string? RemoteId = null);

public sealed record UploadProviderInfo(string Id, string DisplayNameKey);

public interface IUploadProvider
{
    UploadProviderInfo Info { get; }

    bool IsConfigured(UploadSettings settings);

    Task<UploadResult> UploadAsync(
        UploadRequest request,
        UploadSettings settings,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        UploadResult result,
        UploadSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class UploadException : Exception
{
    public UploadException(string message)
        : base(message)
    {
    }

    public UploadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
