// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using Xunit;
using static DDT.Agent.Tests.TestApplications;

namespace DDT.Agent.Tests;

// The checkpoint of an Install applications step.
public sealed class ApplicationProgressTests
{
    [Fact]
    public void KeepsEachApplicationsStateByIdAndTheAttemptsOfAStartedOne()
    {
        ApplicationProgress progress = ApplicationProgress.Read(null, [Runtime, Tool, Viewer]);
        progress.Decide(Runtime.Id, ApplicationState.AlreadyThere);
        progress.Start(Tool.Id, 1);

        string written = progress.Write();

        Assert.Equal(
            $$$"""{"{{{Runtime.Id:D}}}":{"state":"AlreadyThere"},"{{{Tool.Id:D}}}":{"state":"Started","attempts":1}}""",
            written);
        ApplicationProgress read = ApplicationProgress.Read(written, [Runtime, Tool, Viewer]);
        Assert.Equal(
            (ApplicationState.AlreadyThere, ApplicationState.Started, 1, ApplicationState.Pending),
            (read.StateOf(Runtime.Id), read.StateOf(Tool.Id), read.AttemptsOf(Tool.Id), read.StateOf(Viewer.Id)));
        Assert.Equal((true, false, false), (read.IsDecided(Runtime.Id), read.IsDecided(Tool.Id), read.IsDecided(Viewer.Id)));
    }

    [Fact]
    public void ADecidedApplicationHasNoAttemptsLeftInTheCheckpoint()
    {
        ApplicationProgress progress = ApplicationProgress.Read(null, [Tool]);
        progress.Start(Tool.Id, 2);
        progress.Decide(Tool.Id, ApplicationState.Installed);

        Assert.Equal($$$"""{"{{{Tool.Id:D}}}":{"state":"Installed"}}""", progress.Write());
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"x\":1}")]
    [InlineData("{\"0193a4b2-0000-7000-8000-00000000a002\":{\"state\":\"Gone\"}}")]
    public void ACheckpointThatCannotBeReadCountsAsNone(string saved)
    {
        ApplicationProgress progress = ApplicationProgress.Read(saved, [Tool]);

        Assert.Equal((ApplicationState.Pending, "{}"), (progress.StateOf(Tool.Id), progress.Write()));
    }
}
