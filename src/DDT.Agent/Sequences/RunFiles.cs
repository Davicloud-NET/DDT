// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;
using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.Sequences;

// A run's files in run under its directory, <Windows volume>\DDT: state.json, the state the engine saves after every
// change, and token, the run token that resumes the run after a restart. Each is replaced whole, through a temporary
// file written through to the disk, so a power loss leaves the old file or the new one, never part of one. The token
// is a secret: it is never logged.
public sealed class RunFiles(string runDirectory, AgentLog log)
{
    public string StatePath => Path.Combine(runDirectory, "run", "state.json");

    public string TokenPath => Path.Combine(runDirectory, "run", "token");

    // In the installed Windows, the report of how the run ended, which stays with the token until the server has it.
    public string FinalReportPath => Path.Combine(runDirectory, "run", "final-report.json");

    // The files of a run whose Windows volume is at windowsRoot, such as C:\.
    public static RunFiles In(string windowsRoot, AgentLog log) => new(Path.Combine(windowsRoot, "DDT"), log);

    // Where a Windows volume keeps a run's state, if it has one.
    public static string StatePathIn(string windowsRoot) => Path.Combine(windowsRoot, "DDT", "run", "state.json");

    public Task SaveStateAsync(SequenceState state, CancellationToken cancellationToken) =>
        ReplaceAsync(StatePath, JsonSerializer.SerializeToUtf8Bytes(state, AgentJsonContext.Default.SequenceState), cancellationToken);

    public Task SaveTokenAsync(string token, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        return ReplaceAsync(TokenPath, Encoding.ASCII.GetBytes(token), cancellationToken);
    }

    public Task SaveFinalReportAsync(AgentRunReport report, CancellationToken cancellationToken) =>
        ReplaceAsync(FinalReportPath, JsonSerializer.SerializeToUtf8Bytes(report, AgentJsonContext.Default.AgentRunReport), cancellationToken);

    // Null when there is none, and after a warning when it cannot be read.
    public async Task<AgentRunReport?> LoadFinalReportAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FinalReportPath))
        {
            return null;
        }

        try
        {
            byte[] json = await File.ReadAllBytesAsync(FinalReportPath, cancellationToken).ConfigureAwait(false);

            return JsonSerializer.Deserialize(json, AgentJsonContext.Default.AgentRunReport);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            log.Warning($"{FinalReportPath} cannot be read ({exception.Message}).");

            return null;
        }
    }

    // Null when there is no state, and after a warning when it cannot be read: a run that cannot go on is no run.
    public async Task<SequenceState?> LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(StatePath))
        {
            return null;
        }

        try
        {
            byte[] json = await File.ReadAllBytesAsync(StatePath, cancellationToken).ConfigureAwait(false);
            SequenceState? state = JsonSerializer.Deserialize(json, AgentJsonContext.Default.SequenceState);

            if (state is { Definition.Steps: { } steps, Steps: { } states, Variables: not null }
                && steps.Count == states.Count
                && state.NextIndex >= 0
                && state.NextIndex <= steps.Count)
            {
                return state;
            }

            log.Warning($"{StatePath} does not describe a run this agent can go on with, so it is ignored.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            log.Warning($"{StatePath} cannot be read ({exception.Message}), so it is ignored.");
        }

        return null;
    }

    // Null when there is none.
    public async Task<string?> LoadTokenAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(TokenPath))
        {
            return null;
        }

        try
        {
            string token = (await File.ReadAllTextAsync(TokenPath, Encoding.ASCII, cancellationToken).ConfigureAwait(false)).Trim();

            return token.Length > 0 ? token : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning($"The run token in {TokenPath} cannot be read ({exception.Message}).");

            return null;
        }
    }

    // The token goes first: without it the rest can no longer act as the machine.
    public void Discard()
    {
        Leftovers.Delete(TokenPath, log);
        Leftovers.Delete(StatePath, log);
        Leftovers.Delete(FinalReportPath, log);
        Leftovers.Delete(Path.Combine(runDirectory, "run"), log);
    }

    private static async Task ReplaceAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";

        FileStream file = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, FileOptions.WriteThrough);

        await using (file.ConfigureAwait(false))
        {
            await file.WriteAsync(content, cancellationToken).ConfigureAwait(false);
            file.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }
}
