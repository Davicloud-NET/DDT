// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A run that an earlier start of the agent left on the Windows volume at WindowsRoot: its state, and the run token kept
// with it, which resumes the run when the server still runs it.
public sealed record LocalRun(string WindowsRoot, RunFiles Files, SequenceState State, string? RunToken)
{
    // Null when the volume holds no state this agent can go on with.
    public static async Task<LocalRun?> LoadAsync(string windowsRoot, AgentLog log, CancellationToken cancellationToken)
    {
        RunFiles files = RunFiles.In(windowsRoot, log);
        SequenceState? state = await files.LoadStateAsync(cancellationToken).ConfigureAwait(false);

        return state is null ? null : new LocalRun(windowsRoot, files, state, await files.LoadTokenAsync(cancellationToken).ConfigureAwait(false));
    }

    // The answer file holds passwords. It may be there once its step started, even if the step never finished: a step
    // the engine found interrupted is Failed by then. The state has an entry per node of the tree, so the step may sit
    // inside a group or an IF.
    public static void DeleteAnswerFile(SequenceState state, string windowsRoot, AgentLog log)
    {
        ArgumentNullException.ThrowIfNull(state);

        bool started = SequenceTree.Nodes(state.Definition)
            .Zip(state.Steps)
            .Any(pair => pair.First is WriteUnattendStep && pair.Second.State is not (StepState.Pending or StepState.Skipped));

        if (started)
        {
            Leftovers.Delete(UnattendFile.PathIn(windowsRoot), log);
        }
    }

    // For a run that is over: the token first, so nothing left can act as the machine, then the state and the answer
    // file.
    public void Discard(AgentLog log)
    {
        Files.Discard();
        DeleteAnswerFile(State, WindowsRoot, log);
    }
}
