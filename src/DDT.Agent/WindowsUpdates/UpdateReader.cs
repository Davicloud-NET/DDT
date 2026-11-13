// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// Reads an IUpdate's fields, and the WUA collections, which have Count and Item(index) only.
internal static class UpdateReader
{
    public static UpdateFields Read(AutomationObject update)
    {
        using AutomationObject identity = update.GetObject("Identity") ?? throw new ComCallException("Identity", AutomationValues.TypeMismatch, "answered with no object");
        using AutomationObject? kbs = update.GetObject("KBArticleIDs");
        using AutomationObject? behavior = update.GetObject("InstallationBehavior");

        return new UpdateFields(identity.GetText("UpdateID") ?? "", identity.GetInt("RevisionNumber"), update.GetText("Title") ?? "", Texts(kbs))
        {
            Type = update.GetInt("Type"),
            CategoryIds = CategoryIds(update),
            BrowseOnly = BrowseOnly(update),
            IsBeta = update.GetBool("IsBeta"),
            CanRequestUserInput = behavior?.GetBool("CanRequestUserInput") ?? false,
            EulaAccepted = update.GetBool("EulaAccepted"),
            MaxDownloadSize = update.GetDecimal("MaxDownloadSize"),
        };
    }

    // Each item for the caller to dispose.
    public static List<AutomationObject> Items(AutomationObject? collection)
    {
        List<AutomationObject> items = [];
        int count = collection?.GetInt("Count") ?? 0;

        for (int index = 0; index < count; index++)
        {
            if (collection?.GetObject("Item", AutomationValues.Int(index)) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static List<string> Texts(AutomationObject? collection)
    {
        List<string> texts = [];
        int count = collection?.GetInt("Count") ?? 0;

        for (int index = 0; index < count; index++)
        {
            texts.Add(collection?.GetText("Item", AutomationValues.Int(index)) ?? "");
        }

        return texts;
    }

    private static List<string> CategoryIds(AutomationObject update)
    {
        using AutomationObject? categories = update.GetObject("Categories");
        List<string> ids = [];

        foreach (AutomationObject category in Items(categories))
        {
            using (category)
            {
                ids.Add(category.GetText("CategoryID") ?? "");
            }
        }

        return ids;
    }

    // IUpdate3. An older agent without it has no browse-only updates to offer.
    private static bool BrowseOnly(AutomationObject update)
    {
        try
        {
            return update.GetBool("BrowseOnly");
        }
        catch (ComCallException)
        {
            return false;
        }
    }
}
