using SSC.Application.Abstractions;
using SSC.Application.Settings;

namespace SSC.Application.Upload;

public sealed class UploadPipeline
{
    private readonly IReadOnlyList<IUploadProvider> _providers;
    private readonly SettingsService _settings;
    private readonly ILocalizer _localizer;

    public UploadPipeline(
        IEnumerable<IUploadProvider> providers,
        SettingsService settings,
        ILocalizer localizer)
    {
        _providers = (providers ?? throw new ArgumentNullException(nameof(providers))).ToArray();
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    public IReadOnlyList<UploadProviderInfo> Providers =>
        _providers.Select(static provider => provider.Info).ToArray();

    public async Task<UploadResult> UploadAsync(
        UploadRequest request,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Data.Length == 0)
        {
            throw new UploadException(_localizer["uploadEmpty"]);
        }

        var settings = _settings.Current;
        if (!settings.UploadEnabled)
        {
            throw new UploadException(_localizer["uploadDisabled"]);
        }

        var provider = _providers.FirstOrDefault(candidate => candidate.Info.Id == settings.Upload.Provider)
            ?? throw new UploadException(_localizer["uploadNotConfigured"]);
        if (!provider.IsConfigured(settings.Upload))
        {
            throw new UploadException(_localizer["uploadNotConfigured"]);
        }

        var attempts = Math.Clamp(maxAttempts, 1, 5);
        Exception? lastError = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return await provider.UploadAsync(request, settings.Upload, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastError = exception;
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new UploadException(
            $"{_localizer["uploadFailed"]}: {lastError?.Message}",
            lastError ?? new InvalidOperationException("Upload failed."));
    }

    public async Task<bool> DeleteAsync(
        UploadResult result,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Current;
        var provider = _providers.FirstOrDefault(candidate => candidate.Info.Id == result.Provider);
        return provider is not null &&
               await provider.DeleteAsync(result, settings.Upload, cancellationToken).ConfigureAwait(false);
    }
}
