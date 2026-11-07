// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;

namespace DDT.Core.Sequences;

// The rules of a Run script step. StepRules already refused a script in a Windows phase of a Linux sequence.
internal static class ScriptRules
{
    // The highest exit code a shell passes on.
    private const int MaxShellExitCode = 255;

    // stateInMemory is a boot phase before any partition. A restart there loses the run, and a package has no place.
    public static void Check(RunScriptStep script, SequenceKind kind, bool stateInMemory, Action<string?, ServerMessage> add)
    {
        bool fits = CheckWhere(script, kind, add);

        if (string.IsNullOrWhiteSpace(script.Script))
        {
            add("script", ServerMessages.SequenceScriptEmpty.With());
        }
        else if (Encoding.UTF8.GetByteCount(script.Script) > SequenceValidator.MaxScriptBytes)
        {
            add("script", ServerMessages.SequenceScriptTooLarge.With("max", SequenceValidator.MaxScriptBytes / 1024));
        }

        if (script.TimeoutMinutes is < 1 or > SequenceValidator.MaxTimeoutMinutes)
        {
            add("timeoutMinutes", ServerMessages.SequenceScriptTimeout.With("max", SequenceValidator.MaxTimeoutMinutes));
        }

        CheckExitCodes(script, stateInMemory, add);

        // These name the boot environment of the sequence's kind, which a script in the other kind's phase is not in.
        if (!stateInMemory || !fits)
        {
            return;
        }

        if (script.PackageId is not null)
        {
            add(
                "packageId",
                kind == SequenceKind.Linux
                    ? ServerMessages.SequencePackageWithRawImage.With()
                    : ServerMessages.SequencePackageBeforePartition.With());
        }

        if (script.RebootExitCodes is { Count: > 0 })
        {
            add("rebootExitCodes", (kind == SequenceKind.Linux ? ServerMessages.SequenceRestartWithRawImage : ServerMessages.SequenceRestartBeforePartition).With());
        }
    }

    // A phase of the other kind is the one thing to change, so the interpreter is only judged in a phase that fits.
    // False for such a phase.
    private static bool CheckWhere(RunScriptStep script, SequenceKind kind, Action<string?, ServerMessage> add)
    {
        if (!Enum.IsDefined(script.Phase))
        {
            add("phase", ServerMessages.SequenceScriptPhase.With());
        }
        else if (SequenceKinds.Of(script.Phase) == SequenceKind.Linux && kind != SequenceKind.Linux)
        {
            add("phase", ServerMessages.SequenceLinuxPhaseWithoutRawImage.With());

            return false;
        }

        if (!Enum.IsDefined(script.Interpreter))
        {
            add("interpreter", ServerMessages.SequenceScriptInterpreter.With());
        }
        else if (script.Interpreter == ScriptInterpreter.Shell != (kind == SequenceKind.Linux))
        {
            add("interpreter", (kind == SequenceKind.Linux ? ServerMessages.SequenceLinuxNeedsShell : ServerMessages.SequenceShellNeedsLinux).With());
        }

        return true;
    }

    private static void CheckExitCodes(RunScriptStep script, bool stateInMemory, Action<string?, ServerMessage> add)
    {
        bool shell = script.Interpreter == ScriptInterpreter.Shell;

        if (script.SuccessExitCodes is not { Count: >= 1 and <= SequenceValidator.MaxExitCodes })
        {
            add("successExitCodes", ServerMessages.SequenceSuccessExitCodes.With("max", SequenceValidator.MaxExitCodes));
        }
        else if (shell && !script.SuccessExitCodes.All(InShellRange))
        {
            add("successExitCodes", ServerMessages.SequenceShellExitCodes.With());
        }

        // An empty list of restart codes is allowed. A script whose 3010 means success lists it among the success codes
        // instead.
        if (script.RebootExitCodes is not { Count: <= SequenceValidator.MaxExitCodes })
        {
            add("rebootExitCodes", ServerMessages.SequenceRebootExitCodes.With("max", SequenceValidator.MaxExitCodes));

            return;
        }

        if (script.SuccessExitCodes is not null
            && script.SuccessExitCodes.Intersect(script.RebootExitCodes).ToArray() is { Length: > 0 } both)
        {
            add("rebootExitCodes", ServerMessages.SequenceExitCodeMeansBoth.With("codes", string.Join(", ", both)));
        }

        // Where no code may ask for a restart, that is the problem to show, not the code's size.
        if (shell && !stateInMemory && !script.RebootExitCodes.All(InShellRange))
        {
            add("rebootExitCodes", ServerMessages.SequenceShellRebootExitCodes.With());
        }
    }

    private static bool InShellRange(int code) => code is >= 0 and <= MaxShellExitCode;
}
