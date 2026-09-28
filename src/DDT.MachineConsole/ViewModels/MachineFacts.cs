// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The machine as the console names it and as a technician finds it on the Machines page.
public static class MachineFacts
{
    // The header's name for the machine: its model, which the Machines page shows with its MAC address.
    public static string Label(Localizer l, ConsoleMachine? machine)
    {
        ArgumentNullException.ThrowIfNull(l);

        if (machine is null)
        {
            return l.T("This machine");
        }

        string? model = Join(machine.Manufacturer, machine.Model);

        return model ?? (machine.MacAddresses.Count > 0 ? Say.Mac(machine.MacAddresses[0]) : l.T("This machine"));
    }

    // What tells this machine apart on the Machines page: the MAC address first, as the page shows it.
    public static IReadOnlyList<Fact> Identity(Localizer l, ConsoleMachine machine)
    {
        ArgumentNullException.ThrowIfNull(l);
        ArgumentNullException.ThrowIfNull(machine);

        List<Fact> facts = [];

        if (machine.MacAddresses.Count > 0)
        {
            facts.Add(new Fact(l.T("MAC address"), Say.Mac(machine.MacAddresses[0]), Mono: true));
        }

        if (Join(machine.Manufacturer, machine.Model) is { } model)
        {
            facts.Add(new Fact(l.T("Model"), model));
        }

        if (!string.IsNullOrWhiteSpace(machine.SerialNumber))
        {
            facts.Add(new Fact(l.T("Serial number"), machine.SerialNumber, Mono: true));
        }

        if (machine.IpAddresses.Count > 0)
        {
            facts.Add(new Fact(l.T("IP address"), string.Join(", ", machine.IpAddresses), Mono: true));
        }

        return facts;
    }

    // The footer's facts: where the machine is on the network, and its Secure Boot.
    public static IReadOnlyList<Fact> Footer(Localizer l, ConsoleMachine? machine)
    {
        ArgumentNullException.ThrowIfNull(l);

        List<Fact> facts = [];

        if (machine is null)
        {
            return facts;
        }

        if (machine.IpAddresses.Count > 0)
        {
            facts.Add(new Fact(l.T("IP address"), machine.IpAddresses[0], Mono: true));
        }

        if (machine.MacAddresses.Count > 0)
        {
            facts.Add(new Fact(l.T("MAC address"), Say.Mac(machine.MacAddresses[0]), Mono: true));
        }

        if (machine.SecureBootEnabled is not null)
        {
            facts.Add(new Fact(l.T("Secure Boot"), Say.SecureBoot(l, machine.SecureBootEnabled)));
        }

        return facts;
    }

    // The maker and the model, such as "Dell Inc. Latitude 7450", with the maker once where the model names it too.
    public static string? Join(string? manufacturer, string? model)
    {
        string? maker = string.IsNullOrWhiteSpace(manufacturer) ? null : manufacturer.Trim();
        string? name = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

        if (maker is null || name is null)
        {
            return name ?? maker;
        }

        return name.StartsWith(maker, StringComparison.OrdinalIgnoreCase) ? name : $"{maker} {name}";
    }
}
