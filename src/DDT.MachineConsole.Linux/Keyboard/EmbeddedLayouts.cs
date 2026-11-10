// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.Keyboard;

// The .keymap files this head carries, read by KeyboardLayouts.Find.
internal static class EmbeddedLayouts
{
    public static string? Read(string file)
    {
        using Stream? resource = typeof(EmbeddedLayouts).Assembly.GetManifestResourceStream($"keyboard/{file}");

        if (resource is null)
        {
            return null;
        }

        using StreamReader reader = new(resource);

        return reader.ReadToEnd();
    }

    public static KeyboardLayout Fallback => KeyboardLayouts.Find(KeyboardLayouts.Fallback, Read)
        ?? throw new InvalidOperationException("This console was built without its US keyboard layout.");
}
