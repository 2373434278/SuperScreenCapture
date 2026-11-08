namespace SSC.Application.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
