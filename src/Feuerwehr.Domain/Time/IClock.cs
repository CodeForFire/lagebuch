namespace Feuerwehr.Domain.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
