namespace LageBuch.AppLogic.ViewModels;

/// <summary>
/// The German line a view model shows when the Domain refuses a command a peer made stale. The
/// Domain's messages are German sentences, but an <see cref="ArgumentException"/> appends the .NET
/// runtime's English " (Parameter 'x')" (and, out of range, the actual value), which is noise to the
/// Lagebuchführer; that suffix goes.
/// </summary>
public static class DomainRefusal
{
    /// <summary>"Nicht möglich: " and the refusal's own sentence.</summary>
    public static string Message(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = exception.Message;
        if (exception is ArgumentException { ParamName: { } name })
        {
            var suffix = message.IndexOf($" (Parameter '{name}')", StringComparison.Ordinal);
            if (suffix >= 0)
            {
                message = message[..suffix];
            }
        }

        return $"Nicht möglich: {message}";
    }
}
