// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Agent.Tests;

// The sentences that name the agent's system. Windows PE's are pinned letter for letter: logs, docs and people's
// searches hold them.
public sealed class EnvironmentTextsTests
{
    [Theory]
    [InlineData(AgentEnvironment.WindowsPE)]
    [InlineData(AgentEnvironment.Windows)]
    public void WindowsKeepsEverySentenceItHad(AgentEnvironment environment)
    {
        Assert.Equal(AgentOptions.Usage, EnvironmentTexts.Usage(environment));
        Assert.Equal(
            "No internal disk was found. If this PC's storage is set to RAID or Intel VMD/RST, switch it to AHCI in the " +
            "firmware setup: DDT's boot image has no driver for it.",
            EnvironmentTexts.NoDisk(environment));
        Assert.Equal(
            "The machine keeps starting Windows PE instead of the installed Windows, so the run cannot go on there. Set its " +
            "firmware to start Windows Boot Manager first, then run the sequence again.",
            EnvironmentTexts.InstalledSystemDidNotStart(environment));
        Assert.Equal(
            "The machine was to restart into Windows PE for the run but has not restarted since. Restarting it now.",
            EnvironmentTexts.RestartWasDue(environment, RestartInto.BootEnvironment));
        Assert.Equal(
            "The machine was to start the installed Windows but has not restarted since. Restarting it now.",
            EnvironmentTexts.RestartWasDue(environment, RestartInto.InstalledSystem));
        Assert.Equal(
            "The machine restarts into the installed Windows, where the agent goes on with the run.",
            EnvironmentTexts.Restarts(environment, handingOver: true));
        Assert.Equal(
            "The machine restarts into Windows PE, and the run goes on after the restart.",
            EnvironmentTexts.Restarts(environment, handingOver: false));
        Assert.Equal(
            "The run goes on in the installed Windows, but the machine started Windows PE. Handing the run over again (2 of 3 times).",
            EnvironmentTexts.HandingOverAgain(environment, 2, 3));
        Assert.Equal("Windows PE", EnvironmentTexts.SecureBootTeller(environment));
        Assert.Equal("Carrying on with the agent from the boot image.", EnvironmentTexts.OwnAgent(environment));
        Assert.Equal("ddt-agent-0123456789ab.exe", EnvironmentTexts.DownloadedAgent(environment, "0123456789ABCDEF"));
        Assert.Equal("0xC0000135", EnvironmentTexts.ExitCode(environment, unchecked((int)0xC0000135)));
        Assert.Equal(
            "The machine restarts without choosing where it starts next, so it comes back to Windows PE only if its boot order " +
            "starts from the network before the disk.",
            EnvironmentTexts.RestartsPlainly(environment));
    }

    [Fact]
    public void WindowsKeepsWhatItSaysOfTheBootOrderAndOfARefusedCertificate()
    {
        const AgentEnvironment WindowsPE = AgentEnvironment.WindowsPE;

        Assert.Equal(
            "The firmware boot order is back as it was before the deployment, so this machine does not start Windows without its answer file.",
            EnvironmentTexts.BootOrderIsBack(WindowsPE));
        Assert.Equal(
            "The firmware boot order could not be put back as it was before the deployment (The firmware refused.), so this machine may " +
            "start Windows without its answer file. Start it from the network to deploy it again.",
            EnvironmentTexts.BootOrderStays(WindowsPE, "The firmware refused."));
        Assert.Equal(
            "The server's certificate does not come from the root certificate this boot image trusts. Build the " +
            "boot image again with Build-BootImage.ps1 -RootCertificatePath set to the server's ddt-root.pem, which is " +
            "next to its certificate (/var/lib/ddt/certs/ddt-root.pem in the container). A server that moved to its " +
            "own root needs this once.",
            EnvironmentTexts.OtherDdtRoot(WindowsPE));
        Assert.Equal(
            "The server's certificate, issued by CN=Example, does not chain to the root certificate this boot image " +
            "trusts. Build the boot image with Build-BootImage.ps1 -RootCertificatePath set to the root of the CA that " +
            "issued it, and have the server send the intermediate certificates after its own in the certificate file, " +
            "because the agent does not download them.",
            EnvironmentTexts.OtherIssuer(WindowsPE, "CN=Example"));
    }

    // None of them tells the reader of Windows PE, a boot image to build or a Windows that starts.
    [Fact]
    public void TheLinuxBootEnvironmentNamesItselfAndNothingOfWindows()
    {
        const AgentEnvironment LinuxBoot = AgentEnvironment.LinuxBoot;
        string[] sentences =
        [
            EnvironmentTexts.Usage(LinuxBoot),
            EnvironmentTexts.NoDisk(LinuxBoot),
            EnvironmentTexts.InstalledSystemDidNotStart(LinuxBoot),
            EnvironmentTexts.RestartWasDue(LinuxBoot, RestartInto.BootEnvironment),
            EnvironmentTexts.RestartWasDue(LinuxBoot, RestartInto.InstalledSystem),
            EnvironmentTexts.Restarts(LinuxBoot, handingOver: true),
            EnvironmentTexts.Restarts(LinuxBoot, handingOver: false),
            EnvironmentTexts.HandingOverAgain(LinuxBoot, 1, 3),
            EnvironmentTexts.SecureBootTeller(LinuxBoot),
            EnvironmentTexts.RestartsPlainly(LinuxBoot),
            EnvironmentTexts.BootOrderIsBack(LinuxBoot),
            EnvironmentTexts.BootOrderStays(LinuxBoot, "refused"),
            EnvironmentTexts.OtherDdtRoot(LinuxBoot),
            EnvironmentTexts.OtherIssuer(LinuxBoot, "CN=Example"),
            EnvironmentTexts.OwnAgent(LinuxBoot),
        ];

        Assert.All(sentences, sentence => Assert.DoesNotContain("Windows", sentence, StringComparison.Ordinal));
        Assert.All(sentences, sentence => Assert.DoesNotContain("boot image", sentence, StringComparison.Ordinal));
        Assert.Equal(
            "The machine restarts into the Linux boot environment, and the run goes on after the restart.",
            EnvironmentTexts.Restarts(LinuxBoot, handingOver: false));
        Assert.Equal(
            "The run goes on in the installed Linux, but the machine started the Linux boot environment. Handing the run over again (1 of 3 times).",
            EnvironmentTexts.HandingOverAgain(LinuxBoot, 1, 3));
        Assert.Equal(EnvironmentTexts.LinuxDidNotStart, EnvironmentTexts.InstalledSystemDidNotStart(LinuxBoot));
        Assert.Equal("Carrying on with the agent the Linux boot environment brought.", EnvironmentTexts.OwnAgent(LinuxBoot));
        Assert.Equal("ddt-agent-0123456789ab", EnvironmentTexts.DownloadedAgent(LinuxBoot, "0123456789ABCDEF"));
        Assert.Equal("139", EnvironmentTexts.ExitCode(LinuxBoot, 139));
    }

    // The constants other code and tests name are the Windows sentences.
    [Fact]
    public void TheRunnersConstantsAreWhatWindowsPESays()
    {
        Assert.Equal(SequenceRunner.NoDiskMessage, EnvironmentTexts.NoDisk(AgentEnvironment.WindowsPE));
        Assert.Equal(SequenceRunner.WindowsDidNotStartMessage, EnvironmentTexts.InstalledSystemDidNotStart(AgentEnvironment.WindowsPE));
    }
}
