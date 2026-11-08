namespace SSC.Application.Abstractions;

public interface ISettingsRepository
{
    string? Read(string key);

    void Write(string key, string value);
}
