// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// What the agent of the Linux boot environment says of a run it finds handed over to the installed Linux.
public static class LinuxBootReturnTexts
{
    public const string Restarts = "The machine restarts into the installed Linux.";

    public const string StartsOverTaken =
        $"{StartsOver} That ends the run that is going on in the installed Linux.";

    public const string StartsOverNotTaken =
        $"{StartsOver} That ends the run, which the installed Linux had not taken over.";

    private const string StartsOver =
        $"{StartOverQuestion.Word} was typed, so the machine starts over here: the agent registers without the run's token and tells the " +
        "server that someone at the machine chose to.";

    public static string RestartsSoon { get; } =
        string.Create(CultureInfo.InvariantCulture, $"The machine restarts into the installed Linux in {LinuxBootReturn.Wait.TotalSeconds:0} seconds.");

    public static string NotAsked { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"The server did not answer within {LinuxBootReturn.AskTime.TotalSeconds:0} seconds, so the agent could not ask it whether the run " +
        $"still goes on in the installed Linux. It goes by what the disk says.");

    public static string DisksUnread(string why) =>
        $"The disks cannot be read ({why}), so the agent cannot look for a run that was handed over to the installed Linux. " +
        "It goes on as after any start.";

    // Which seed the agent goes by when several disks hold a run, and why.
    public static string Believed(IReadOnlyList<HandedOverSeed> found)
    {
        ArgumentNullException.ThrowIfNull(found);

        string disks = string.Join(", ", found.Select(seed => seed.Disk.Number.ToString(CultureInfo.InvariantCulture)).Order(StringComparer.Ordinal));
        string held = $"{found.Count} disks hold a run that was handed over to the installed Linux: {disks}.";

        return found[0] is { IsTaken: true, RunId: null }
            ? $"{held} The seed of disk {found[0].Disk.Number} has no token and names no run the agent can read, so the agent cannot " +
              "tell which run was assigned last. It asks the server, and goes by the disk whose seed names the run the server says " +
              $"goes on, or else by disk {found[0].Disk.Number}. The other seeds stay as they are."
            : $"{held} The agent goes by disk {found[0].Disk.Number}, whose run was assigned last: a machine has one run at a time, " +
              "so no older one can still go on. The other seeds stay as they are.";
    }

    // The seed holds no token. Its state names the run, or names none, which says even less of what the installed Linux has.
    public static string Asks(HandedOverSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        if (seed.IsUnread)
        {
            return $"What the cloud-init seed of disk {Disk(seed)} holds is not known. A registration would end a run that goes on in the " +
                "installed Linux, so the agent first asks the server whether one of this machine goes on there.";
        }

        return seed.RunId is { } id
            ? $"Run {id:D} of this machine was handed over to the installed Linux on disk {Disk(seed)}, and that disk's cloud-init seed " +
              "holds its state without its token, as it does once the installed Linux has taken the run over. This start came from the " +
              "network instead of that disk, so the agent asks the server whether the run still goes on there."
            : $"The cloud-init seed of disk {Disk(seed)} holds what a hand-over to the installed Linux left, with no token and no state " +
              "that names a run. That alone says nothing of a run that goes on, so the agent asks the server whether one of this machine " +
              "goes on in the installed Linux.";
    }

    public static string GoesOn(HandedOverSeed seed, AgentInstalledRun installed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(installed);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"The server says that run {installed.RunId:D} of this machine goes on in the installed Linux, and has changed nothing. This is " +
            $"start {installed.Detours} of {installed.MaxDetours} from the network in a row without a call from the installed Linux in " +
            $"between. At start {installed.MaxDetours}, the server ends the run. The agent starts disk {Disk(seed)} again.");
    }

    public static string NotTaken(HandedOverSeed seed) =>
        $"{Run(seed)} was handed over to the installed Linux on disk {Disk(seed)}, and its token is still on that disk's cloud-init " +
        "seed: the installed Linux has not taken the run over. The agent registers with the token.";

    // removed says whether what the hand-over left on the seed is gone. Where it is not, a warning before this said why.
    // A seed that could not be read stays as it is, and nothing is known of a run on it.
    public static string NoLongerRuns(HandedOverSeed seed, bool removed)
    {
        ArgumentNullException.ThrowIfNull(seed);

        return seed.IsUnread
            ? $"The server says that no run of this machine goes on in the installed Linux. The cloud-init seed of disk {Disk(seed)}, which " +
              "the agent cannot read, stays as it is, and the agent goes on as after any start."
            : $"The server no longer runs the run that was handed over on disk {Disk(seed)}: it was stopped, it ended, or the machine has " +
              $"had another since. {(removed ? "What the hand-over left on that disk's cloud-init seed is removed, and the agent" : "The agent")} " +
              "goes on as after any start.";
    }

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
