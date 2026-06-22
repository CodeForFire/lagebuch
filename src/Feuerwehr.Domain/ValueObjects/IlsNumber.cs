using System.Text.RegularExpressions;

namespace Feuerwehr.Domain.ValueObjects;

public sealed partial record IlsNumber
{
    private IlsNumber(string value) => Value = value;

    public string Value { get; }

    public static IlsNumber Parse(string input)
    {
        if (input is not null && FourDigits().IsMatch(input))
            return new IlsNumber(input);
        throw new FormatException("ILS number must be exactly 4 digits.");
    }

    public static bool TryParse(string? input, out IlsNumber? result)
    {
        if (input is not null && FourDigits().IsMatch(input))
        {
            result = new IlsNumber(input);
            return true;
        }
        result = null;
        return false;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^\d{4}$")]
    private static partial Regex FourDigits();
}
