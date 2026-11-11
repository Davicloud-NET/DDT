// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Applications;

// The variables an installer gets, named as a script's are, and the run's values as DDT_VAR_<Name>.
public static class ApplicationEnvironment
{
    public const string Name = "DDT_APPLICATION";
    public const string Package = "DDT_PACKAGE";

    // package is the unpacked files' folder, or null without files.
    public static IReadOnlyDictionary<string, string> Of(ApplicationInstall install, string? package)
    {
        ArgumentNullException.ThrowIfNull(install);

        Dictionary<string, string> environment = new(StringComparer.OrdinalIgnoreCase)
        {
            ["DDT_RUN_ID"] = install.Context.RunId.ToString("D"),
            ["DDT_STEP_ID"] = install.StepId.ToString("D"),
            [Name] = install.Application.Name,
        };

        if (package is not null)
        {
            environment[Package] = package;
        }

        foreach ((string name, string value) in RunScriptStepRunner.UserVariables(install.Context))
        {
            environment[RunScriptStepRunner.VariablePrefix + name] = value;
        }

        return environment;
    }
}
