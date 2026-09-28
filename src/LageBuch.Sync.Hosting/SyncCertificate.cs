using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace LageBuch.Sync.Hosting;

/// <summary>
/// Generates the self-signed certificate the sync host serves over TLS (§ P0 #2). A new certificate
/// is minted per share session and discarded on stop; the key it carries is the host's persistent
/// <see cref="HostIdentity"/>, which is what clients pin via Trust-on-First-Use.
/// </summary>
public static class SyncCertificate
{
    /// <summary>
    /// Creates a fresh self-signed X.509 certificate with a throwaway key, valid for approximately 24 hours.
    /// </summary>
    /// <returns>A tuple containing the certificate and its uppercase hex SHA-256 thumbprint.</returns>
    public static (X509Certificate2 Cert, string Thumbprint) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Generate(key);
    }

    /// <summary>
    /// Creates a fresh self-signed X.509 certificate for <paramref name="key"/>, valid for approximately
    /// 24 hours. The certificate holds its own copy of the key; the caller still owns <paramref name="key"/>.
    /// </summary>
    /// <returns>A tuple containing the certificate and its uppercase hex SHA-256 thumbprint.</returns>
    public static (X509Certificate2 Cert, string Thumbprint) Generate(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // A fixed subject tells a joining client this host shows its Kennung (see HostKennung); the
        // certificates still differ per share by their random serial number and validity.
        var request = new CertificateRequest(HostKennung.CertificateSubject, key, HashAlgorithmName.SHA256);

        // Identify the loopback endpoints the host actually serves, so the certificate is a complete TLS
        // server certificate rather than a bare self-signed one.
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        // Windows' Schannel refuses to finish a TLS server handshake with a certificate that lacks the
        // Server Authentication EKU; Linux/OpenSSL tolerates the omission. The client pins the key via
        // TOFU and accepts any certificate on the first connect, so this only needs to satisfy the server-side TLS
        // stack, not the client's trust decision.
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                critical: false));

        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.Now, DateTimeOffset.Now.AddHours(24));

        // CreateSelfSigned's private key is ephemeral (in-memory CNG on Windows, not a persisted key
        // container). Kestrel's Windows (Schannel) TLS server handshake can't use that key as-is and
        // aborts the connection before the client even sees an alert — round-tripping through PFX
        // gives the cert a private key SslStream can actually use for the handshake on every platform.
        var cert = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null);
        var thumbprint = Convert.ToHexString(cert.GetCertHash(HashAlgorithmName.SHA256));
        return (cert, thumbprint);
    }
}
