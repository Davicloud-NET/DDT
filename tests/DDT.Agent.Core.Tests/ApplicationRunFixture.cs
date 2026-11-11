// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Applications;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Applications;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Tests;

// StepRunnerFixture in the installed Windows, whose root is C under Tools.Root, with an Install applications step. The
// detector and the Command installer are fakes. Events holds the checkpoint's saves ("save") and the installs in order,
// and Saves what each save kept.
internal sealed class ApplicationRunFixture : StepRunnerFixture
{
    public static readonly InstallApplicationsStep Step = new()
    {
        Id = Guid.Parse("0193a4b2-0000-7000-8000-0000000000c1"),
        Name = "Install applications",
        Applications = [new ApplicationReference(TestApplications.Tool.Id, ApplicationReferenceKind.Application)],
    };

    public ApplicationRunFixture()
        : this(new FakeFreeSpace())
    {
    }

    private ApplicationRunFixture(FakeFreeSpace freeSpace)
        : base([Step], freeSpace: freeSpace)
    {
        FreeSpace = freeSpace;
        Installer = new RecordingApplicationInstaller(Events);
        Session.RunRoot = Path.Combine(Tools.Root, "C");
        Directory.CreateDirectory(RunDirectory);
    }

    public FakeFreeSpace FreeSpace { get; }

    public FakeApplicationDetector Detector { get; } = new();

    public RecordingApplicationInstaller Installer { get; }

    public List<string> Events { get; } = [];

    public List<string> Saves { get; } = [];

    public string RunDirectory => RunFiles.DirectoryIn(Path.Combine(Tools.Root, "C"));

    public InstallApplicationsStepRunner Runner => new(
        Server,
        Session,
        ReportRunningAsync,
        new ApplicationInstaller(Detector, [Installer], new ApplicationFolders(Downloads, Session, WorkDirectory), Log),
        Log,
        Time);

    // The server answers the next few requests for the step's applications with these.
    public ApplicationRunFixture Serve(params AgentApplication[] applications)
    {
        for (int answer = 0; answer < 4; answer++)
        {
            Server.OnRunApplications(_ => new AgentStepApplications(applications));
        }

        return this;
    }

    // A turn of the step with what an earlier turn saved. values are the run's values.
    public StepContext Turn(string? saved = null, bool interrupted = false, IReadOnlyDictionary<string, string>? values = null) =>
        Context(SequencePhase.Windows, values: values) with
        {
            Checkpoint = new StepCheckpoint(saved, 0, interrupted, progress =>
            {
                Events.Add("save");
                Saves.Add(progress);

                return Task.CompletedTask;
            }),
        };

    public Task<StepResult> RunAsync(StepContext? context = null) => Runner.RunAsync(Step, context ?? Turn(), Xunit.TestContext.Current.CancellationToken);
}
