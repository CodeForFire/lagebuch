namespace Feuerwehr.Domain;

public sealed record SessionOperator
{
    public SessionOperator(string Name, string? CallSign = null)
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Operator name must not be blank.", nameof(Name));

        this.Name = Name.Trim();
        this.CallSign = string.IsNullOrWhiteSpace(CallSign) ? null : CallSign.Trim();
    }

    public string Name { get; }
    public string? CallSign { get; }

    public string Display => CallSign is null ? Name : $"{Name} ({CallSign})";
}
