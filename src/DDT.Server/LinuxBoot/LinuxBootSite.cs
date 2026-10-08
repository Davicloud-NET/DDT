// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Pxe;
using DDT.Server.Certificates;
using DDT.Server.Settings;

namespace DDT.Server.LinuxBoot;

// What netboot serves and generates for the Linux boot environment: the release's files, and this server's address,
// root certificate, keyboard layout and kernel arguments, each read as a machine asks.
public sealed class LinuxBootSite(LinuxBootEnvironment environment, ServerAddress address, DdtSettings settings) : ILinuxBootSite
{
    public LinuxBootBundle? Current
    {
        get
        {
            LinuxBootState state = environment.Current;

            if (!state.Available || environment.RootCertificatePem is not { } root)
            {
                return null;
            }

            SettingsSnapshot snapshot = settings.Current;
            string keymap = LinuxKeyboardLayouts.Keymap(snapshot.Deployment.ConsoleKeyboardLayout, state.Keymaps);

            return new LinuxBootBundle(state.Folder, state.Files, new Uri(address.Url), root)
            {
                Keymap = keymap,
                KeyboardLayout = LinuxKeyboardLayouts.Name(keymap),
                KernelArguments = snapshot.Pxe?.LinuxKernelArguments ?? string.Empty,
            };
        }
    }
}
