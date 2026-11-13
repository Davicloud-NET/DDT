// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsUpdates;

// What the COM thread keeps of Windows Update between the seam's calls: one session, its installer and the updates
// of the last search. Every member runs on the COM thread.
internal sealed class WindowsUpdateAgent(CancellationToken disposing) : IDisposable
{
    private readonly FoundUpdates _found = new();
    private AutomationObject? _session;
    private AutomationObject? _installer;

    private AutomationObject Session => _session ??= OpenSession();

    private AutomationObject Installer => _installer ??= Session.CallForObject("CreateUpdateInstaller");

    public bool IsRestartPending()
    {
        using AutomationObject information = AutomationObject.Create(WindowsUpdateIds.SystemInformation);

        return information.GetBool("RebootRequired") || Installer.GetBool("RebootRequiredBeforeInstallation");
    }

    public bool IsBusy() => Installer.GetBool("IsBusy");

    public IReadOnlyList<AvailableUpdate> Search(WindowsUpdateSearch search, CancellationToken stop)
    {
        _found.Dispose();

        if (search.MicrosoftUpdate)
        {
            MicrosoftUpdateService.Register();
        }

        using AutomationObject searcher = Session.CallForObject("CreateUpdateSearcher");
        searcher.Put("ClientApplicationID", AutomationValues.Text(WindowsUpdateIds.ClientApplication));

        if (search.MicrosoftUpdate)
        {
            searcher.Put("ServerSelection", AutomationValues.Int(WindowsUpdateIds.OtherServer));
            searcher.Put("ServiceID", AutomationValues.Text(WindowsUpdateIds.MicrosoftUpdate));
        }

        using AutomationObject result = new UpdateJob(searcher, "Search", disposing).Run(stop, WindowsUpdateIds.Criteria);
        int code = result.GetInt("ResultCode");

        if (code is not (OperationResults.Succeeded or OperationResults.SucceededWithErrors))
        {
            throw new DeploymentStepException($"Windows Update's search ended with result code {code}.");
        }

        using AutomationObject? updates = result.GetObject("Updates");

        return Keep(UpdateReader.Items(updates));
    }

    public void AcceptEula(AvailableUpdate update) => _found.Of(update).Call("AcceptEula")?.Dispose();

    public IReadOnlyList<UpdateOutcome> Download(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken stop)
    {
        using AutomationObject collection = _found.Collection(updates);
        using AutomationObject downloader = Session.CallForObject("CreateUpdateDownloader");
        downloader.Put("Updates", AutomationValues.Dispatch(collection.Pointer));
        using AutomationObject result = new UpdateJob(downloader, "Download", disposing) { Percent = percent }.Run(stop);

        return Outcomes(result, updates, installation: false);
    }

    public UpdateInstallation Install(IReadOnlyList<AvailableUpdate> updates, IProgress<int> percent, CancellationToken stop)
    {
        using AutomationObject collection = _found.Collection(updates);
        Installer.Put("Updates", AutomationValues.Dispatch(collection.Pointer));
        using AutomationObject result = new UpdateJob(Installer, "Install", disposing) { Percent = percent, WaitsAfterAbort = true }.Run(stop);

        return new UpdateInstallation(Outcomes(result, updates, installation: true), result.GetBool("RebootRequired"));
    }

    public void Dispose()
    {
        _found.Dispose();
        _installer?.Dispose();
        _session?.Dispose();
    }

    private static AutomationObject OpenSession()
    {
        AutomationObject session = AutomationObject.Create(WindowsUpdateIds.UpdateSession);

        try
        {
            session.Put("ClientApplicationID", AutomationValues.Text(WindowsUpdateIds.ClientApplication));

            return session;
        }
        catch
        {
            session.Dispose();

            throw;
        }
    }

    // Each update's own result, never the job's overall code.
    private static List<UpdateOutcome> Outcomes(AutomationObject result, IReadOnlyList<AvailableUpdate> updates, bool installation)
    {
        List<UpdateOutcome> outcomes = [];

        for (int index = 0; index < updates.Count; index++)
        {
            using AutomationObject one = result.CallForObject("GetUpdateResult", AutomationValues.Int(index));
            bool restart = installation && one.GetBool("RebootRequired");
            outcomes.Add(OperationResults.Outcome(updates[index].Id, one.GetInt("ResultCode"), one.GetInt("HResult"), restart));
        }

        return outcomes;
    }

    private List<AvailableUpdate> Keep(List<AutomationObject> items)
    {
        List<(UpdateFields Fields, AutomationObject Update)> found = [];

        try
        {
            foreach (AutomationObject item in items)
            {
                found.Add((UpdateReader.Read(item), item));
            }
        }
        catch
        {
            items.ForEach(item => item.Dispose());

            throw;
        }

        _found.Replace(found);

        return [.. found.Select(update => update.Fields.ToAvailableUpdate())];
    }
}
