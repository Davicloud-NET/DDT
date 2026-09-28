// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace DDT.E2E;

// DDT.Host built from this repository, on a free localhost port, with its store and SQLite database in directory.
// It issues its certificate from its own root, which the agents pin.
internal sealed partial class HostProcess : IAsyncDisposable
{
    private static readonly TimeSpan s_startTimeout = TimeSpan.FromMinutes(2);

    private readonly Process _process;

    private HostProcess(Process process, OutputLines output, Uri url, string storePath)
    {
        _process = process;
        Output = output;
        Url = url;
        StorePath = storePath;
    }

    public OutputLines Output { get; }

    public Uri Url { get; }

    public string StorePath { get; }

    public string RootCertificatePath => Path.Combine(StorePath, "certs", "ddt-root.pem");

    public string DatabasePath => Path.Combine(StorePath, "ddt-dev.db");

    public string AdministratorPassword { get; private set; } = string.Empty;

    // settings are configuration keys with their values, such as DDT:Deployment:Locale.
    public static async Task<HostProcess> StartAsync(string directory, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!File.Exists(RepositoryPaths.Host))
        {
            throw new FileNotFoundException($"The host was not built at {RepositoryPaths.Host}. Build tests\\DDT.E2E, which builds it.");
        }

        string store = Path.Combine(directory, "store");
        Uri url = new($"https://localhost:{FreePort()}/");

        ProcessStartInfo start = StartInfo(store, url, settings);
        Process process = Process.Start(start) ?? throw new InvalidOperationException($"{RepositoryPaths.Host} did not start.");
        KillOnExitJob.Add(process);
        OutputLines output = new(process, Path.Combine(directory, "host.log"));
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        HostProcess host = new(process, output, url, store);

        try
        {
            host.AdministratorPassword = await host.WaitForStartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return host;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }

        _process.Dispose();
        Output.Dispose();
    }

    private static ProcessStartInfo StartInfo(string store, Uri url, IReadOnlyDictionary<string, string> settings)
    {
        string certificates = Path.Combine(store, "certs");
        ProcessStartInfo start = new(RepositoryPaths.Host)
        {
            WorkingDirectory = Path.GetDirectoryName(RepositoryPaths.Host),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Nothing from the developer's own setup, such as a PostgreSQL connection string, may reach this host.
        foreach (string key in start.Environment.Keys.Where(IsInherited).ToList())
        {
            start.Environment.Remove(key);
        }

        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        start.Environment["DDT__StorePath"] = store;
        start.Environment["Kestrel__Endpoints__Https__Url"] = url.AbsoluteUri.TrimEnd('/');
        start.Environment["Kestrel__Certificates__Default__Path"] = Path.Combine(certificates, "ddt.pem");
        start.Environment["Kestrel__Certificates__Default__KeyPath"] = Path.Combine(certificates, "ddt-key.pem");

        foreach ((string key, string value) in settings)
        {
            start.Environment[key.Replace(":", "__", StringComparison.Ordinal)] = value;
        }

        return start;
    }

    // The host is started once it listens and has logged the first administrator's password, which it returns.
    private Task<string> WaitForStartAsync(CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            new Expectation("The host's start", s_startTimeout, () => Output.Tail()),
            _ =>
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"The host ended with exit code {_process.ExitCode}:{Environment.NewLine}{Output.Tail()}");
                }

                string text = Output.Text;
                Match password = AdministratorPasswordLine().Match(text);

                return Task.FromResult(password.Success && text.Contains("Now listening on", StringComparison.Ordinal) ? password.Groups[1].Value : null);
            },
            cancellationToken);

    private static bool IsInherited(string key) =>
        key.StartsWith("DDT__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("Kestrel__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("ASPNETCORE_", StringComparison.OrdinalIgnoreCase)
        || key.StartsWith("DOTNET_ENVIRONMENT", StringComparison.OrdinalIgnoreCase);

    // Free on the loopback addresses when asked. Kestrel binds localhost to both of them.
    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();

        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    [GeneratedRegex(@"User name admin, password (\S+?)\. ")]
    private static partial Regex AdministratorPasswordLine();
}
