// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// Registers Microsoft Update, which then stays: it is Windows' "Receive updates for other Microsoft products".
internal static class MicrosoftUpdateService
{
    public static void Register()
    {
        using AutomationObject manager = AutomationObject.Create(WindowsUpdateIds.UpdateServiceManager);
        manager.Put("ClientApplicationID", AutomationValues.Text(WindowsUpdateIds.ClientApplication));

        if (!IsRegistered(manager))
        {
            manager.Call("AddService2", AutomationValues.Text(WindowsUpdateIds.MicrosoftUpdate), AutomationValues.Int(WindowsUpdateIds.RegistrationFlags), AutomationValues.Text(""))?.Dispose();
        }
    }

    // A failed query reads as not registered: AddService2 then names the real failure.
    private static bool IsRegistered(AutomationObject manager)
    {
        try
        {
            using AutomationObject? registration = manager.Call("QueryServiceRegistration", AutomationValues.Text(WindowsUpdateIds.MicrosoftUpdate));

            return registration?.GetInt("RegistrationState") == WindowsUpdateIds.Registered;
        }
        catch (ComCallException)
        {
            return false;
        }
    }
}
