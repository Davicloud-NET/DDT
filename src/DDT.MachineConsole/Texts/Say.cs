// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.ConsoleProtocol;

namespace DDT.MachineConsole.Texts;

// The protocol's values in the console's words, and the numbers and identifiers it shows, formatted the way the web
// formats them.
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
        ConsoleRemedy.RunAgain => l.T(
            "Run it again: choose the task sequence here when the console asks for one, or assign it on the Machines " +
            "page. The agent waits for either."),
        _ => l.T("The agent has stopped. Restart the machine from the network to start over."),
    };

    public static string ConnectionStage(Localizer l, ConnectionStage stage) => stage switch
    {
        ConsoleProtocol.ConnectionStage.NameLookup => l.T("Name lookup"),
        ConsoleProtocol.ConnectionStage.Connection => l.T("Connection"),
        ConsoleProtocol.ConnectionStage.SecureConnection => l.T("Secure connection"),
        _ => l.T("Answer"),
    };

    // What to look at when a request failed at stage.
    public static string ConnectionAdvice(Localizer l, ConnectionStage stage) => stage switch
    {
        ConsoleProtocol.ConnectionStage.NameLookup => l.T(
            "The server's name did not resolve. Check the DNS server the network gives this machine, and the server " +
            "name the boot image was built with."),
        ConsoleProtocol.ConnectionStage.Connection => l.T(
            "The server did not accept a connection. Check the address and port, the firewall, and the route from " +
            "this network to the server."),
        ConsoleProtocol.ConnectionStage.SecureConnection => l.T(
            "The secure connection failed. The server may be stalling, or its certificate may not chain to the root " +
            "the boot image trusts."),
        _ => l.T("The server was reached but did not answer as the agent expects. Its log says why."),
    };

    public static string Activity(Localizer l, ConsoleActivity activity) => activity switch
    {
        ConsoleActivity.Preparing => l.T("Checking the run before anything on the disk changes"),
        ConsoleActivity.Step => l.T("Running a step"),
        ConsoleActivity.HandingOver => l.T("Handing the run over to the installed Windows"),
        ConsoleActivity.Restarting => l.T("Restarting"),
        ConsoleActivity.WaitingForWindowsSetup => l.T("Waiting for Windows setup"),
        ConsoleActivity.Finishing => l.T("Making the disk bootable and sending the last reports"),
        ConsoleActivity.WaitingForInput => l.T("Waiting for answers"),
        ConsoleActivity.Paused => l.T("Paused"),
        _ => l.T("Removing the agent"),
    };

    public static string RestartTarget(Localizer l, RestartTarget target) => target == ConsoleProtocol.RestartTarget.WindowsPE
        ? l.T("Restarting into Windows PE")
        : l.T("Restarting into the installed system");

    public static string RestartReason(Localizer l, RestartReason reason) => reason switch
    {
        ConsoleProtocol.RestartReason.StepAsked => l.T("A step asked for a restart. The run goes on after it."),
        ConsoleProtocol.RestartReason.HandOver => l.T("The run goes on in the installed Windows."),
        ConsoleProtocol.RestartReason.RunDone => l.T("The run is done."),
        _ => l.T("The machine was to restart before and did not, so it restarts before anything else."),
    };

    // What a step of this kind does, as the heading of the run screen says it. A kind this list does not know is
    // said as the step's name alone.
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
        _ => null,
    };

    public static string Phase(Localizer l, ConsolePhase phase) => phase == ConsolePhase.WindowsPE
        ? l.T("In Windows PE")
        : l.T("In the installed Windows");

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
                "{image} is not signed for Secure Boot, and this machine has Secure Boot on. It starts only once Secure " +
                "Boot is turned off in the firmware setup, or your own key is enrolled.",
                ("image", image)),
            ConsoleProtocol.SecureBootProblem.MayNotStart => l.F(
                "DDT cannot tell whether {image} is signed for Secure Boot, and this machine has Secure Boot on. It may " +
                "not start until Secure Boot is turned off in the firmware setup, or your own key is enrolled.",
                ("image", image)),
            _ => l.F(
                "{image} is signed only under {cas}, which this machine's firmware does not trust. It starts only once " +
                "that CA is allowed, or Secure Boot is turned off in the firmware setup.",
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

    // Binary multiples with the labels Windows shows, one decimal at most, as the web writes them.
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

    // How the disk is attached, as the agent names StorageBusType, in the words storage tools use.
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

    // 12 hexadecimal digits as the web writes a MAC address: 3C:52:82:6A:1F:0B.
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

    // Capitals, as tags are set; the invariant culture keeps German letters such as ä as they are, capitalized.
    public static string Tag(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.ToUpperInvariant();
    }
}
