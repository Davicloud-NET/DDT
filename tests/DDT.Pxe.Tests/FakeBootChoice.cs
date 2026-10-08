// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe.Tests;

// The server's list of what each machine should start, as a test sets it. Asked keeps who was asked for.
internal sealed class FakeBootChoice : IBootChoice
{
    public BootWish Wish { get; set; } = BootWish.None;

    public Exception? Failure { get; set; }

    public List<NetbootMachine> Asked { get; } = [];

    public BootWish For(NetbootMachine machine)
    {
        Asked.Add(machine);

        return Failure is null ? Wish : throw Failure;
    }
}
