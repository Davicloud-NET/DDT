// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;
using System.Globalization;

namespace DDT.E2E;

// The published agent in a dry run as machine DRYRUN-<id>. It keeps the machine's disk under Root.
// Its input is redirected, so nobody can type at it. That's why machines are authorized on the web.
internal sealed class AgentProcess : IAsyncDisposable
{
    private readonly Process _process;

    private AgentProcess(Process process, OutputLines output, int dryRunId)
    {
        _process = process;
        Output = output;
        DryRunId = dryRunId;
    }

    public OutputLines Output { get; }

    public int DryRunId { get; }

    public string SerialNumber => $"DRYRUN-{DryRunId}";

    // The dry run's disk. It outlasts the process, so an agent started again with the same id continues its run.
    public string Root => RootOf(DryRunId);

    // Where a sequence that writes a raw disk image writes the dry run's disk. It outlasts the root.
    public string DiskPath => DiskPathOf(DryRunId);

    public static string RootOf(int dryRunId) => Path.Combine(Path.GetTempPath(), $"ddt-dry-run-{dryRunId}");

    public static string DiskPathOf(int dryRunId) => $"{RootOf(dryRunId)}-disk0.img";

    public static AgentProcess Start(AgentStartInfo agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        string[] arguments =
        [
            "--dry-run",
            "--dry-run-id",
            agent.DryRunId.ToString(CultureInfo.InvariantCulture),
            "--server",
            agent.Server.AbsoluteUri,
            "--root-certificate",
            agent.RootCertificatePath,
            .. agent.SecureBoot ? (string[])["--dry-run-secure-boot"] : [],
        ];
        ProcessStartInfo start = new(agent.AgentPath, arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(agent.AgentPath),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        Process process = Process.Start(start) ?? throw new InvalidOperationException($"{agent.AgentPath} did not start.");
        KillOnExitJob.Add(process);
        OutputLines output = new(process, agent.LogPath);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        return new AgentProcess(process, output, agent.DryRunId);
    }

    // Returns while the file with this hash is downloading into the run's cache, in either phase. The step that
    // downloads it runs until the download is done. Polled often, so even a quick download is seen.
    public async Task WaitForDownloadAsync(string sha256, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string part = Path.Combine(Root, "W", "DDT", "cache", $"{sha256}.part");

        await Eventually.WaitAsync(
            new Expectation($"Agent {DryRunId}'s download of {part}", timeout, () => Output.Tail())
            {
                Interval = TimeSpan.FromMilliseconds(5),
            },
            _ => Task.FromResult(File.Exists(part) || _process.HasExited),
            cancellationToken).ConfigureAwait(false);

        if (_process.HasExited)
        {
            throw new InvalidOperationException($"Agent {DryRunId} ended before it downloaded {part}:{Environment.NewLine}{Output.Tail()}");
        }
    }

    public async Task<int> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);

        try
        {
            await _process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Agent {DryRunId} did not end within {timeout.TotalSeconds:0} s:{Environment.NewLine}{Output.Tail()}");
        }

        // The asynchronous readers may still hold the last lines; this waits for them.
        _process.WaitForExit();

        return _process.ExitCode;
    }

    // Kills the agent like a power loss would. None of the agent's code gets to run after this.
    public async Task KillAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        _process.WaitForExit();
    }

    public async ValueTask DisposeAsync()
    {
        await KillAsync().ConfigureAwait(false);
        _process.Dispose();
        Output.Dispose();
    }
}
