using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// Generates the ephemeral self-signed certificate the sync host serves over TLS (§ P0 #2). A new
/// certificate is minted per share session and discarded on stop; clients pin it via Trust-on-First-Use.
/// </summary>
public static class SyncCertificate
{
    /// <summary>
    /// Creates a fresh self-signed X.509 certificate valid for approximately 24 hours.
    /// </summary>
    /// <returns>A tuple containing the certificate and its uppercase hex SHA-256 thumbprint.</returns>
    public static (X509Certificate2 Cert, string Thumbprint) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(
            $"CN=LageBuch-Sync-{Guid.NewGuid():N}", key, HashAlgorithmName.SHA256);

        // Identify the loopback endpoints the host actually serves, so the certificate is a complete TLS
        // server certificate rather than a bare self-signed one.
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        // Windows' Schannel refuses to finish a TLS server handshake with a certificate that lacks the
        // Server Authentication EKU; Linux/OpenSSL tolerates the omission. The client pins via TOFU and
        // accepts any certificate on the first connect, so this only needs to satisfy the server-side TLS
        // stack, not the client's trust decision.
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));

        var cert = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24));
        var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
        return (cert, thumbprint);
    }
}
