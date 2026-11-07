// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

public sealed record AgentOptions(
    Uri ServerUrl,
    X509Certificate2? RootCertificate,
    bool DryRun,
    int DryRunId,
    bool NoUpdate,
    string? KeyboardLayout)
{
    // Frozen. An agent from an older boot image passes this as the last argument to the newer agent it starts.
    public const string NoUpdateArgument = "--no-update";

    // Frozen too. An agent passes it before NoUpdateArgument, followed by the path of the console the server offers.
    public const string ConsoleArgument = "--console";

    public const string Usage =
        "Usage: ddt-agent [--config <agent.json>] [--server <https url>] [--root-certificate <pem file>] " +
        "[--no-update] [--console <ddt-console.exe>] [--dry-run [--dry-run-id <number>] [--dry-run-secure-boot] " +
        "[--dry-run-environment <WindowsPE|LinuxBoot>] [--dry-run-netboot <OnceInLinux|OnceBeforeLinux|Always>]], or ddt-agent --licenses";

    public const string DryRunEnvironmentArgument = "--dry-run-environment";

    public const string DryRunNetbootArgument = "--dry-run-netboot";

    // The fake machine of a dry run says that Secure Boot is on.
    public bool DryRunSecureBoot { get; init; }

    // The boot environment the fake machine of a dry run started. Null is the one this agent is built for.
    public AgentEnvironment? DryRunEnvironment { get; init; }

    // Whether the fake machine of a dry run starts from the network again after a hand-over to the installed Linux.
    public DryRunNetboot DryRunNetboot { get; init; }

    // Starts this console instead of the ddt-console.exe beside the agent, even in a dry run or with input redirected.
    public string? ConsolePath { get; init; }

    // The address agent.json gives for the server beside its name. Null when it gives none.
    public IPAddress? ServerAddress { get; init; }

    // The agent.json a hand-over stages for the installed system. It only holds what it takes to reach the server.
    public AgentConfiguration ForInstalledSystem() => new(ServerUrl.AbsoluteUri, RootCertificate?.ExportCertificatePem(), null);

    // Arguments override agent.json, which by default sits next to the executable.
    public static bool TryParse(IReadOnlyList<string> args, out AgentOptions? options, out string error) =>
        TryParse(args, AgentEnvironment.WindowsPE, out options, out error);

    // bootEnvironment is the one the executable is built for. Its usage text ends every refusal, and an executable
    // for Linux has no dry run of Windows PE.
    public static bool TryParse(IReadOnlyList<string> args, AgentEnvironment bootEnvironment, out AgentOptions? options, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        options = null;
        Arguments parsed = new(bootEnvironment);

        if (!TryParseArguments(args, parsed, out error)
            || !parsed.TryCheckDryRun(out error)
            || !TryApplyConfigFile(parsed, out error)
            || !TryReadServerUrl(parsed, out Uri? serverUrl, out error)
            || !TryLoadRoot(parsed.RootPem, out X509Certificate2? root, out error))
        {
            return false;
        }

        options = new AgentOptions(serverUrl, root, parsed.DryRun, parsed.DryRunId, parsed.NoUpdate, parsed.KeyboardLayout)
        {
            DryRunSecureBoot = parsed.DryRunSecureBoot,
            DryRunEnvironment = parsed.DryRunEnvironment,
            DryRunNetboot = parsed.DryRunNetboot,
            ConsolePath = parsed.ConsolePath,
            ServerAddress = parsed.ServerAddress,
        };

        return true;
    }

    private static bool TryParseArguments(IReadOnlyList<string> args, Arguments parsed, out string error)
    {
        for (int index = 0; index < args.Count; index++)
        {
            string argument = args[index];

            if (parsed.TrySetFlag(argument))
            {
                continue;
            }

            if (index + 1 >= args.Count)
            {
                error = $"{argument} needs a value. {parsed.Usage}";

                return false;
            }

            if (!TrySetValue(parsed, argument, args[++index], out error))
            {
                return false;
            }
        }

        error = string.Empty;

        return true;
    }

    private static bool TrySetValue(Arguments parsed, string argument, string value, out string error)
    {
        error = string.Empty;

        switch (argument)
        {
            case "--config":
                parsed.ConfigPath = value;

                return true;
            case "--server":
                parsed.Server = value;

                return true;
            case ConsoleArgument:
                parsed.ConsolePath = value;

                return true;
            case "--root-certificate":
                bool read = TryReadText(value, out string? pem, out error);
                parsed.RootPem = pem;

                return read;
            case "--dry-run-id" when int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int id):
                parsed.DryRunId = id;

                return true;
            case DryRunEnvironmentArgument when BootEnvironment(value) is { } environment && parsed.CanDryRun(environment):
                parsed.DryRunEnvironment = environment;

                return true;
            case DryRunNetbootArgument when Netboot(value) is { } netboot:
                parsed.DryRunNetboot = netboot;

                return true;
            default:
                error = $"Unknown or invalid argument {argument}. {parsed.Usage}";

                return false;
        }
    }

    // A run starts in a boot environment, so an installed system is none a dry run can start.
    private static AgentEnvironment? BootEnvironment(string name) =>
        Enum.GetValues<AgentEnvironment>()
            .Where(environment => SequenceKinds.IsBoot(SequenceKinds.Phase(environment)))
            .Cast<AgentEnvironment?>()
            .FirstOrDefault(environment => string.Equals(environment.ToString(), name, StringComparison.OrdinalIgnoreCase));

    // By its name, as the usage spells it. A number is none.
    private static DryRunNetboot? Netboot(string name) =>
        Enum.GetValues<DryRunNetboot>()
            .Cast<DryRunNetboot?>()
            .FirstOrDefault(netboot => string.Equals(netboot.ToString(), name, StringComparison.OrdinalIgnoreCase));

    // Only the default agent.json may be missing. A file named on the command line must exist.
    private static bool TryApplyConfigFile(Arguments parsed, out string error)
    {
        string defaultConfigPath = Path.Combine(AppContext.BaseDirectory, "agent.json");
        error = string.Empty;

        if (parsed.ConfigPath is null && !File.Exists(defaultConfigPath))
        {
            return true;
        }

        string configPath = parsed.ConfigPath ?? defaultConfigPath;

        if (!TryReadText(configPath, out string? json, out error))
        {
            return false;
        }

        try
        {
            AgentConfiguration? file = JsonSerializer.Deserialize(json, AgentConfigurationJsonContext.Default.AgentConfiguration);

            parsed.Server ??= file?.ServerUrl;
            parsed.RootPem ??= file?.RootCertificate;
            parsed.KeyboardLayout = file?.KeyboardLayout;
            parsed.ServerAddress = IPAddress.TryParse(file?.ServerAddress, out IPAddress? address) ? address : null;

            return true;
        }
        catch (JsonException exception)
        {
            error = $"{configPath} is not valid: {exception.Message}";

            return false;
        }
    }

    private static bool TryReadServerUrl(Arguments parsed, [NotNullWhen(true)] out Uri? serverUrl, out string error)
    {
        if (!Uri.TryCreate(parsed.Server, UriKind.Absolute, out serverUrl) || serverUrl.Scheme != Uri.UriSchemeHttps)
        {
            serverUrl = null;
            error = $"An https server URL is required. {parsed.Usage}";

            return false;
        }

        error = string.Empty;

        return true;
    }

    private static bool TryLoadRoot(string? pem, out X509Certificate2? root, out string error)
    {
        root = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(pem))
        {
            return true;
        }

        try
        {
            root = X509Certificate2.CreateFromPem(pem);

            return true;
        }
        catch (CryptographicException exception)
        {
            error = $"The root certificate is not a PEM certificate: {exception.Message}";

            return false;
        }
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

    // What the command line and agent.json say, before it is checked.
    private sealed class Arguments(AgentEnvironment bootEnvironment)
    {
        public string Usage { get; } = EnvironmentTexts.Usage(bootEnvironment);

        public string? ConfigPath { get; set; }

        public string? Server { get; set; }

        public string? RootPem { get; set; }

        public string? KeyboardLayout { get; set; }

        public IPAddress? ServerAddress { get; set; }

        public string? ConsolePath { get; set; }

        public bool DryRun { get; private set; }

        public bool DryRunSecureBoot { get; private set; }

        public bool NoUpdate { get; private set; }

        public int DryRunId { get; set; } = 1;

        public AgentEnvironment? DryRunEnvironment { get; set; }

        public DryRunNetboot DryRunNetboot { get; set; }

        // Both only say what a dry run's machine does.
        public bool TryCheckDryRun(out string error)
        {
            string? alone = DryRun ? null : DryRunEnvironment is not null ? DryRunEnvironmentArgument : DryRunNetboot != DryRunNetboot.Never ? DryRunNetbootArgument : null;
            error = alone is null ? string.Empty : $"{alone} goes with --dry-run. {Usage}";

            return error.Length == 0;
        }

        // The Windows executable stands in for both boot environments, the one for Linux only for its own.
        public bool CanDryRun(AgentEnvironment environment) => bootEnvironment == AgentEnvironment.WindowsPE || environment == bootEnvironment;

        // False for an argument that takes a value.
        public bool TrySetFlag(string argument)
        {
            switch (argument)
            {
                case "--dry-run":
                    DryRun = true;

                    return true;
                case "--dry-run-secure-boot":
                    DryRunSecureBoot = true;

                    return true;
                case NoUpdateArgument:
                    NoUpdate = true;

                    return true;
                default:
                    return false;
            }
        }
    }
}
