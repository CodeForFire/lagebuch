using System.Security.Cryptography;

namespace LageBuch.Sync.Hosting.Tests;

public sealed class HostIdentityTests : IDisposable
{
    private readonly TempDirectory _dir = new();

    private string KeyPath => Path.Join(_dir.Path, "host-key.pem");

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_missing_key_is_created_and_the_same_key_is_loaded_again()
    {
        using var created = HostIdentity.LoadOrCreate(KeyPath);
        using var loaded = HostIdentity.LoadOrCreate(KeyPath);

        Assert.True(File.Exists(KeyPath));
        Assert.Equal(created.ExportSubjectPublicKeyInfo(), loaded.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void A_corrupt_key_file_is_replaced_by_a_new_key()
    {
        File.WriteAllText(KeyPath, "-----BEGIN PRIVATE KEY-----\nbm90IGEga2V5\n-----END PRIVATE KEY-----\n");

        using var key = HostIdentity.LoadOrCreate(KeyPath);
        using var reloaded = HostIdentity.LoadOrCreate(KeyPath);

        Assert.Equal(key.ExportSubjectPublicKeyInfo(), reloaded.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void Creating_the_key_writes_atomically_and_leaves_no_tmp_sibling()
    {
        using var key = HostIdentity.LoadOrCreate(KeyPath);

        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void A_public_key_alone_is_replaced_by_a_private_key()
    {
        // Imports without complaint, and then every share would fail signing its certificate.
        using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(KeyPath, other.ExportSubjectPublicKeyInfoPem());
        }

        using var key = HostIdentity.LoadOrCreate(KeyPath);

        using var cert = SyncCertificate.Generate(key).Cert;
        Assert.True(cert.HasPrivateKey);
    }

    [Fact]
    public void A_key_on_another_curve_is_replaced_by_a_p256_key()
    {
        using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP384))
        {
            File.WriteAllText(KeyPath, other.ExportPkcs8PrivateKeyPem());
        }

        using var key = HostIdentity.LoadOrCreate(KeyPath);

        Assert.Equal(256, key.KeySize);
    }

    [Fact]
    public void A_key_file_readable_by_others_is_made_owner_only_when_loaded()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // the per-user AppData ACL does this job on Windows; there is no Unix mode to check
        }

        using (HostIdentity.LoadOrCreate(KeyPath))
        {
            File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        using var loaded = HostIdentity.LoadOrCreate(KeyPath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    [Fact]
    public void The_key_file_is_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // the per-user AppData ACL does this job on Windows; there is no Unix mode to check
        }

        using var key = HostIdentity.LoadOrCreate(KeyPath);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyPath));
    }

    [Fact]
    public void Two_certificates_from_one_key_share_the_pin_and_the_kennung()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var a = SyncCertificate.Generate(key).Cert;
        using var b = SyncCertificate.Generate(key).Cert;

        Assert.NotEqual(a.GetCertHash(HashAlgorithmName.SHA256), b.GetCertHash(HashAlgorithmName.SHA256));
        Assert.Equal(HostKennung.Pin(a), HostKennung.Pin(b));
        Assert.Equal(HostKennung.Of(a), HostKennung.Of(b));
    }
}
