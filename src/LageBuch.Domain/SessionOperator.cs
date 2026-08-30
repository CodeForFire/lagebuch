namespace LageBuch.Domain;

public sealed record SessionOperator
{
    public SessionOperator(string name, string? callSign = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Operator name must not be blank.", nameof(name));
        }

        this.Name = name.Trim();
        this.CallSign = string.IsNullOrWhiteSpace(callSign) ? null : callSign.Trim();
    }

    public string Name { get; }

    public string? CallSign { get; }

    public string Display => CallSign is null ? Name : $"{Name} ({CallSign})";
}
