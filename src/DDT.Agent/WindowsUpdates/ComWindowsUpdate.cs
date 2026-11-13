// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsUpdates;

// The Windows Update Agent through its COM API, late-bound. Every call runs on one COM thread, which Dispose ends.
public sealed class ComWindowsUpdate : IWindowsUpdate, IDisposable
{
    private readonly ComThread _thread;
    private readonly WindowsUpdateAgent _agent;

    public ComWindowsUpdate()
    {
        WindowsUpdateAgent? agent = null;
        _thread = new ComThread("Windows Update", () => agent?.Dispose());
        _agent = agent = new WindowsUpdateAgent(_thread.Disposing);
    }

    public Task<bool> IsRestartPendingAsync(CancellationToken cancellationToken) => RunAsync(agent => agent.IsRestartPending(), cancellationToken);

    public Task<bool> IsBusyAsync(CancellationToken cancellationToken) => RunAsync(agent => agent.IsBusy(), cancellationToken);

    public Task<IReadOnlyList<AvailableUpdate>> SearchAsync(WindowsUpdateSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        return RunAsync(agent => agent.Search(search, cancellationToken), cancellationToken);
    }

    public Task AcceptEulaAsync(AvailableUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);

        return RunAsync(
            agent =>
            {
                agent.AcceptEula(update);

                return true;
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<UpdateOutcome>> DownloadAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(percent);

        return RunAsync(agent => agent.Download(updates, percent, cancellationToken), cancellationToken);
    }

    public Task<UpdateInstallation> InstallAsync(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(percent);

        return RunAsync(agent => agent.Install(updates, percent, cancellationToken), cancellationToken);
    }

    public void Dispose() => _thread.Dispose();

    // Waits for the COM thread whatever the token says: a call that can stop watches it there.
    private async Task<T> RunAsync<T>(Func<WindowsUpdateAgent, T> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _thread.RunAsync(() => work(_agent)).ConfigureAwait(false);
        }
        catch (ComCallException exception)
        {
            throw new DeploymentStepException($"Windows Update failed in {exception.Message}.", exception);
        }
    }
}
