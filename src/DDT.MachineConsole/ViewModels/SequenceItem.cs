// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// A sequence in the list, with its flags as tags.
public sealed class SequenceItem(Localizer localizer, SequenceOption option) : ObservableObject
{
    public SequenceOption Option => option;

    public string Name => option.Name;

    public string? Description => string.IsNullOrWhiteSpace(option.Description) ? null : option.Description;

    public bool HasDescription => Description is not null;

    public IReadOnlyList<Tag> Tags
    {
        get
        {
            List<Tag> tags = [];

            if (option.Suggested)
            {
                tags.Add(Tag.Of(localizer.T("Suggested"), TagTone.Ok));
            }

            if (option.ErasesDisk)
            {
                tags.Add(Tag.Of(localizer.T("Erases a disk"), TagTone.Idle));
            }

            if (option.NeedsComputerName)
            {
                tags.Add(Tag.Of(localizer.T("Asks for a name"), TagTone.Idle));
            }

            if (option.RequiredBytes > 0)
            {
                tags.Add(Tag.Of(localizer.F("Needs {size}", ("size", Say.Bytes(localizer, option.RequiredBytes))), TagTone.Idle));
            }

            if (option.NotSignedForSecureBoot)
            {
                tags.Add(Tag.Of(localizer.T("Not for Secure Boot"), TagTone.Attention));
            }

            if (option.NotTrustedHere)
            {
                tags.Add(Tag.Of(localizer.T("Not trusted by this machine"), TagTone.Attention));
            }

            return tags;
        }
    }

    public void Refresh() => Raise(nameof(Tags));
}
