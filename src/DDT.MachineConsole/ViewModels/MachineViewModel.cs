// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Everything the agent knows about the machine, and about itself: identity, network, disks, Secure Boot, the keyboard
// layout and the agent.
public sealed class MachineViewModel(Localizer localizer) : OverlayViewModel(localizer)
{
    private ConsoleState? _state;

    public string Title => T("This machine");

    public override string CloseLabel => T("Close the details");

    // Set by the console, which opens the command prompt on Shift+F10 wherever it is. None in DDT's session in the
    // installed Windows, where no command prompt opens.
    public Command? PromptCommand { get; set; }

    public bool HasPrompt => PromptCommand is not null;

    public string PromptLabel => T("Command prompt");

    public string IdentityTitle => T("Identity");

    public string NetworkTitle => T("Network");

    public string DisksTitle => T("Disks");

    public string FirmwareTitle => T("Firmware");

    public string AgentTitle => T("Agent");

    public string NotRead => T("Not read yet.");

    public IReadOnlyList<Fact> Identity
    {
        get
        {
            ConsoleMachine? machine = _state?.Machine;

            return
            [
                new(T("Manufacturer"), Known(machine?.Manufacturer)),
                new(T("Model"), Known(machine?.Model)),
                new(T("Serial number"), Known(machine?.SerialNumber), Mono: true),
                new(T("SMBIOS UUID"), Known(machine?.SmbiosUuid), Mono: true),
                new(T("Machine ID"), _state?.MachineId is { } id ? id.ToString() : T("Not registered yet"), Mono: true),
            ];
        }
    }

    public IReadOnlyList<Fact> Network
    {
        get
        {
            ConsoleMachine? machine = _state?.Machine;
            List<Fact> facts = [];

            if (machine is null || machine.MacAddresses.Count == 0)
            {
                facts.Add(new Fact(T("MAC address"), T("Not reported")));
            }
            else
            {
                for (int index = 0; index < machine.MacAddresses.Count; index++)
                {
                    facts.Add(new Fact(
                        index == 0 ? T("MAC address, primary") : T("MAC address"),
                        Say.Mac(machine.MacAddresses[index]),
                        Mono: true));
                }
            }

            facts.Add(new Fact(
                T("IP address"),
                machine is null || machine.IpAddresses.Count == 0 ? T("Not reported") : string.Join(", ", machine.IpAddresses),
                Mono: machine?.IpAddresses.Count > 0));

            facts.Add(new Fact(T("Server"), _state?.Server.Address ?? T("Not known"), Mono: true));

            return facts;
        }
    }

    public IReadOnlyList<DiskItem> Disks => _state?.Machine.Disks is { } disks ? [.. disks.Select(disk => new DiskItem(L, disk))] : [];

    public bool DisksRead => _state?.Machine.Disks is not null;

    public bool HasNoDisks => DisksRead && Disks.Count == 0;

    public bool DisksNotRead => !DisksRead;

    public string NoDisks => T("No disk DDT can install on.");

    public IReadOnlyList<Fact> Firmware =>
    [
        new(T("Secure Boot"), Say.OnOff(L, _state?.Machine.SecureBootEnabled)),
        new(T("Trusted CAs"), Say.Cas(L, _state?.Machine.TrustedUefiCas)),
    ];

    public IReadOnlyList<Fact> Agent =>
    [
        new(T("Agent version"), Known(_state?.AgentVersion)),
        new(T("Stage"), _state is null ? T("Not known") : Say.Stage(L, _state.Stage)),
        new(T("Dry run"), _state?.DryRun == true ? T("Yes, nothing on the disks changes") : T("No")),
        new(T("Keyboard layout"), Known(_state?.Machine.KeyboardLayout)),
        new(T("Console version"), ConsoleBuild.Version),
    ];

    public void Update(ConsoleState state)
    {
        _state = state;
        RaiseAll();
    }

    private string Known(string? value) => string.IsNullOrWhiteSpace(value) ? T("Not reported") : value;
}
