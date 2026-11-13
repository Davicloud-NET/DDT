// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Sequences;
using DDT.Core.Sequences;

namespace DDT.Agent.WindowsFeatures;

// Enables the step's features in order. One that needs a restart restarts the machine after the last, and the step goes
// on after it. The checkpoint keeps the names that are done, one per line, so the turn after the restart only enables
// what is left.
public sealed class EnableFeaturesStepRunner(IWindowsFeatures features, AgentLog log) : IStepKindRunner<EnableFeaturesStep>
{
    public const string GoesOn = "The machine restarts to finish the features, then the step goes on.";

    public static string Unknown(string name) => $"DISM knows no feature named {name}.";

    public async Task<StepResult> RunAsync(EnableFeaturesStep step, StepContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<string> names = [.. step.Features.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];
        HashSet<string> done = Read(context.Checkpoint.Saved);
        bool restart = false;

        foreach (string name in names.Where(name => !done.Contains(name)))
        {
            context.Detail.Report(name);
            log.Information($"Enabling {name}.");

            FeatureOutcome outcome = await features.EnableAsync(name, cancellationToken).ConfigureAwait(false);

            if (outcome == FeatureOutcome.Unknown)
            {
                return StepResult.Failed(Unknown(name));
            }

            done.Add(name);
            await context.Checkpoint.SaveAsync(Write(done)).ConfigureAwait(false);
            context.Progress.Report(names.Count(done.Contains) * 100 / names.Count);
            restart |= outcome == FeatureOutcome.RestartNeeded;
            log.Information(outcome == FeatureOutcome.RestartNeeded ? $"Enabled {name}, which needs a restart." : $"Enabled {name}.");
        }

        if (restart)
        {
            log.Information(GoesOn);

            return StepResult.RestartAndContinue(Write(done));
        }

        context.Progress.Report(100);
        log.Information($"Features: {names.Count} enabled.");

        return StepResult.Done();
    }

    private static HashSet<string> Read(string? saved) =>
        new((saved ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static string Write(HashSet<string> done) => string.Join('\n', done.Order(StringComparer.OrdinalIgnoreCase));
}
