// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Sequences;

namespace DDT.Agent;

// What the agent of the Linux boot environment says of a run it finds handed over to the installed Linux.
public static class LinuxBootReturnTexts
{
    public const string Restarts = "The machine restarts into the installed Linux.";

    public const string StartsOverTaken =
        "A key was pressed, so the machine starts over here and registers as after any start. That ends the run that is " +
        "going on in the installed Linux.";

    public const string StartsOverNotTaken =
        "A key was pressed, so the machine starts over here and registers as after any start. That ends the run, which the " +
        "installed Linux had not taken over.";

    public static string RestartsSoon { get; } =
        string.Create(CultureInfo.InvariantCulture, $"The machine restarts into the installed Linux in {LinuxBootReturn.Wait.TotalSeconds:0} seconds.");

    public static string RestartsUnlessKey { get; } = $"{RestartsSoon} To start over here instead, press a key within that time: it ends the run.";

    public static string DisksUnread(string why) =>
        $"The disks cannot be read ({why}), so the agent cannot look for a run that was handed over to the installed Linux. " +
        "It goes on as after any start.";

    // Which seed the agent goes by when several disks hold a run, and why.
    public static string Believed(IReadOnlyList<HandedOverSeed> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        string disks = string.Join(", ", found.Select(seed => seed.Disk.Number.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));

        return $"{found.Count} disks hold a run that was handed over to the installed Linux: {disks}. The agent goes by disk " +
            $"{found[0].Disk.Number}, whose run was assigned last: a machine has one run at a time, so no older one can still go on. " +
            "The other seeds stay as they are.";
    }

    public static string Taken(HandedOverSeed seed) =>
        $"{Run(seed)} goes on in the installed Linux on disk {Disk(seed)}, which has taken it over. This start came from the network " +
        "instead of that disk, so the agent does not register and starts the installed Linux again.";

    public static string NotTaken(HandedOverSeed seed) =>
        $"{Run(seed)} was handed over to the installed Linux on disk {Disk(seed)}, and its token is still on that disk's cloud-init " +
        "seed: the installed Linux has not taken the run over. The agent registers with the token.";

    // removed says whether what the hand-over left on the seed is gone. Where it is not, a warning before this said why.
    public static string NoLongerRuns(HandedOverSeed seed, bool removed) =>
        $"The server no longer runs the run that was handed over on disk {Disk(seed)}: it was stopped, it ended, or the machine has " +
        $"had another since. {(removed ? "What the hand-over left on that disk's cloud-init seed is removed, and the agent" : "The agent")} " +
        "goes on as after any start.";

    public static string Again(int returns) =>
        $"The machine started from the network again before the installed Linux took the run over, for the {Ordinal(returns)} time. " +
        "A first start can fail or be cut short, so the agent puts the disk first again and restarts the machine into it. The " +
        $"{Ordinal(LinuxBootReturn.MaxReturns)} time, it gives the run up.";

    // Why the run fails, with what is known and where to look.
    public static string NeverTaken(int returns) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"The machine started from the network {returns} times after the run was handed over, and each time the run's token was still on ") +
        "the cloud-init seed, so the installed Linux never took the run over. The image has no cloud-init or no systemd, the " +
        "user-data keeps cloud-init from running DDT's script, the machine does not start from its disk, or that script failed. " +
        "Look at the machine's own screen while it starts from its disk, and in the installed Linux at /var/log/cloud-init-output.log.";

    public static string NotTold(string what, string why) => $"The server could not be told of {what} ({why}).";

    private static string Run(HandedOverSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        return seed.RunId is { } id ? $"Run {id:D} of this machine" : "A run of this machine";
    }

    private static string Disk(HandedOverSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        return seed.Disk.Number.ToString(CultureInfo.InvariantCulture);
    }

    private static string Ordinal(int number) => number switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        _ => string.Create(CultureInfo.InvariantCulture, $"{number}th"),
    };
}
