using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TriviaBattle.Server.Configuration;

/// <summary>
/// The certificate for the server's https address (setting "HttpsPort"). Station screens on other PCs need
/// https to use a camera (hero photos): browsers only give cameras to https or localhost pages. The server
/// makes it itself (self-signed), for this PC's name and network addresses, and keeps it in
/// certs/lan-https.pfx so it stays the same across restarts. Each station's browser warns about it once
/// ("Your connection is not private": Advanced, then Proceed) and remembers. Delete the file to make a new
/// one, e.g. when the PC's address has changed. The file holds a private key: it's never committed.
/// </summary>
public static class LanCertificate
{
    public static X509Certificate2 LoadOrCreate(string contentRoot)
    {
        var path = Path.Combine(contentRoot, "certs", "lan-https.pfx");
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Create());
        }
        // Loaded from the file even when just made: on Windows a key made in memory can't serve https.
        return X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
    }

    /// <summary>This PC's IPv4 addresses on the network cards that are up (what other PCs connect to).</summary>
    public static IReadOnlyList<IPAddress> LocalAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .ToList();

    private static byte[] Create()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=Trivia Battle ({Environment.MachineName})", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        names.AddIpAddress(IPAddress.Loopback);
        foreach (var address in LocalAddresses())
            names.AddIpAddress(address);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(2));
        return certificate.Export(X509ContentType.Pfx);
    }
}
