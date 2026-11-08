using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using SSC.Application.Abstractions;
using SSC.Application.Settings;

namespace SSC.Infrastructure.Upload;

public sealed class S3UploadProvider : IUploadProvider
{
    public UploadProviderInfo Info { get; } = new("s3", "uploadProviderS3");

    public bool IsConfigured(UploadSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.Bucket) &&
        !string.IsNullOrWhiteSpace(settings.Region) &&
        !string.IsNullOrWhiteSpace(settings.AccessKey) &&
        !string.IsNullOrWhiteSpace(settings.SecretKey);

    public async Task<UploadResult> UploadAsync(
        UploadRequest request,
        UploadSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(settings);
        var key = BuildKey(settings, request.FileName);
        using var stream = new MemoryStream(request.Data, writable: false);
        await client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = settings.Bucket,
            Key = key,
            InputStream = stream,
            ContentType = request.ContentType
        }, cancellationToken).ConfigureAwait(false);

        var url = BuildUrl(settings, key);
        return new UploadResult(Info.Id, url, url, key);
    }

    public async Task<bool> DeleteAsync(
        UploadResult result,
        UploadSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(result.RemoteId))
        {
            return false;
        }

        using var client = CreateClient(settings);
        await client.DeleteObjectAsync(
            settings.Bucket,
            result.RemoteId,
            cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static AmazonS3Client CreateClient(UploadSettings settings)
    {
        var credentials = new BasicAWSCredentials(settings.AccessKey.Trim(), settings.SecretKey.Trim());
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return new AmazonS3Client(credentials, new AmazonS3Config
            {
                ServiceURL = settings.Endpoint.Trim(),
                ForcePathStyle = true
            });
        }

        return new AmazonS3Client(
            credentials,
            Amazon.RegionEndpoint.GetBySystemName(settings.Region.Trim()));
    }

    private static string BuildKey(UploadSettings settings, string fileName)
    {
        var prefix = string.IsNullOrWhiteSpace(settings.Prefix) ? "screenshots" : settings.Prefix.Trim();
        var safeName = string.Concat(fileName.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-'));
        return $"{prefix.TrimEnd('/')}/{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{safeName}";
    }

    private static string BuildUrl(UploadSettings settings, string key)
    {
        var encodedKey = string.Join(
            "/",
            key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return $"{settings.Endpoint.TrimEnd('/')}/{settings.Bucket}/{encodedKey}";
        }

        return $"https://{settings.Bucket}.s3.{settings.Region}.amazonaws.com/{encodedKey}";
    }
}
