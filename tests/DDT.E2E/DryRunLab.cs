// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using DDT.Contracts;
using DDT.Contracts.Accounts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Sequences;
using DDT.Contracts.Settings;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.E2E;

// One host for all tests, with the agent published from this repository, a signed-in administrator, a live
// connection, and the images and packages the sequences use. Everything lives in a temporary directory, which goes
// with the host, the agents and the dry runs' disks when the tests end.
public sealed partial class DryRunLab : IAsyncLifetime
{
    public const string LocalAdministratorPassword = "E2e-Local-Admin-7Qx4";
    public const string JoinPassword = "E2e-Join-Secret-9Kp2";
    public const string AccountPassword = "E2e-Share-Secret-3Vn6";
    public const string Domain = "e2e.ddt.test";

    // What a dry run of this model reports, and what the driver package targets.
    public const string Manufacturer = "DDT";
    public const string Model = "Dry run";

    // Large enough that downloading it through the slow relay outlasts the agent's calls to the server by far: 32 s at
    // 4 MB/s, when the agent calls at least every 10 s.
    private const int LargeMegabytes = 128;
    private const int SlowBytesPerSecond = 4 * 1024 * 1024;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ddt-e2e-{Guid.NewGuid():N}");
    private readonly List<int> _dryRunIds = [];
    private HostProcess? _host;
    private SlowRelay? _relay;
    private AdminApi? _api;
    private LiveRecorder? _live;
    private X509Certificate2? _rootCertificate;
    private string? _agentPath;

    // Set when a prerequisite is missing, which skips every test.
    public string? SkipReason { get; private set; }

    internal HostProcess Host => _host!;

    internal AdminApi Api => _api!;

    internal LiveRecorder Live => _live!;

    public ImageSummary Image { get; private set; } = null!;

    // Another Windows image, of another name, for the branch a dry run does not take.
    public ImageSummary OtherImage { get; private set; } = null!;

    public ImageSummary LargeImage { get; private set; } = null!;

    public PackageSummary Drivers { get; private set; } = null!;

    public PackageSummary Files { get; private set; } = null!;

    public PackageSummary LargeFiles { get; private set; } = null!;

    // A raw disk image whose boot file is not signed for Secure Boot.
    public ImageSummary RawImage { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // Also after a failed start, and more than once.
    public async ValueTask DisposeAsync()
    {
        if (_live is { } live)
        {
            _live = null;
            await live.DisposeAsync().ConfigureAwait(false);
        }

        _api?.Dispose();
        _api = null;

        if (_relay is { } relay)
        {
            _relay = null;
            await relay.DisposeAsync().ConfigureAwait(false);
        }

        if (_host is { } host)
        {
            _host = null;
            await host.DisposeAsync().ConfigureAwait(false);
        }

        _rootCertificate?.Dispose();
        _rootCertificate = null;

        foreach (int id in _dryRunIds)
        {
            await DeleteDirectoryAsync(AgentProcess.RootOf(id)).ConfigureAwait(false);
            File.Delete(AgentProcess.DiskPathOf(id));
        }

        await DeleteDirectoryAsync(_directory).ConfigureAwait(false);
    }

    // A new machine, or with dryRunId the machine an earlier agent stood in for, whose disk it goes on with. With
    // slowDownloads, the agent reaches the host through the slow relay, and with secureBoot the machine says Secure Boot
    // is on.
    internal AgentProcess StartAgent(int? dryRunId = null, bool slowDownloads = false, bool secureBoot = false)
    {
        int id = dryRunId ?? NewDryRunId();
        string log = Path.Combine(_directory, $"agent-{id}-{Environment.TickCount64}.log");

        return AgentProcess.Start(_agentPath!, slowDownloads ? _relay!.Url : Host.Url, Host.RootCertificatePath, id, log, secureBoot);
    }

    internal Task<SequenceView> CreateSequenceAsync(string name, IReadOnlyList<SequenceStep> steps, CancellationToken cancellationToken) =>
        CreateSequenceAsync(name, new SequenceDefinition(SequenceDefinition.CurrentVersion, steps), cancellationToken);

    internal async Task<SequenceView> CreateSequenceAsync(string name, SequenceDefinition definition, CancellationToken cancellationToken)
    {
        SequenceView sequence = await Api.SendAsync(
            HttpMethod.Post,
            "api/sequences",
            new CreateSequenceRequest($"{name} {Guid.NewGuid():N}", "Made by the end-to-end tests.", definition),
            DdtJsonContext.Default.CreateSequenceRequest,
            DdtJsonContext.Default.SequenceView,
            HttpStatusCode.Created,
            cancellationToken).ConfigureAwait(false);

        Assert.True(sequence.Problems.Count == 0, $"{name} has problems: {string.Join(" ", sequence.Problems.Select(problem => problem.Message))}");

        return sequence;
    }

    internal Task<MachineSummary> WaitForMachineAsync(AgentProcess agent, CancellationToken cancellationToken) =>
        Eventually.GetAsync(
            $"The registration of {agent.SerialNumber}",
            TimeSpan.FromMinutes(1),
            async call => (await Api.GetAsync("api/machines", DdtJsonContext.Default.IReadOnlyListMachineSummary, call).ConfigureAwait(false))
                .FirstOrDefault(machine => machine.SerialNumber == agent.SerialNumber),
            () => agent.Output.Tail(),
            cancellationToken);

    internal Task<MachineSummary> ApproveAsync(Guid machineId, Guid? expectedSequenceId, CancellationToken cancellationToken) =>
        Api.SendAsync(
            HttpMethod.Post,
            $"api/machines/{machineId:D}/approve",
            new ApproveMachineRequest(expectedSequenceId),
            DdtJsonContext.Default.ApproveMachineRequest,
            DdtJsonContext.Default.MachineSummary,
            HttpStatusCode.OK,
            cancellationToken);

    internal Task<MachineSummary> AssignAsync(
        Guid machineId,
        Guid sequenceId,
        string? computerName,
        CancellationToken cancellationToken,
        bool allowSecureBootMismatch = false) =>
        Api.SendAsync(
            HttpMethod.Post,
            $"api/machines/{machineId:D}/deployments",
            new AssignSequenceRequest(sequenceId, computerName, allowSecureBootMismatch),
            DdtJsonContext.Default.AssignSequenceRequest,
            DdtJsonContext.Default.MachineSummary,
            HttpStatusCode.OK,
            cancellationToken);

    // As the machine's page continues the pause it shows.
    internal Task<DeploymentView> ContinueAsync(Guid machineId, RunPauseView pause, CancellationToken cancellationToken) =>
        Api.SendAsync(
            HttpMethod.Post,
            $"api/machines/{machineId:D}/deployments/current/continue",
            new ContinueRunRequest(pause.StepId, pause.Pass),
            DdtJsonContext.Default.ContinueRunRequest,
            DdtJsonContext.Default.DeploymentView,
            HttpStatusCode.OK,
            cancellationToken);

    // A stored account with AccountPassword, saved as the Accounts page saves one: with the administrator's password
    // entered again.
    internal Task<AccountView> CreateAccountAsync(string userName, string domain, IReadOnlyList<string> hosts, bool runAs, CancellationToken cancellationToken) =>
        Api.SendReauthenticatedAsync(
            HttpMethod.Post,
            "api/accounts",
            Host.AdministratorPassword,
            new SaveAccountRequest(0, $"E2E account {Guid.NewGuid():N}", userName, domain, hosts, runAs, new SecretUpdate(SecretAction.Set, AccountPassword)),
            DdtJsonContext.Default.SaveAccountRequest,
            DdtJsonContext.Default.AccountView,
            HttpStatusCode.Created,
            cancellationToken);

    internal Task<DeploymentView> RunAsync(Guid runId, CancellationToken cancellationToken) =>
        Api.GetAsync($"api/deployments/{runId:D}", DdtJsonContext.Default.DeploymentView, cancellationToken);

    internal Task<IReadOnlyList<DeploymentSummary>> RunsAsync(Guid machineId, CancellationToken cancellationToken) =>
        Api.GetAsync($"api/machines/{machineId:D}/deployments", DdtJsonContext.Default.IReadOnlyListDeploymentSummary, cancellationToken);

    internal Task<MachineLogPage> LogAsync(Guid machineId, string query, CancellationToken cancellationToken) =>
        Api.GetAsync($"api/machines/{machineId:D}/log?{query}", DdtJsonContext.Default.MachineLogPage, cancellationToken);

    // The audit events about a run, read from the host's database.
    internal async Task<IReadOnlyList<AuditEvent>> AuditAsync(Guid runId, CancellationToken cancellationToken)
    {
        DbContextOptions<DdtDbContext> options = new DbContextOptionsBuilder<DdtDbContext>()
            .UseSqlite($"Data Source={Host.DatabasePath};Mode=ReadOnly;Pooling=False")
            .Options;
        DdtDbContext database = new(options);

        await using (database.ConfigureAwait(false))
        {
            string subject = runId.ToString("D");

            return await database.AuditEvents.AsNoTracking().Where(audit => audit.SubjectId == subject).ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    // What every test ends with. The agents warned of nothing and failed at nothing but what the test expects, the
    // host logged no error, and neither a configured password nor the stored account's, nor the form an answer file
    // gives them, appears in the agents' output, in text the test saw, in the host's log, in what the hub sent, or in
    // the host's database files.
    internal void AssertClean(IReadOnlyList<AgentProcess> agents, IReadOnlyList<string> expectedProblems, params (string Where, string Text)[] seen)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(expectedProblems);

        foreach (AgentProcess agent in agents)
        {
            string[] problems = [.. agent.Output.Lines.Where(line => AgentProblem().IsMatch(line))];
            Assert.True(
                problems.All(line => expectedProblems.Any(expected => line.Contains(expected, StringComparison.Ordinal))),
                $"Agent {agent.DryRunId} warned or failed unexpectedly:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }

        string[] hostErrors = [.. Host.Output.Lines.Where(line => line.StartsWith("fail:", StringComparison.Ordinal) || line.StartsWith("crit:", StringComparison.Ordinal))];
        Assert.True(hostErrors.Length == 0, $"The host logged errors:{Environment.NewLine}{string.Join(Environment.NewLine, hostErrors)}");

        List<(string Where, string Text)> everything =
        [
            .. agents.Select(agent => ($"Agent {agent.DryRunId}'s output", agent.Output.Text)),
            .. seen,
            ("The host's log", Host.Output.Text),
            ("The hub's traffic", Live.Traffic),
        ];

        foreach (string file in (string[])[Host.DatabasePath, Host.DatabasePath + "-wal"])
        {
            if (File.Exists(file))
            {
                using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using MemoryStream copy = new();
                stream.CopyTo(copy);
                everything.Add(($"The database file {Path.GetFileName(file)}", Encoding.Latin1.GetString(copy.ToArray())));
            }
        }

        foreach ((string where, string text) in everything)
        {
            foreach ((string name, string secret) in Secrets())
            {
                Assert.False(text.Contains(secret, StringComparison.Ordinal), $"{where} holds {name}.");
            }
        }
    }

    internal void SkipWhenUnavailable() => Assert.SkipWhen(SkipReason is not null, SkipReason ?? string.Empty);

    // The agent's console lines start with the time and the level.
    [GeneratedRegex(@"^\d\d:\d\d:\d\d (WARN|ERROR) ")]
    private static partial Regex AgentProblem();

    private static IEnumerable<(string Name, string Secret)> Secrets()
    {
        (string Name, string Password)[] passwords =
        [
            ("the local administrator's password", LocalAdministratorPassword),
            ("the join password", JoinPassword),
            ("the stored account's password", AccountPassword),
        ];

        foreach ((string name, string password) in passwords)
        {
            yield return (name, password);

            // Setup reads an answer file's passwords as Base64 of UTF-16 LE text with a suffix.
            yield return ($"{name} as an answer file has it", Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "Password")));
            yield return ($"{name} as an answer file has it", Convert.ToBase64String(Encoding.Unicode.GetBytes(password + "AdministratorPassword")));
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directory);
        string agentDirectory = Path.Combine(_directory, "agent");

        if (await AgentPublisher.PublishAsync(agentDirectory, Path.Combine(_directory, "publish.log"), cancellationToken).ConfigureAwait(false) is { } reason)
        {
            SkipReason = reason;

            return;
        }

        _agentPath = Path.Combine(agentDirectory, AgentPublisher.FileName);
        _host = await HostProcess.StartAsync(
            _directory,
            new Dictionary<string, string>
            {
                // For the slow relay, which the agents reach by address.
                ["DDT:Https:SubjectAlternativeNames"] = "127.0.0.1",
                ["DDT:Deployment:TimeZone"] = "W. Europe Standard Time",
                ["DDT:Deployment:Locale"] = "de-DE",
                ["DDT:Deployment:Keyboard"] = "0407:00000407",
                ["DDT:Deployment:LocalAdministrator:Password"] = LocalAdministratorPassword,
                ["DDT:Deployment:Domain:Name"] = Domain,
                ["DDT:Deployment:Domain:UserName"] = @"E2E\ddt-join",
                ["DDT:Deployment:Domain:Password"] = JoinPassword,
            },
            cancellationToken).ConfigureAwait(false);

        _relay = new SlowRelay(Host.Url, SlowBytesPerSecond);
        _rootCertificate = X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(Host.RootCertificatePath, cancellationToken).ConfigureAwait(false));
        _api = new AdminApi(Host.Url, _rootCertificate);
        await Api.SignInAsync("admin", Host.AdministratorPassword, cancellationToken).ConfigureAwait(false);
        _live = await LiveRecorder.ConnectAsync(Api, _rootCertificate, cancellationToken).ConfigureAwait(false);

        Image = await UploadImageAsync("small.wim", 1, cancellationToken).ConfigureAwait(false);
        OtherImage = await UploadImageAsync("other.wim", 1, cancellationToken, "Other E2E Windows").ConfigureAwait(false);
        LargeImage = await UploadImageAsync("large.wim", LargeMegabytes, cancellationToken).ConfigureAwait(false);

        string drivers = Path.Combine(_directory, "drivers.zip");
        TestContent.WriteDriverPackage(drivers);
        PackageSummary uploaded = await Api.UploadPackageAsync(drivers, UploadKind.Drivers, cancellationToken).ConfigureAwait(false);
        Drivers = await Api.SendAsync(
            HttpMethod.Put,
            $"api/packages/{uploaded.Id:D}",
            new UpdatePackageRequest(uploaded.Name, "Drivers for the dry run's model", [new HardwareModel(Manufacturer, Model)]),
            DdtJsonContext.Default.UpdatePackageRequest,
            DdtJsonContext.Default.PackageSummary,
            HttpStatusCode.OK,
            cancellationToken).ConfigureAwait(false);

        Files = await UploadFilesAsync("files.zip", 0, cancellationToken).ConfigureAwait(false);
        LargeFiles = await UploadFilesAsync("large-files.zip", LargeMegabytes, cancellationToken).ConfigureAwait(false);

        string raw = Path.Combine(_directory, "e2e-cloudimg-amd64.img.gz");
        TestContent.WriteRawImage(raw, 16);
        RawImage = Assert.Single(await Api.UploadImageAsync(raw, cancellationToken).ConfigureAwait(false));
    }

    private async Task<ImageSummary> UploadImageAsync(string name, int megabytes, CancellationToken cancellationToken, string imageName = "DDT E2E Windows")
    {
        string path = Path.Combine(_directory, name);
        TestContent.WriteWim(path, megabytes, imageName);

        return Assert.Single(await Api.UploadImageAsync(path, cancellationToken).ConfigureAwait(false));
    }

    private Task<PackageSummary> UploadFilesAsync(string name, int megabytes, CancellationToken cancellationToken)
    {
        string path = Path.Combine(_directory, name);
        TestContent.WriteFilesPackage(path, megabytes);

        return Api.UploadPackageAsync(path, UploadKind.Files, cancellationToken);
    }

    private int NewDryRunId()
    {
        int id;

        do
        {
            id = Random.Shared.Next(100_000, int.MaxValue);
        }
        while (Directory.Exists(AgentProcess.RootOf(id)) || _dryRunIds.Contains(id));

        _dryRunIds.Add(id);

        return id;
    }

    // A killed process can hold a file for a moment after it ended.
    private static async Task DeleteDirectoryAsync(string path)
    {
        for (int attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (attempt < 20 && exception is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            }
        }
    }
}
