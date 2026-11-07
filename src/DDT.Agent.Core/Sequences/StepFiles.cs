// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Where a script step keeps its files in a run's directory, and how the agent names those it writes itself.
internal static class StepFiles
{
    public const string ScriptsFolder = "scripts";
    public const string PackagesFolder = "packages";
    public const string CacheFolder = "cache";

    // After the step's id: its shell script, and the file its script sets variables in.
    public const string ShellExtension = ".sh";
    public const string VariablesExtension = ".variables";

    // Whether the name is one the agent gave a step's file in the installed Linux. Such a file may hold a secret.
    public static bool IsOwn(string file) =>
        Path.GetExtension(file) is ShellExtension or VariablesExtension && Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "D", out _);
}
