namespace LageBuch.AppLogic.Services.Dispatch;

/// <summary>
/// This machine's alarm folder and the alarms already answered there. Per machine, not part of the
/// Stammdaten: the folder is a path on this PC, and whether an alarm was taken up is a question
/// this PC asked.
/// </summary>
public interface IDispatchInboxStore
{
    /// <summary>The folder alarm mails are dropped into, or null when none is set.</summary>
    string? GetFolder();

    /// <summary>Sets the folder; null switches alarm import off.</summary>
    void SetFolder(string? folder);

    /// <summary>The <see cref="DispatchAlarm.Key"/>s already answered, by Anlegen or Verwerfen.</summary>
    IReadOnlySet<string> GetHandled();

    /// <summary>Records an alarm as answered, so it is never offered again.</summary>
    void MarkHandled(string key, DateTimeOffset at);
}
