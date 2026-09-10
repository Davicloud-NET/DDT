using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DDT.Server.Security;

// Kestrel reads the certificate from disk, so a fresh deployment needs a certificate to exist
// before the first request. A generated one is a starting point, not a recommendation: it is
// self signed, so operators are expected to replace it or distribute it as a trusted root.
public static class ServerCertificateFile
{
    public static bool EnsureExists(string certificatePath, string keyPath, IEnumerable<string> subjectAlternativeNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        if (File.Exists(certificatePath) && File.Exists(keyPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

        using RSA key = RSA.Create(3072);
        CertificateRequest request = new("CN=DDT", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], true));
        request.CertificateExtensions.Add(BuildSubjectAlternativeNames(subjectAlternativeNames));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddYears(2));

        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        SetOwnerOnlyPermissions(keyPath);

        return true;
    }

    private static X509Extension BuildSubjectAlternativeNames(IEnumerable<string> configured)
    {
        SubjectAlternativeNameBuilder builder = new();
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) { "localhost", Dns.GetHostName() };

        foreach (string name in configured)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name.Trim());
            }
        }

        foreach (string name in names)
        {
            if (IPAddress.TryParse(name, out IPAddress? address))
            {
                builder.AddIpAddress(address);
            }
            else
            {
                builder.AddDnsName(name);
            }
        }

        foreach (IPAddress address in LocalAddresses())
        {
            builder.AddIpAddress(address);
        }

        return builder.Build();
    }

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        IPAddress[] addresses;

        try
        {
            addresses = Dns.GetHostAddresses(Dns.GetHostName());
        }
        catch (SocketException)
        {
            return [];
        }

        return addresses.Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
    }

    private static void SetOwnerOnlyPermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
