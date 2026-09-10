using DDT.Server.Configuration;
using DDT.Server.Security;

namespace DDT.Host.Startup;

public static class CertificateBootstrap
{
    public static bool EnsureConfiguredCertificate(IConfiguration configuration, DdtOptions options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Https.GenerateSelfSignedCertificate)
        {
            return false;
        }

        string? certificatePath = configuration["Kestrel:Certificates:Default:Path"];
        string? keyPath = configuration["Kestrel:Certificates:Default:KeyPath"];

        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(keyPath))
        {
            return false;
        }

        string[] names = options.Https.SubjectAlternativeNames
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return ServerCertificateFile.EnsureExists(certificatePath, keyPath, names);
    }
}
