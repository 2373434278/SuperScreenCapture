using System.Net.Http.Headers;
using System.Text.Json;
using SSC.Application.Abstractions;
using SSC.Application.Settings;

namespace SSC.Infrastructure.Upload;

public sealed class CustomHttpUploadProvider : IUploadProvider
{
    private static readonly HttpClient Http = new();

    public UploadProviderInfo Info { get; } = new("custom", "uploadProviderCustom");

    public bool IsConfigured(UploadSettings settings) =>
        Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public async Task<UploadResult> UploadAsync(
        UploadRequest request,
        UploadSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint.Trim());
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token.Trim());
        }

        if (string.IsNullOrWhiteSpace(settings.FieldName))
        {
            message.Content = new ByteArrayContent(request.Data);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
        }
        else
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(request.Data);
            file.Headers.ContentType = new MediaTypeHeaderValue(request.ContentType);
            form.Add(file, settings.FieldName.Trim(), request.FileName);
            message.Content = form;
        }

        using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var url = ExtractUrl(body)
            ?? throw new InvalidOperationException("Custom upload endpoint returned no URL.");
        return new UploadResult(Info.Id, url, url);
    }

    public async Task<bool> DeleteAsync(
        UploadResult result,
        UploadSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(result.DeleteUrl))
        {
            return false;
        }

        using var message = new HttpRequestMessage(HttpMethod.Delete, result.DeleteUrl);
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token.Trim());
        }

        using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private static string? ExtractUrl(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var trimmed = body.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var direct) &&
            (direct.Scheme == Uri.UriSchemeHttp || direct.Scheme == Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (TryGetString(root, "url", out var url) ||
                TryGetString(root, "link", out url))
            {
                return url;
            }

            if (root.TryGetProperty("data", out var data))
            {
                if (TryGetString(data, "url", out url) ||
                    TryGetString(data, "link", out url))
                {
                    return url;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static bool TryGetString(JsonElement element, string property, out string? value)
    {
        if (element.TryGetProperty(property, out var propertyValue) &&
            propertyValue.ValueKind == JsonValueKind.String)
        {
            value = propertyValue.GetString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = null;
        return false;
    }
}
