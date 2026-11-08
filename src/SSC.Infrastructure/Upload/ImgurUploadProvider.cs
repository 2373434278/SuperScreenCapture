using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SSC.Application.Abstractions;
using SSC.Application.Settings;

namespace SSC.Infrastructure.Upload;

public sealed class ImgurUploadProvider : IUploadProvider
{
    private static readonly HttpClient Http = new();
    private const string ApiBase = "https://api.imgur.com/3/image";

    public UploadProviderInfo Info { get; } = new("imgur", "uploadProviderImgur");

    public bool IsConfigured(UploadSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ClientId);

    public async Task<UploadResult> UploadAsync(
        UploadRequest request,
        UploadSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(Convert.ToBase64String(request.Data), Encoding.UTF8), "image");
        using var message = new HttpRequestMessage(HttpMethod.Post, ApiBase)
        {
            Content = form
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", settings.ClientId.Trim());

        using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");
        var link = data.GetProperty("link").GetString()
            ?? throw new InvalidOperationException("Imgur returned no link.");
        var id = data.TryGetProperty("id", out var idValue) ? idValue.GetString() : null;
        var deleteHash = data.TryGetProperty("deletehash", out var hashValue) ? hashValue.GetString() : null;
        var deleteUrl = string.IsNullOrWhiteSpace(deleteHash) ? null : $"{ApiBase}/{deleteHash}";
        return new UploadResult(Info.Id, link, deleteUrl, id);
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
        message.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", settings.ClientId.Trim());
        using var response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }
}
