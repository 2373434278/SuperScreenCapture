using System.ComponentModel;

namespace SSC.Application.Abstractions;

public interface ILocalizer : INotifyPropertyChanged
{
    string Language { get; }

    IReadOnlyList<string> SupportedLanguages { get; }

    string this[string key] { get; }

    bool SetLanguage(string language);

    event EventHandler<string>? LanguageChanged;
}
