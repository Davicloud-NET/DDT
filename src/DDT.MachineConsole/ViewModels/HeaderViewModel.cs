// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using Avalonia.Media.Imaging;
using DDT.ConsoleProtocol;
using DDT.MachineConsole.Agent;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// The header: the machine, the connection to the agent, the dry-run tag and the organisation's logo.
public sealed class HeaderViewModel(Localizer localizer) : ObservableObject
{
    private ConsoleState? _state;
    private LinkEnd? _ended;
    private bool _isDetached;
    private string? _logoPath;

    public string MachineLabel => MachineFacts.Label(localizer, _state?.Machine);

    public string Connection => _ended switch
    {
        LinkEnd.Closed => localizer.T("The agent has ended"),
        LinkEnd.Broken => localizer.T("The connection to the agent broke"),
        _ when _state is null || _isDetached => localizer.T("Waiting for the agent"),
        _ when _state.Server.Problem is not null => localizer.F("Cannot reach {server}", ("server", Say.Host(_state.Server.Address))),
        _ when _state.MachineId is null => localizer.F("Connecting to {server}", ("server", Say.Host(_state.Server.Address))),
        _ => localizer.F("Connected to {server}", ("server", Say.Host(_state.Server.Address))),
    };

    // In DDT's session, while the agent's service restarts with Windows.
    public bool IsDetached
    {
        get => _isDetached;
        set
        {
            _isDetached = value;
            Raise(nameof(Connection));
        }
    }

    public bool IsDryRun => _state?.DryRun == true;

    public Tag DryRunTag => Tag.Of(localizer.T("Dry run"), TagTone.Attention);

    // Null without a logo, or when the file is not a picture the console can draw.
    public Bitmap? Logo { get; private set; }

    public bool HasLogo => Logo is not null;

    public void Update(ConsoleState state)
    {
        _state = state;
        Raise(nameof(MachineLabel));
        Raise(nameof(Connection));
        Raise(nameof(IsDryRun));
    }

    public void Ended(LinkEnd end)
    {
        _ended = end;
        Raise(nameof(Connection));
    }

    // Read once for each path, since the agent names a new path for a new logo.
    public void ShowLogo(string? path)
    {
        if (path == _logoPath)
        {
            return;
        }

        _logoPath = path;
        Bitmap? previous = Logo;
        Logo = path is null ? null : LogoLoader.Read(path);
        Raise(nameof(Logo));
        Raise(nameof(HasLogo));
        previous?.Dispose();
    }

    public void Refresh() => RaiseAll();
}
