namespace LageBuch.Domain.Time;

public interface IClock
{
    DateTimeOffset Now { get; }
}
