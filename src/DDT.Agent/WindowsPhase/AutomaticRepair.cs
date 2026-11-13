// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;
using System.Security;
using System.Text;
using DDT.Agent.Deployment;
using DDT.Agent.InstalledPhase;
using DDT.Agent.Sequences;

namespace DDT.Agent.WindowsPhase;

// Sets bootstatuspolicy of the boot entry Windows started from to IgnoreAllFailures. What it was, or none, is kept in
// the run's directory, written once and read when the run is over. currentPolicy reads the entry's value, null for none.
public sealed class AutomaticRepair(string windowsRoot, IToolRunner tools, Func<int?> currentPolicy, AgentLog log) : IAutomaticRepair
{
    public const string FileName = "boot-status-policy.txt";
    public const string None = "none";
    public const string IgnoreAllFailures = "IgnoreAllFailures";

    // BcdOSLoader_BootStatusPolicy's values from 0 on, as far as bcdedit has names for them.
    private static readonly string[] s_names = ["DisplayAllFailures", IgnoreAllFailures, "IgnoreShutdownFailures", "IgnoreBootFailures"];

    public AutomaticRepair(string windowsRoot, IToolRunner tools, AgentLog log)
        : this(windowsRoot, tools, BootStatusPolicy.Current, log)
    {
    }

    public static string BcdeditPath => Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

    public static string FilePathIn(string windowsRoot) => Path.Combine(RunFiles.DirectoryIn(windowsRoot), FileName);

    public async Task TurnOffAsync(CancellationToken cancellationToken)
    {
        string path = FilePathIn(windowsRoot);

        if (!File.Exists(path))
        {
            if (Before() is not { } before)
            {
                return;
            }

            try
            {
                await FileReplacement.ReplaceAsync(path, Encoding.ASCII.GetBytes(before), log, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                log.Warning($"Windows' Automatic Repair stays on: what it is set to could not be kept in {path} ({exception.Message}).");

                return;
            }

            log.Information($"Windows' Automatic Repair is off while the run goes on, so a failed start does not wait for a key (bootstatuspolicy was {before}).");
        }

        await BcdeditAsync(["/set", "{current}", "bootstatuspolicy", IgnoreAllFailures], "Windows' Automatic Repair could not be turned off", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PutBackAsync(CancellationToken cancellationToken)
    {
        string path = FilePathIn(windowsRoot);

        if (!File.Exists(path))
        {
            return;
        }

        string before;

        try
        {
            before = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Windows' Automatic Repair stays off: {path} could not be read ({exception.Message}).");

            return;
        }

        string[]? arguments = before == None
            ? ["/deletevalue", "{current}", "bootstatuspolicy"]
            : s_names.Contains(before, StringComparer.Ordinal) ? ["/set", "{current}", "bootstatuspolicy", before] : null;

        if (arguments is null)
        {
            log.Warning($"{path} names no bootstatuspolicy, so Windows' Automatic Repair stays off.");
        }
        else if (await BcdeditAsync(arguments, "Windows' Automatic Repair could not be put back as it was", cancellationToken).ConfigureAwait(false))
        {
            log.Information($"Windows' Automatic Repair is as it was before the run (bootstatuspolicy {before}).");
        }

        Leftovers.Delete(path, log);
    }

    // The name bcdedit sets the value with, None, or null when it stays as it is.
    private string? Before()
    {
        try
        {
            return currentPolicy() switch
            {
                null => None,
                int value when value >= 0 && value < s_names.Length => s_names[value],
                int value => Unnamed(value),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or Win32Exception or InvalidDataException)
        {
            log.Warning($"Windows' Automatic Repair stays on: what it is set to could not be read ({exception.Message}).");

            return null;
        }
    }

    private string? Unnamed(int value)
    {
        log.Warning($"Windows' Automatic Repair stays on: bootstatuspolicy is {value}, which bcdedit could not set back.");

        return null;
    }

    private async Task<bool> BcdeditAsync(IReadOnlyList<string> arguments, string failure, CancellationToken cancellationToken)
    {
        try
        {
            await tools.RunAsync(BcdeditPath, arguments, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"{failure} ({exception.Message}).");

            return false;
        }
    }
}
