using System.Security.Cryptography;

namespace LageBuch.Sync.Hosting.Tests;

public class SyncCertificateTests
{
    [Fact]
    public void Generate_returns_a_self_signed_cert_and_its_sha256_thumbprint()
    {
        var (cert, thumbprint) = SyncCertificate.Generate();

        Assert.NotNull(cert);
        Assert.False(cert.NotBefore > DateTimeOffset.Now);
        Assert.True(cert.NotAfter >= DateTimeOffset.Now.AddHours(23));
        Assert.Equal(cert.GetCertHash(HashAlgorithmName.SHA256), Convert.FromHexString(thumbprint));
        cert.Dispose();
    }

    [Fact]
    public void Two_generations_produce_distinct_certs()
    {
        using var a = SyncCertificate.Generate().Cert;
        using var b = SyncCertificate.Generate().Cert;
        Assert.NotEqual(a.Thumbprint, b.Thumbprint);
        Assert.NotEqual(a.GetCertHash(), b.GetCertHash());
    }
}
