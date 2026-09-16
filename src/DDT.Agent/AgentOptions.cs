using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace DDT.Agent;

public sealed record AgentOptions(
    Uri ServerUrl,
    string EnrollmentToken,
    X509Certificate2? RootCertificate,
    bool DryRun,
    int DryRunId)
{
    public const string Usage =
        "Usage: ddt-agent [--config <agent.json>] [--server <https url>] [--enrollment-token <token>] " +
        "[--root-certificate <pem file>] [--dry-run [--dry-run-id <number>]]";

    // Arguments override agent.json, which by default sits next to the executable.
    public static bool TryParse(IReadOnlyList<string> args, out AgentOptions? options, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        string? configPath = null;
        string? server = null;
        string? token = null;
        string? rootPem = null;
        bool dryRun = false;
        int dryRunId = 1;

        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];

            if (argument == "--dry-run")
            {
                dryRun = true;
                continue;
            }

            if (index + 1 >= args.Count)
            {
                error = $"{argument} needs a value. {Usage}";

                return false;
            }

            string value = args[++index];

            switch (argument)
            {
                case "--config":
                    configPath = value;
                    break;
                case "--server":
                    server = value;
                    break;
                case "--enrollment-token":
                    token = value;
                    break;
                case "--root-certificate":
                    if (!TryReadText(value, out rootPem, out error))
                    {
                        return false;
                    }

                    break;
                case "--dry-run-id" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id):
                    dryRunId = id;
                    break;
                default:
                    error = $"Unknown or invalid argument {argument}. {Usage}";

                    return false;
            }
        }

        // Only the default agent.json may be missing; a file named on the command line must exist.
        string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "agent.json");

        if (configPath is not null || File.Exists(defaultConfigPath))
        {
            configPath ??= defaultConfigPath;

            if (!TryReadText(configPath, out string? json, out error))
            {
                return false;
            }

            try
            {
                AgentConfiguration? file = JsonSerializer.Deserialize(
                    json,
                    AgentConfigurationJsonContext.Default.AgentConfiguration);

                server ??= file?.ServerUrl;
                token ??= file?.EnrollmentToken;
                rootPem ??= file?.RootCertificate;
            }
            catch (JsonException exception)
            {
                error = $"{configPath} is not valid: {exception.Message}";

                return false;
            }
        }

        if (!Uri.TryCreate(server, UriKind.Absolute, out Uri? serverUrl) || serverUrl.Scheme != Uri.UriSchemeHttps)
        {
            error = $"An https server URL is required. {Usage}";

            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            error = $"An enrollment token is required. {Usage}";

            return false;
        }

        // The token goes into an Authorization header, which throws on a stray line break from a
        // hand-edited agent.json.
        if (!token.All(character => character is > ' ' and <= '~'))
        {
            error = "The enrollment token contains whitespace or characters that are not printable ASCII.";

            return false;
        }

        X509Certificate2? root = null;

        if (!string.IsNullOrWhiteSpace(rootPem))
        {
            try
            {
                root = X509Certificate2.CreateFromPem(rootPem);
            }
            catch (CryptographicException exception)
            {
                error = $"The root certificate is not a PEM certificate: {exception.Message}";

                return false;
            }
        }

        options = new AgentOptions(serverUrl, token, root, dryRun, dryRunId);
        error = string.Empty;

        return true;
    }

    private static bool TryReadText(string path, [NotNullWhen(true)] out string? text, out string error)
    {
        try
        {
            text = File.ReadAllText(path);
            error = string.Empty;

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            text = null;
            error = $"Cannot read {path}: {exception.Message}";

            return false;
        }
    }
}
