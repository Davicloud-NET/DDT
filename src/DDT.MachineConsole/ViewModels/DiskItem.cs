// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

public sealed class DiskItem(Localizer localizer, ConsoleDisk disk) : ObservableObject
{
    public ConsoleDisk Disk => disk;

    public string Number => localizer.F("Disk {number}", ("number", localizer.Number(disk.Number)));

    public string Model => Say.DiskModel(localizer, disk.Model);

    public string Size => Say.Bytes(localizer, disk.SizeBytes);

    public string Bus => Say.Bus(localizer, disk.BusType);

    public string Partitions => Say.Partitions(localizer, disk.PartitionCount);

    // A disk with partitions holds something that erasing it destroys.
    public bool HoldsData => disk.PartitionCount > 0;

    public void Refresh() => Raise(string.Empty);
}
