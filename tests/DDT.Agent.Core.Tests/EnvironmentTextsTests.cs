// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
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

    // As the installed Windows speaks like Windows PE. The agent that runs there brings what it says differently.
    [Fact]
    public void TheInstalledLinuxSpeaksLikeTheLinuxBootEnvironment()
    {
        const AgentEnvironment Linux = AgentEnvironment.Linux;

        Assert.Equal(EnvironmentTexts.LinuxUsage, EnvironmentTexts.Usage(Linux));
        Assert.Equal("ddt-agent-0123456789ab", EnvironmentTexts.DownloadedAgent(Linux, "0123456789ABCDEF"));
        Assert.Equal("139", EnvironmentTexts.ExitCode(Linux, 139));
        Assert.Equal("this machine's firmware", EnvironmentTexts.SecureBootTeller(Linux));
        Assert.Equal("the installed Linux", AgentEnvironments.Name(Linux));
        Assert.Equal("the installed Windows", AgentEnvironments.Name(AgentEnvironment.Windows));
        Assert.Equal(("installed Linux", "Linux boot environment", "Windows"), (LogText.Phase("Linux"), LogText.Phase("LinuxBoot"), LogText.Phase("Windows")));
    }

    // Windows keeps its words. The Linux boot environment keeps a run in memory, so a stop there ends the run, while
    // the installed Linux goes on with it at the agent's next start.
    [Fact]
    public void EachSystemSaysWhatAStopARestartAndAStepOfTheBootEnvironmentMeanThere()
    {
        foreach (AgentEnvironment windows in (AgentEnvironment[])[AgentEnvironment.WindowsPE, AgentEnvironment.Windows])
        {
            Assert.Equal(
                "The agent was stopped. The run's state stays on the disk, so the run goes on after a restart if the server still runs it.",
                EnvironmentTexts.Stopped(windows));
            Assert.Equal("Windows restarts, and the run goes on after the restart.", EnvironmentTexts.InstalledSystemRestarts(windows));
            Assert.Equal(SequenceRunner.WindowsPEAfterWindowsMessage, EnvironmentTexts.BootStepAfterInstalled(windows));
        }

        Assert.Equal(
            "The agent was stopped. The Linux boot environment keeps a run's state in memory, so the run does not go on after a restart.",
            EnvironmentTexts.Stopped(AgentEnvironment.LinuxBoot));
        Assert.Equal(
            "The agent was stopped. The run's state stays on the disk, so the run goes on when the agent starts again, if the server still runs it.",
            EnvironmentTexts.Stopped(AgentEnvironment.Linux));
        Assert.Equal("Linux restarts, and the run goes on after the restart.", EnvironmentTexts.InstalledSystemRestarts(AgentEnvironment.Linux));
    }

    // The agent's next start fails a step that was cut off, so the stop must not promise that the run goes on.
    [Fact]
    public void AStopInAStepOfTheInstalledLinuxSaysWhatTheNextStartDoesWithThatStep()
    {
        RunScriptStep step = new() { Id = Guid.NewGuid(), Name = "Sleep long", Phase = SequencePhase.Linux, Script = "sleep 600" };

        Assert.Equal(
            "The agent was stopped while step Sleep long ran. The run's state stays on the disk. Nobody knows how far the step came, so it " +
            "counts as failed when the agent starts again, and the run fails with it.",
            EnvironmentTexts.Stopped(AgentEnvironment.Linux, step));
        Assert.Equal(
            "The agent was stopped while step Sleep long ran. The run's state stays on the disk. Nobody knows how far the step came, so it " +
            "counts as failed when the agent starts again, and the run goes on after it, if the server still runs it.",
            EnvironmentTexts.Stopped(AgentEnvironment.Linux, step with { ContinueOnError = true }));

        // Windows and the boot environment keep their words.
        Assert.Equal(EnvironmentTexts.Stopped(AgentEnvironment.Windows), EnvironmentTexts.Stopped(AgentEnvironment.Windows, step));
        Assert.Equal(EnvironmentTexts.Stopped(AgentEnvironment.LinuxBoot), EnvironmentTexts.Stopped(AgentEnvironment.LinuxBoot, step));
    }

    [Fact]
    public void AStepOfTheBootEnvironmentAfterTheInstalledLinuxIsSaidInLinuxsWords()
    {
        Assert.Equal(
            "A step that runs in the Linux boot environment follows the steps in the installed Linux, and the run cannot go back to the boot " +
            "environment. Move the step before the first step in the installed Linux, then run the sequence again.",
            EnvironmentTexts.BootStepAfterInstalled(AgentEnvironment.Linux));
    }

    // An NVMe disk behind a controller in RAID or RST mode is no device Linux sees, whatever drivers it has.
    [Fact]
    public void WithoutADiskTheLinuxBootEnvironmentNamesTheControllerModeThatHidesOne() =>
        Assert.Equal(
            "No internal disk was found. The Linux boot environment may have no driver for this PC's storage controller, or the " +
            "controller is in RAID or Intel RST mode, which hides the disk from Linux: set it to AHCI in the firmware setup to show " +
            "it. The log lists every disk it saw and why it was left out.",
            EnvironmentTexts.NoDisk(AgentEnvironment.LinuxBoot));

    // The constants other code and tests name are the Windows sentences.
    [Fact]
    public void TheRunnersConstantsAreWhatWindowsPESays()
    {
        Assert.Equal(SequenceRunner.NoDiskMessage, EnvironmentTexts.NoDisk(AgentEnvironment.WindowsPE));
        Assert.Equal(SequenceRunner.WindowsDidNotStartMessage, EnvironmentTexts.InstalledSystemDidNotStart(AgentEnvironment.WindowsPE));
    }
}
