namespace LageBuch.AppLogic.Services.Map;

/// <summary>Why a Luftbild could not be loaded, in German.</summary>
public sealed class LuftbildException : Exception
{
    public LuftbildException()
    {
    }

    public LuftbildException(string message)
        : base(message)
    {
    }

    public LuftbildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
