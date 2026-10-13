// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Xunit;

namespace DDT.Agent.Tests;

// A signal to a process a list showed a moment ago, with the kernel's calls played: whether a descriptor is to be
// had, and whether the number is still the same process's once it is.
public sealed class LinuxProcessSignalTests
{
    private const int Process = 4711;
    private const long Started = 5000;
    private const int Descriptor = 9;
    private const int Terminate = 15;

    // ESRCH, ENOSYS and EPERM of errno.h.
    private const int NoSuchProcess = 3;
    private const int NoSuchCall = 38;
    private const int NotPermitted = 1;

    private readonly List<string> _calls = [];

    // open is what pidfd_open answers, and nowStarted the start the number has when it is read again.
    private LinuxProcessSignal Signal(int open, long? nowStarted, int sent = 0) => new(_ => nowStarted)
    {
        Open = process =>
        {
            _calls.Add($"open {process}");

            return open;
        },
        SendTo = (descriptor, number) =>
        {
            _calls.Add($"send {descriptor} {number}");

            return sent;
        },
        Kill = (process, number) =>
        {
            _calls.Add($"kill {process} {number}");

            return sent;
        },
        Close = descriptor => _calls.Add($"close {descriptor}"),
    };

    [Fact]
    public void GoesThroughADescriptorOfTheProcessWhereTheKernelGivesOne()
    {
        Assert.Equal(0, Signal(Descriptor, Started).Send(Process, Started, Terminate));

        Assert.Equal(["open 4711", "send 9 15", "close 9"], _calls);
    }

    // The number was given to another process between the list and the signal. The descriptor is that other one's.
    [Theory]
    [InlineData(6000L)]
    [InlineData(null)]
    public void SignalsNobodyWhenTheDescriptorIsOfAProcessThatStartedAtAnotherTime(long? nowStarted)
    {
        Assert.Equal(NoSuchProcess, Signal(Descriptor, nowStarted).Send(Process, Started, Terminate));

        Assert.Equal(["open 4711", "close 9"], _calls);
    }

    [Fact]
    public void AProcessThatIsGoneIsNotSignalledAnotherWay()
    {
        Assert.Equal(NoSuchProcess, Signal(-NoSuchProcess, Started).Send(Process, Started, Terminate));

        Assert.Equal(["open 4711"], _calls);
    }

    // A kernel before 5.3 has no such call, and a system call filter refuses it. Left at that, nothing was signalled.
    [Theory]
    [InlineData(NoSuchCall)]
    [InlineData(NotPermitted)]
    public void WithoutADescriptorTheSignalGoesToTheNumberWhileItIsStillThatProcesss(int error)
    {
        Assert.Equal(0, Signal(-error, Started).Send(Process, Started, Terminate));

        Assert.Equal(["open 4711", "kill 4711 15"], _calls);
    }

    [Theory]
    [InlineData(6000L)]
    [InlineData(null)]
    public void WithoutADescriptorNobodyIsSignalledOnceTheNumberIsAnotherProcesssOrNobodys(long? nowStarted)
    {
        Assert.Equal(NoSuchProcess, Signal(-NotPermitted, nowStarted).Send(Process, Started, Terminate));

        Assert.Equal(["open 4711"], _calls);
    }

    [Theory]
    [InlineData(Descriptor)]
    [InlineData(-NoSuchCall)]
    public void SaysTheErrorOfASignalTheKernelRefused(int open)
    {
        Assert.Equal(NotPermitted, Signal(open, Started, sent: NotPermitted).Send(Process, Started, Terminate));
    }
}
