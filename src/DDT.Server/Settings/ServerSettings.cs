// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Data.Common;
using DDT.Contracts.Settings;
using DDT.Pxe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DDT.Server.Settings;

// What configuration alone decides, for the page's read-only server panel. A value is shown only from the list below;
// every other key shows only whether it is set and where. A key whose last segment is Password, Secret, Key or Headers,
// and every connection string, is secret by rule and never shows a value.
public static class ServerSettings
{
    private static readonly string[] s_shown =
    [
        "DDT:Roles",
        "DDT:StorePath",
        "DDT:RequireHttps",
        "Kestrel:Certificates:Default:Path",
        "Kestrel:Certificates:Default:KeyPath",
        "DDT:Pxe:HttpBootPort",
        "DDT:Pxe:BootDirectory",
        "AllowedHosts",
    ];

    private static readonly string[] s_listed =
    [
        "DDT:Https:GenerateSelfSignedCertificate",
        "DDT:Https:SubjectAlternativeNames",
        "Kestrel:Certificates:Default:Password",
        "DDT:Agent:BinaryPath",
        "ASPNETCORE_URLS",
        "Urls",
        "ASPNETCORE_HTTP_PORTS",
        "ASPNETCORE_HTTPS_PORTS",
    ];

    private static readonly string[] s_secretEndings = ["Password", "Secret", "Key", "Headers"];

    public static IReadOnlyList<ServerSetting> Describe(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        List<ServerSetting> settings = [Connection(configuration)];

        settings.AddRange(s_shown.Select(key => Setting(configuration, key, shown: true)));
        settings.AddRange(s_listed.Select(key => Setting(configuration, key, shown: false)));

        // Every endpoint shows its URL. Its other keys, such as a certificate of its own, only show that they are set.
        foreach (IConfigurationSection endpoint in configuration.GetSection("Kestrel:Endpoints").GetChildren())
        {
            foreach (KeyValuePair<string, string?> setting in endpoint.AsEnumerable().Where(setting => setting.Value is not null).OrderBy(setting => setting.Key, StringComparer.Ordinal))
            {
                settings.Add(Setting(configuration, setting.Key, shown: setting.Key.EndsWith(":Url", StringComparison.OrdinalIgnoreCase)));
            }
        }

        foreach (KeyValuePair<string, string?> setting in configuration.AsEnumerable()
            .Where(setting => setting.Key.StartsWith("OTEL_", StringComparison.OrdinalIgnoreCase) && setting.Value is not null)
            .OrderBy(setting => setting.Key, StringComparer.Ordinal))
        {
            settings.Add(string.Equals(setting.Key, "OTEL_EXPORTER_OTLP_ENDPOINT", StringComparison.OrdinalIgnoreCase)
                ? new ServerSetting(setting.Key, WithoutUserInfo(setting.Value!), true, ConfigurationSources.Describe(configuration, setting.Key), false)
                : Setting(configuration, setting.Key, shown: false));
        }

        settings.Add(new ServerSetting("ASPNETCORE_ENVIRONMENT", environment.EnvironmentName, true, null, false));

        return settings;
    }

    // Shown with its default when unset, as the server applies it.
    private static ServerSetting Setting(IConfiguration configuration, string key, bool shown)
    {
        string? value = configuration[key];
        bool secret = IsSecret(key);
        bool isSet = value is not null;

        if (!isSet && shown)
        {
            value = key switch
            {
                "DDT:StorePath" => new Configuration.DdtOptions().StorePath,
                "DDT:RequireHttps" => "true",
                "DDT:Pxe:HttpBootPort" => new PxeOptions().HttpBootPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "DDT:Pxe:BootDirectory" => new PxeOptions().BootDirectory,
                _ => null,
            };
        }

        return new ServerSetting(key, shown && !secret ? value : null, isSet, isSet ? ConfigurationSources.Describe(configuration, key) : null, secret);
    }

    // The provider, host and database of the connection string, and never the rest of it, which holds the password.
    private static ServerSetting Connection(IConfiguration configuration)
    {
        const string Key = "ConnectionStrings:ddtdb";
        string? connection = configuration[Key];

        if (string.IsNullOrWhiteSpace(connection))
        {
            return new ServerSetting(Key, "SQLite in DDT:StorePath, for development only", false, null, true);
        }

        try
        {
            DbConnectionStringBuilder builder = new() { ConnectionString = connection };
            string host = builder.TryGetValue("Host", out object? h) ? $"{h}" : builder.TryGetValue("Server", out object? s) ? $"{s}" : "?";
            string port = builder.TryGetValue("Port", out object? p) ? $":{p}" : string.Empty;
            string database = builder.TryGetValue("Database", out object? d) ? $"{d}" : "?";

            return new ServerSetting(Key, $"PostgreSQL, host {host}{port}, database {database}", true, ConfigurationSources.Describe(configuration, Key), true);
        }
        catch (ArgumentException)
        {
            return new ServerSetting(Key, "PostgreSQL", true, ConfigurationSources.Describe(configuration, Key), true);
        }
    }

    private static bool IsSecret(string key)
    {
        string last = key.Split(':')[^1];

        return s_secretEndings.Any(ending => last.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
            || key.StartsWith("ConnectionStrings:", StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutUserInfo(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && uri.UserInfo.Length > 0
            ? new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString()
            : value;
}
