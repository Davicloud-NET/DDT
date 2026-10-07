// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;
using DDT.Contracts.Sequences;
using DDT.Server.Images;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Sequences that write a raw disk image, as a server before version 4 stored them: at version 2 or 3, for Windows PE.
public sealed class StoredLinuxSequenceTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static RunScriptStep OldScript(ScriptInterpreter interpreter) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"Prepare with {interpreter}",
        Phase = SequencePhase.WindowsPE,
        Interpreter = interpreter,
        Script = "echo %DDT_PHASE%",
        RebootExitCodes = [],
    };

    // Stores the document the way a server of that version wrote it, which no request does any more.
    private async Task<SequenceView> StoredAsync(int version, params SequenceStep[] steps)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView created = await administrator.CreatedSequenceAsync(SequenceRequests.ScriptOnly());
        string stored = SequenceRequests.Json(new SequenceDefinition(version, steps));

        await application.QueryAsync(async database =>
        {
            TaskSequence sequence = await database.TaskSequences.SingleAsync(s => s.Id == created.Id, TestContext.Current.CancellationToken);
            sequence.Definition = stored;

            return await database.SaveChangesAsync(TestContext.Current.CancellationToken);
        });

        return await RegisteredMachine.ReadAsync<SequenceView>(await administrator.GetAsync($"{SequenceRequests.Sequences}/{created.Id}"));
    }

    private async Task<SequenceStep[]> ImageAndSeedAsync()
    {
        Image image = await application.SeedRawImageAsync(RandomNumberGenerator.GetBytes(4096), name: $"noble {Guid.NewGuid():N}");

        return [.. SequenceRequests.Linux(image.Id).Steps];
    }

    [Fact]
    public async Task AnImageWithItsSeedGoesOnRunningWithNoChange()
    {
        SequenceStep[] steps = await ImageAndSeedAsync();

        SequenceView flat = await StoredAsync(2, steps);
        SequenceView tree = await StoredAsync(3, new GroupStep { Id = Guid.NewGuid(), Name = "Install", Steps = steps });

        foreach (SequenceView view in new[] { flat, tree })
        {
            Assert.Empty(view.Problems);
            Assert.Equal(4, view.Definition.Version);
            Assert.NotNull(view.NodePhases);
            Assert.All(view.NodePhases, node => Assert.Equal([SequencePhase.LinuxBoot], node.Phases));
        }

        // Saved as it was read, it is stored at the version it needs.
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(flat));
        Assert.Equal(4, saved.Definition.Version);
        Assert.Empty(saved.Problems);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AScriptWrittenForWindowsPEMakesItADraftThatSaysWhatToChange(int version)
    {
        SequenceStep[] steps = await ImageAndSeedAsync();
        RunScriptStep cmd = OldScript(ScriptInterpreter.Cmd);
        RunScriptStep powerShell = OldScript(ScriptInterpreter.PowerShell);

        SequenceView view = await StoredAsync(version, [cmd, .. steps, powerShell]);

        Assert.Equal(4, view.Definition.Version);
        Assert.Equal(
            [(cmd.Id, "phase", "sequence.windowsScriptInLinux"), (powerShell.Id, "phase", "sequence.windowsScriptInLinux")],
            view.Problems.Select(problem => (problem.StepId!.Value, problem.Field, problem.Code)));
        Assert.Equal(
            "This script was written for Windows PE, which no longer runs Linux sequences. Rewrite it for the shell and let it run in " +
            "the Linux boot environment.",
            view.Problems[0].Message);
        Assert.Equal(SequencePhase.WindowsPE, Assert.IsType<RunScriptStep>(view.Definition.Steps[0]).Phase);

        SignedInClient administrator = await application.AdministratorAsync();
        SequenceSummary summary = Assert.Single(
            await RegisteredMachine.ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences)),
            s => s.Id == view.Id);
        Assert.Equal(2, summary.ProblemCount);
    }

    // Rewritten for the shell, the same sequence runs again.
    [Fact]
    public async Task TheScriptRewrittenForTheShellMakesItRunAgain()
    {
        SequenceStep[] steps = await ImageAndSeedAsync();
        RunScriptStep cmd = OldScript(ScriptInterpreter.Cmd);
        SequenceView view = await StoredAsync(2, [cmd, .. steps]);
        SignedInClient administrator = await application.AdministratorAsync();

        RunScriptStep shell = cmd with { Phase = SequencePhase.LinuxBoot, Interpreter = ScriptInterpreter.Shell, Script = "echo \"$DDT_PHASE\"" };
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(
            await administrator.SaveSequenceAsync(view, view.Definition with { Steps = [shell, .. steps] }));

        Assert.Empty(saved.Problems);
        Assert.Equal(4, saved.Definition.Version);
    }

    // A sequence that installs Windows needs nothing of version 4 and keeps the version older agents run.
    [Fact]
    public async Task ASequenceThatInstallsWindowsKeepsItsVersion()
    {
        SequenceView view = await StoredAsync(1, SequenceRequests.ScriptOnly().Steps[0]);

        Assert.Equal(1, view.Definition.Version);
        Assert.Empty(view.Problems);
    }
}
