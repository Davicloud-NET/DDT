// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.WindowsUpdates;

// The update objects the last search returned, by id and revision: those, not look-alikes, are downloaded and installed.
internal sealed class FoundUpdates : IDisposable
{
    private readonly Dictionary<(string Id, int Revision), AutomationObject> _updates = [];

    // Takes the objects over and releases the last search's.
    public void Replace(IEnumerable<(UpdateFields Fields, AutomationObject Update)> found)
    {
        Dispose();

        foreach ((UpdateFields fields, AutomationObject update) in found)
        {
            if (!_updates.TryAdd((fields.Id, fields.Revision), update))
            {
                update.Dispose();
            }
        }
    }

    public AutomationObject Of(AvailableUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        return _updates.TryGetValue((update.Id, update.Revision), out AutomationObject? found)
            ? found
            : throw new DeploymentStepException($"Windows Update's last search did not return {UpdateNames.Of(update)}.");
    }

    // A new UpdateCollection of the updates, in their order.
    public AutomationObject Collection(IReadOnlyList<AvailableUpdate> updates)
    {
        AutomationObject collection = AutomationObject.Create(WindowsUpdateIds.UpdateCollection);

        try
        {
            foreach (AvailableUpdate update in updates)
            {
                collection.CallInt("Add", AutomationValues.Dispatch(Of(update).Pointer));
            }

            return collection;
        }
        catch
        {
            collection.Dispose();

            throw;
        }
    }

    public void Dispose()
    {
        foreach (AutomationObject update in _updates.Values)
        {
            update.Dispose();
        }

        _updates.Clear();
    }
}
