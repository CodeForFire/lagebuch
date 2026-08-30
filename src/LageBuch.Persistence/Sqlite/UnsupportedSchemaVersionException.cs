namespace LageBuch.Persistence.Sqlite;

/// <summary>
/// Thrown when a .fwincident file carries a schema version this build does not know yet, i.e. it
/// was last written by a newer version of the app.
///
/// Distinct from a generic failure on purpose: the file is intact and the user's answer is "open
/// it with the newer build", not "your Einsatz is damaged" -- so the UI can say so.
/// </summary>
public sealed class UnsupportedSchemaVersionException : Exception
{
    public UnsupportedSchemaVersionException(int fileVersion, int supportedVersion)
        : base($"Diese Einsatzdatei wurde mit einer neueren Version des Programms gespeichert " +
               $"(Dateiversion {fileVersion}, unterstützt bis {supportedVersion}). " +
               $"Bitte die neuere Version verwenden.")
    {
        FileVersion = fileVersion;
        SupportedVersion = supportedVersion;
    }

    public UnsupportedSchemaVersionException() : this(0, 0) { }

    public UnsupportedSchemaVersionException(string message) : base(message) { }

    public UnsupportedSchemaVersionException(string message, Exception innerException) : base(message, innerException) { }

    public int FileVersion { get; }

    public int SupportedVersion { get; }
}
