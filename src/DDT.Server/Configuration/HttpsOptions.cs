namespace DDT.Server.Configuration;

public sealed class HttpsOptions
{
    // The agent pins the DDT certificate chain and validates the hostname, so every name and
    // address the server is reached by has to be in the certificate.
    public string SubjectAlternativeNames { get; init; } = string.Empty;

    public bool GenerateSelfSignedCertificate { get; init; } = true;
}
