// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Texts;

// Turns the protocol's values into the console's words, and formats numbers and identifiers the way the web does.
public static class Say
{
    private static readonly string[] s_units = ["KB", "MB", "GB", "TB"];

    public static string Stage(Localizer l, ConsoleStage stage) => stage switch
    {
        ConsoleStage.Starting => l.T("Starting"),
        ConsoleStage.Connecting => l.T("Connecting"),
        ConsoleStage.WaitingForAuthorization => l.T("Waiting for authorization"),
        ConsoleStage.WaitingForSequence => l.T("Waiting for a task sequence"),
        ConsoleStage.Choosing => l.T("Choosing a task sequence"),
        ConsoleStage.Running => l.T("Running"),
        ConsoleStage.Restarting => l.T("Restarting"),
        ConsoleStage.Finished => l.T("Finished"),
        ConsoleStage.Failed => l.T("Failed"),
        _ => l.T("Stopped"),
    };

    public static string StepState(Localizer l, ConsoleStepState state) => state switch
    {
        ConsoleStepState.Running => l.T("Running"),
        ConsoleStepState.Done => l.T("Done"),
        ConsoleStepState.Skipped => l.T("Skipped"),
        ConsoleStepState.Failed => l.T("Failed"),
        _ => l.T("Waiting"),
    };

    public static string Remedy(Localizer l, ConsoleRemedy remedy) => remedy switch
    {
        ConsoleRemedy.RunAgain => l.T("Run it again: choose the task sequence here, or assign it on the Machines page."),
        _ => l.T("Restart the machine from the network to start over."),
    };

    public static string ConnectionStage(Localizer l, ConnectionStage stage) => stage switch
    {
        ConsoleProtocol.ConnectionStage.NameLookup => l.T("Name lookup"),
        ConsoleProtocol.ConnectionStage.Connection => l.T("Connection"),
        ConsoleProtocol.ConnectionStage.SecureConnection => l.T("Secure connection"),
        _ => l.T("Answer"),
    };

    // What to check when a request failed at that stage.
    public static string ConnectionAdvice(Localizer l, ConnectionStage stage) => stage switch
    {
        ConsoleProtocol.ConnectionStage.NameLookup => l.T(
            "The server's name did not resolve. Check this network's DNS server and the server name in the boot image."),
        ConsoleProtocol.ConnectionStage.Connection => l.T(
            "The server did not accept a connection. Check the address and port, the firewall and the route to the server."),
        ConsoleProtocol.ConnectionStage.SecureConnection => l.T(
            "The secure connection failed. The server may be stalling, or the boot image does not trust its certificate."),
        _ => l.T("The server answered unexpectedly. Its log says why."),
    };

    // Linux says the Linux sibling of a sentence that names Windows.
    public static string Activity(Localizer l, ConsoleActivity activity, bool linux) => activity switch
    {
        ConsoleActivity.Preparing => l.T("Checking the run"),
        ConsoleActivity.Step => l.T("Running a step"),
        ConsoleActivity.HandingOver when linux => l.T("Handing the run over to the installed Linux"),
        ConsoleActivity.HandingOver => l.T("Handing the run over to the installed Windows"),
        ConsoleActivity.Restarting => l.T("Restarting"),
        ConsoleActivity.WaitingForWindowsSetup => l.T("Waiting for Windows setup"),
        ConsoleActivity.WaitingForCloudInit => l.T("Waiting for cloud-init"),
        ConsoleActivity.Finishing => l.T("Making the disk bootable and sending the last reports"),
        ConsoleActivity.WaitingForInput => l.T("Waiting for answers"),
        ConsoleActivity.Paused => l.T("Paused"),
        _ => l.T("Removing the agent"),
    };

    public static string RestartTarget(Localizer l, RestartTarget target) => target switch
    {
        ConsoleProtocol.RestartTarget.WindowsPE => l.T("Restarting into Windows PE"),
        ConsoleProtocol.RestartTarget.LinuxBoot => l.T("Restarting into the Linux boot environment"),
        _ => l.T("Restarting into the installed system"),
    };

    public static string RestartReason(Localizer l, RestartReason reason, bool linux) => reason switch
    {
        ConsoleProtocol.RestartReason.StepAsked => l.T("A step asked for a restart."),
        ConsoleProtocol.RestartReason.HandOver when linux => l.T("The run goes on in the installed Linux."),
        ConsoleProtocol.RestartReason.HandOver => l.T("The run goes on in the installed Windows."),
        ConsoleProtocol.RestartReason.RunDone => l.T("The run is done."),
        _ => l.T("A restart was still pending."),
    };

    // The run screen's heading for a kind of step. Returns null for an unknown kind, and the screen shows the step's
    // name instead.
    public static string? StepAction(Localizer l, string kind) => kind switch
    {
        "partition" => l.T("Partitioning the disk"),
        "applyImage" => l.T("Applying the image"),
        "injectDrivers" => l.T("Adding drivers"),
        "writeUnattend" => l.T("Writing the answer file"),
        "joinDomain" => l.T("Joining the domain"),
        "runScript" => l.T("Running a script"),
        "reboot" => l.T("Restarting"),
        "writeRawImage" => l.T("Writing the disk image"),
        "writeCloudInitSeed" => l.T("Writing the cloud-init seed"),
        "setVariable" => l.T("Setting a variable"),
        "pause" => l.T("Paused"),
        "installApplications" => l.T("Installing applications"),
        _ => null,
    };

    public static string Phase(Localizer l, ConsolePhase phase) => phase switch
    {
        ConsolePhase.WindowsPE => l.T("In Windows PE"),
        ConsolePhase.LinuxBoot => l.T("In the Linux boot environment"),
        ConsolePhase.Linux => l.T("In the installed Linux"),
        _ => l.T("In the installed Windows"),
    };

    public static string Level(Localizer l, ConsoleLogLevel level) => level switch
    {
        ConsoleLogLevel.Warning => l.T("Warning"),
        ConsoleLogLevel.Error => l.T("Error"),
        _ => l.T("Info"),
    };

    public static string SecureBootProblem(Localizer l, SecureBootQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        string image = question.ImageName ?? l.T("The disk image");

        return question.Problem switch
        {
            ConsoleProtocol.SecureBootProblem.NotSigned => l.F(
                "{image} is not signed for Secure Boot. It starts only once Secure Boot is turned off or your own key is enrolled.",
                ("image", image)),
            ConsoleProtocol.SecureBootProblem.MayNotStart => l.F(
                "DDT cannot tell whether {image} is signed for Secure Boot. It may not start until Secure Boot is turned off " +
                "or your own key is enrolled.",
                ("image", image)),
            _ => l.F(
                "{image} is signed only under {cas}, which this machine does not trust. It starts only once that CA is " +
                "allowed or Secure Boot is turned off.",
                ("image", image),
                ("cas", Cas(l, question.SignedUnder))),
        };
    }

    public static string Cas(Localizer l, MicrosoftUefiCas? cas) => cas switch
    {
        MicrosoftUefiCas.Ca2011 => l.T("Microsoft's third-party UEFI CA 2011"),
        MicrosoftUefiCas.Ca2023 => l.T("Microsoft's third-party UEFI CA 2023"),
        MicrosoftUefiCas.Ca2011 | MicrosoftUefiCas.Ca2023 => l.T("Microsoft's third-party UEFI CAs 2011 and 2023"),
        MicrosoftUefiCas.None => l.T("none of Microsoft's third-party UEFI CAs"),
        _ => l.T("Not known"),
    };

    public static string SecureBoot(Localizer l, bool? enabled) => enabled switch
    {
        true => l.T("Secure Boot on"),
        false => l.T("Secure Boot off"),
        _ => l.T("Secure Boot not reported"),
    };

    public static string OnOff(Localizer l, bool? on) => on switch
    {
        true => l.T("On"),
        false => l.T("Off"),
        _ => l.T("Not reported"),
    };

    // Binary multiples with the labels Windows shows and at most one decimal, like the web writes them.
    public static string Bytes(Localizer l, long bytes)
    {
        if (bytes < 1024)
        {
            return l.F("{count} bytes", ("count", l.Number(bytes)));
        }

        double value = bytes / 1024.0;
        int unit = 0;

        while (value >= 1024 && unit < s_units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{l.Number(value, 1)} {s_units[unit]}";
    }

    public static string Partitions(Localizer l, int count) => count switch
    {
        0 => l.T("Empty"),
        1 => l.T("1 partition"),
        _ => l.F("{count} partitions", ("count", l.Number(count))),
    };

    // How the disk is attached. Turns the agent's StorageBusType name into the words storage tools use.
    public static string Bus(Localizer l, string bus) => bus switch
    {
        "Nvme" => "NVMe",
        "NvmeOf" => "NVMe-oF",
        "Sata" => "SATA",
        "Sas" => "SAS",
        "Ata" => "ATA",
        "Atapi" => "ATAPI",
        "Scsi" => "SCSI",
        "IScsi" => "iSCSI",
        "Usb" => "USB",
        "Raid" => "RAID",
        "Sd" => "SD",
        "Mmc" => "MMC",
        "Ufs" => "UFS",
        "Scm" => "SCM",
        "Ssa" => "SSA",
        "Fibre" => "Fibre Channel",
        "Ieee1394" => "IEEE 1394",
        "Spaces" => "Storage Spaces",
        "Virtual" => l.T("Virtual"),
        "FileBackedVirtual" => l.T("Virtual, backed by a file"),
        "Unknown" => l.T("Unknown bus"),
        _ => bus,
    };

    public static string DiskModel(Localizer l, string? model) => string.IsNullOrWhiteSpace(model) ? l.T("Unknown model") : model;

    // Formats 12 hex digits the way the web writes a MAC address: 3C:52:82:6A:1F:0B.
    public static string Mac(string mac)
    {
        ArgumentNullException.ThrowIfNull(mac);

        return mac.Length == 12 && !mac.Contains(':', StringComparison.Ordinal)
            ? string.Join(':', Enumerable.Range(0, 6).Select(index => mac.Substring(index * 2, 2)))
            : mac;
    }

    // The time of a log line as the text console shows it: by the machine's clock, in UTC.
    public static string Time(DateTimeOffset time) => time.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    // The host and port of the server's URL, which is what a technician recognizes.
    public static string Host(string address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ? uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}" : address;
    }

    // Tags are set in capitals. The invariant culture capitalizes German letters like ä and keeps their umlaut.
    public static string Tag(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.ToUpperInvariant();
    }
}
