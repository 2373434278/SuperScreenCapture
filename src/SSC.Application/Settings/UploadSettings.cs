namespace SSC.Application.Settings;

public sealed class UploadSettings
{
    public string Provider { get; set; } = "imgur";

    public string ClientId { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Prefix { get; set; } = "screenshots";

    public string Endpoint { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string FieldName { get; set; } = "file";

    public UploadSettings Clone() => (UploadSettings)MemberwiseClone();
}
