using System.Security.Cryptography;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// The host's long-lived key: one per install, kept next to the app's other files. Every share
/// signs its new certificate with it, so joined clients — which pin the key, see
/// <see cref="HostKennung"/> — recognise the host across shares, incidents and restarts.
/// </summary>
public static class HostIdentity
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Loads the key at <paramref name="path"/>, or creates and stores one when the file is missing
    /// or does not hold a P-256 private key. A replaced key gives the host a new Kennung, and its
    /// clients will ask to compare it — which is correct, because they cannot tell it from another
    /// device. An I/O failure reading or writing the file is not caught; starting the share fails
    /// with it.
    /// </summary>
    public static ECDsa LoadOrCreate(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (TryReadKey(path, out var existing))
        {
            return CreateKey(existing);
        }

        var created = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            if (TryPersist(path, created.ExportPkcs8PrivateKeyPem()))
            {
                return created;
            }
        }
        catch
        {
            created.Dispose();
            throw;
        }

        // Two instances sharing for the first time at once: the one that lands second keeps the key
        // already on disk, so both serve the one key their clients will pin.
        created.Dispose();
        return TryReadKey(path, out var stored)
            ? CreateKey(stored)
            : throw new IOException($"Der Schlüssel des Hosts in {path} ist nicht lesbar.");
    }

    // The key object is built here, at the one place that returns it, from parameters that are no
    // longer needed afterwards: the private scalar is not left lying in managed memory.
    private static ECDsa CreateKey(ECParameters parameters)
    {
        try
        {
            return ECDsa.Create(parameters);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(parameters.D);
        }
    }

    // False for a missing file, and for one that holds no usable key: garbage, a public key alone, or
    // another curve all import without complaint, yet only a P-256 private key can sign a certificate.
    // Parameters rather than a key object, so nothing disposable leaves this method.
    private static bool TryReadKey(string path, out ECParameters parameters)
    {
        parameters = default;
        if (!File.Exists(path))
        {
            return false;
        }

        using (var candidate = ECDsa.Create())
        {
            try
            {
                candidate.ImportFromPem(File.ReadAllText(path));
                parameters = candidate.ExportParameters(includePrivateParameters: true);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                return false;
            }
        }

        if (!string.Equals(parameters.Curve.Oid.Value, ECCurve.NamedCurves.nistP256.Oid.Value, StringComparison.Ordinal))
        {
            CryptographicOperations.ZeroMemory(parameters.D);
            return false;
        }

        // A key file restored from a backup or copied between machines may have come back readable
        // by others; the key is only as private as the file.
        if (!OperatingSystem.IsWindows() && File.GetUnixFileMode(path) != OwnerOnly)
        {
            File.SetUnixFileMode(path, OwnerOnly);
        }

        return true;
    }

    // Written to a sibling of its own and moved into place without overwriting, so a crash mid-write
    // cannot leave half a key, and a concurrent first share cannot swap the key under the other. On
    // Unix the sibling is created owner-only (CreateNew: never an existing file that keeps its old
    // mode, never through a symlink); on Windows the per-user AppData folder's ACL restricts it.
    // False when another instance stored its key first.
    private static bool TryPersist(string path, string pem)
    {
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = OwnerOnly;
            }

            using (var writer = new StreamWriter(new FileStream(tmp, options)))
            {
                writer.Write(pem);
            }

            // Only reached when the file at path held no usable key; clear it so the move below can
            // refuse to overwrite a key another instance stored in the meantime.
            File.Delete(path);
            try
            {
                File.Move(tmp, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
