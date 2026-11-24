// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.BitLocker;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

// StepRunnerFixture with both BitLocker steps and fakes for manage-bde, the TPM, the domain and the policy key. The
// runners log to Console as well as to the server, and every checkpoint the step saves is in Saves. Events holds the
// fakes' calls in order, and "server has the key" once the server took it.
internal sealed class BitLockerRunFixture : StepRunnerFixture
{
    public static readonly EnableBitLockerStep Enable = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d1"), Name = "Enable BitLocker" };

    public static readonly PreprovisionBitLockerStep Preprovision = new() { Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000d2"), Name = "Pre-provision BitLocker" };

    public BitLockerRunFixture()
        : base([Preprovision, Enable])
    {
        BitLocker = new FakeBitLocker(Events);
        Policy = new FakeBitLockerPolicy(Events);
        ConsoleLog = new AgentLog(Time, Console);
    }

    public List<string> Events { get; } = [];

    public FakeBitLocker BitLocker { get; }

    public FakeTpmAndDomain Machine { get; } = new();

    public FakeWindowsFeatures Features { get; } = new();

    public FakeBitLockerPolicy Policy { get; }

    public StringWriter Console { get; } = new();

    public AgentLog ConsoleLog { get; }

    public List<string> Saves { get; } = [];

    public EnableBitLockerStepRunner EnableRunner =>
        new(Server, Session, ReportRunningAsync, new BitLockerTools(BitLocker, Machine, Features, Machine, Policy), ConsoleLog, Time);

    public PreprovisionBitLockerStepRunner PreprovisionRunner => new(BitLocker, Machine, Session, ConsoleLog);

    // What the server took last.
    public AgentRecoveryKey? Key { get; private set; }

    // The server takes the next key, and says so in Events.
    public void ServerTakesTheKey() =>
        Server.OnRunRecord(record =>
        {
            Key = Assert.IsType<AgentRecoveryKey>(record);
            Events.Add("server has the key");
        });

    public Task<StepResult> EnableAsync(EnableBitLockerStep? step = null, string? saved = null) =>
        EnableRunner.RunAsync(step ?? Enable, Checkpointed(SequencePhase.Windows, saved), TestContext.Current.CancellationToken);

    public Task<StepResult> PreprovisionAsync(PreprovisionBitLockerStep? step = null)
    {
        Partitioned();

        return PreprovisionRunner.RunAsync(step ?? Preprovision, Context(), TestContext.Current.CancellationToken);
    }

    // What the agent printed and what it sent as the run log.
    public async Task<string> EverythingLoggedAsync()
    {
        while (ConsoleLog.QueuedLines > 0)
        {
            await ConsoleLog.FlushAsync(Server, MachineId, "session", TestContext.Current.CancellationToken);
        }

        return string.Join('\n', [Console.ToString(), .. Server.SentLines.Select(line => line.Message)]);
    }

    private StepContext Checkpointed(SequencePhase phase, string? saved) => Context(phase) with
    {
        Checkpoint = new StepCheckpoint(saved, 0, saved is not null, progress =>
        {
            Saves.Add(progress);

            return Task.CompletedTask;
        }),
    };
}
