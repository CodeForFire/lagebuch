namespace LageBuch.Domain.ValueObjects;

public sealed record IncidentNumber
{
    public IncidentNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Incident number must not be blank.", nameof(value));
        }

        Value = value.Trim();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
