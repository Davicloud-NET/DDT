// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;
using Xunit;

namespace DDT.Agent.Tests;

public sealed class LocalRunTests : IDisposable
{
    private static readonly GroupStep s_group = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000a4"),
        Name = "Windows",
        Steps = [TestRuns.Apply, TestRuns.Unattend],
    };

    private static readonly SequenceDefinition s_definition = new(SequenceDefinition.CurrentVersion, [TestRuns.Partition, s_group]);

    private readonly string _windows = Directory.CreateTempSubdirectory("ddt-local-run-").FullName;
    private readonly AgentLog _log = new(new ImmediateTimeProvider(), TextWriter.Null);

    public void Dispose() => Directory.Delete(_windows, recursive: true);

    // The answer file holds passwords, and its step sits inside a group here, after the entries of the group and the
    // image.
    [Theory]
    [InlineData(StepState.Running, false)]
    [InlineData(StepState.Failed, false)]
    [InlineData(StepState.Pending, true)]
    [InlineData(StepState.Skipped, true)]
    public void DeletesTheAnswerFileOnceItsStepInsideTheTreeStarted(StepState state, bool kept)
    {
        string file = UnattendFile.PathIn(_windows);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "<unattend />");
        SequenceState start = SequenceStates.Start(TestRuns.RunId, s_definition);
        SequenceState run = start with { Steps = [.. start.Steps.Take(3), start.Steps[3] with { State = state, Pass = 1 }] };

        LocalRun.DeleteAnswerFile(run, _windows, _log);

        Assert.Equal(kept, File.Exists(file));
    }
}
