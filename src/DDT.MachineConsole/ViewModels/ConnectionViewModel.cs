// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;
using DDT.MachineConsole.Texts;

namespace DDT.MachineConsole.ViewModels;

// Starting and connecting: the agent checks for a newer agent and registers. When a request fails, the screen says
// how far it got, in the stages of a connection, with the agent's own words and what to look at.
public sealed class ConnectionViewModel(Localizer localizer) : StageViewModel(localizer)
{
    private static readonly ConnectionStage[] s_stages =
    [
        ConnectionStage.NameLookup,
        ConnectionStage.Connection,
        ConnectionStage.SecureConnection,
        ConnectionStage.Answer,
    ];

    private ConsoleState? _state;

    public ConsoleStage Stage => _state?.Stage ?? ConsoleStage.Starting;

    public string Title => Stage == ConsoleStage.Starting ? T("Starting the agent") : T("Connecting to the server");

    public Tag Tag => HasProblem
        ? Tag.Of(T("Not reached"), TagTone.Attention)
        : Tag.Of(Say.Stage(L, Stage), TagTone.Run);

    public string ServerLabel => T("Server");

    public string Server => _state is null ? string.Empty : Say.Host(_state.Server.Address);

    public string Address => _state?.Server.Address ?? string.Empty;

    public string Explanation => Stage == ConsoleStage.Starting
        ? T("The agent checks whether the server has a newer agent, then registers this machine with the server.")
        : T("The agent registers this machine with the server. It needs nothing from you.");

    public bool HasProblem => _state?.Server.Problem is not null;

    public string ProblemLabel => T("What the agent saw");

    // The agent's words, as it logged them.
    public string Problem => _state?.Server.Problem ?? string.Empty;

    public string Failures => _state?.Server.Failures switch
    {
        null or 0 => string.Empty,
        1 => T("The last request failed. The agent tries again by itself."),
        int count => F("The last {count} requests failed. The agent tries again by itself.", ("count", L.Number(count))),
    };

    public bool HasAdvice => _state?.Server.FailedStage is not null;

    public string Advice => _state?.Server.FailedStage is { } stage ? Say.ConnectionAdvice(L, stage) : string.Empty;

    // The stages of a request, up to the one that failed; empty while nothing failed or the agent cannot tell where.
    public IReadOnlyList<ConnectionStageItem> Stages =>
        _state?.Server.FailedStage is { } failed
            ? [.. s_stages.Select(stage => new ConnectionStageItem(
                Say.ConnectionStage(L, stage),
                stage < failed ? ConnectionStageState.Passed : stage == failed ? ConnectionStageState.Failed : ConnectionStageState.NotReached,
                stage < failed ? T("Passed") : stage == failed ? T("Failed") : T("Not reached")))]
            : [];

    public override void Update(ConsoleState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _state = state;
        RaiseAll();
    }
}

public enum ConnectionStageState
{
    Passed,
    Failed,
    NotReached,
}

public sealed record ConnectionStageItem(string Name, ConnectionStageState State, string StateText)
{
    public bool IsFailed => State == ConnectionStageState.Failed;

    // The stages show as modules of the rail: passed as done, the failed one hatched, the rest empty.
    public ConsoleStepState ModuleState => State switch
    {
        ConnectionStageState.Passed => ConsoleStepState.Done,
        ConnectionStageState.Failed => ConsoleStepState.Failed,
        _ => ConsoleStepState.Pending,
    };
}
