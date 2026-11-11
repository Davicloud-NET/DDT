// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.Applications;

// Installs the applications the server froze for the step, in its order, and goes on after the restarts they ask for.
// The checkpoint keeps each one's state by id, so a turn after a restart skips what is decided and finds an installer
// the restart cut off.
public sealed class InstallApplicationsStepRunner(
    IAgentServer server,
    RunSession session,
    Func<CancellationToken, Task> reportRunning,
    ApplicationInstaller installer,
    AgentLog log,
    TimeProvider timeProvider) : IStepKindRunner<InstallApplicationsStep>
{
    public const string Empty = "The step has no applications to install.";

    public const string GoesOn = "The machine restarts, then the step goes on with the next application.";

    public async Task<StepResult> RunAsync(InstallApplicationsStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        await reportRunning(cancellationToken).ConfigureAwait(false);

        AgentStepApplications answer = await ServerCallRules.CallAsync(
            call => server.GetRunStepApplicationsAsync(session.MachineId, session.Tokens.Token, session.Run.Id, step.Id, call),
            "the applications to install",
            log,
            timeProvider,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<AgentApplication> applications = answer.Applications ?? [];

        if (applications.Count == 0)
        {
            log.Information(Empty);
            context.Progress.Report(100);

            return StepResult.Done();
        }

        ApplicationProgress progress = ApplicationProgress.Read(context.Checkpoint.Saved, applications);
        bool restartDue = false;

        for (int index = 0; index < applications.Count; index++)
        {
            AgentApplication application = applications[index];

            if (progress.IsDecided(application.Id))
            {
                continue;
            }

            ApplicationInstall install = new(application, step.Id, context, progress.AttemptsOf(application.Id) + 1);
            ApplicationOutcome outcome = await DecideAsync(install, applications, progress, cancellationToken).ConfigureAwait(false);
            progress.Decide(application.Id, outcome.State);
            context.Progress.Report(Percent(Decided(applications, progress), applications.Count));

            if (outcome.State == ApplicationState.Failed && !application.ContinueOnFailure)
            {
                return StepResult.Failed(outcome.Message) with { ExitCode = outcome.ExitCode };
            }

            Log(application, outcome);
            restartDue = outcome.RestartDue;

            if (restartDue && applications.Skip(index + 1).Any(next => !progress.IsDecided(next.Id)))
            {
                log.Information(GoesOn);

                return StepResult.RestartAndContinue(progress.Write());
            }
        }

        return Close(applications, progress, restartDue);
    }

    private static int Percent(int decided, int total) => decided * 100 / total;

    private static int Decided(IReadOnlyList<AgentApplication> applications, ApplicationProgress progress) =>
        applications.Count(application => progress.IsDecided(application.Id));

    // The application it depends on that failed or was skipped, or null.
    private static AgentApplication? Missing(AgentApplication application, IReadOnlyList<AgentApplication> applications, ApplicationProgress progress)
    {
        IReadOnlyList<Guid> dependencies = application.DependsOn ?? [];

        return applications.FirstOrDefault(other =>
            dependencies.Contains(other.Id) && progress.StateOf(other.Id) is ApplicationState.Failed or ApplicationState.Skipped);
    }

    private async Task<ApplicationOutcome> DecideAsync(
        ApplicationInstall install,
        IReadOnlyList<AgentApplication> applications,
        ApplicationProgress progress,
        CancellationToken cancellationToken)
    {
        AgentApplication application = install.Application;

        if (Missing(application, applications, progress) is { } dependency)
        {
            string why = progress.StateOf(dependency.Id) == ApplicationState.Failed ? "failed" : "was skipped";

            return new ApplicationOutcome(ApplicationState.Skipped, $"Skipped {application.Name}, because it depends on {dependency.Name}, which {why}.");
        }

        int decided = Decided(applications, progress);
        ScaledProgress percent = new(install.Context.Progress, Percent(decided, applications.Count), Percent(decided + 1, applications.Count));

        async Task MarkStartedAsync()
        {
            progress.Start(application.Id, install.Attempt);
            await install.Context.Checkpoint.SaveAsync(progress.Write()).ConfigureAwait(false);
        }

        return await installer.InstallAsync(install, MarkStartedAsync, percent, cancellationToken).ConfigureAwait(false);
    }

    private void Log(AgentApplication application, ApplicationOutcome outcome)
    {
        if (outcome.State == ApplicationState.Failed)
        {
            log.Warning($"{outcome.Message} The step goes on, because {application.Name} may fail.");
        }
        else
        {
            log.Information(outcome.Message);
        }
    }

    // A closing line with the counts, a warning naming those that failed.
    private StepResult Close(IReadOnlyList<AgentApplication> applications, ApplicationProgress progress, bool restartDue)
    {
        int failed = progress.Count(ApplicationState.Failed);
        List<string> parts = [$"{progress.Count(ApplicationState.Installed)} installed"];

        if (progress.Count(ApplicationState.AlreadyThere) is > 0 and var there)
        {
            parts.Add($"{there} there already");
        }

        if (progress.Count(ApplicationState.Skipped) is > 0 and var skipped)
        {
            parts.Add($"{skipped} skipped");
        }

        if (failed > 0)
        {
            IEnumerable<string> names = applications
                .Where(application => progress.StateOf(application.Id) == ApplicationState.Failed)
                .Select(application => application.Name);
            parts.Add($"{failed} failed ({string.Join(", ", names)})");
            log.Warning($"Applications: {string.Join(", ", parts)}. The run goes on.");
        }
        else
        {
            log.Information($"Applications: {string.Join(", ", parts)}.");
        }

        return restartDue ? StepResult.RebootRequired() : StepResult.Done();
    }
}
