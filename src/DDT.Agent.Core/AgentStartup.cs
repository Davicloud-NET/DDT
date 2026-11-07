// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography.X509Certificates;
using System.Text;
using DDT.Agent.Consoles;

namespace DDT.Agent;

// What every agent does before its system's own parts: the legal texts, the options, the consoles and the update.
// Then the platform does the run, or a dry run of it.
public sealed class AgentStartup(AgentPlatform platform)
{
    public const string LicensesArgument = "--licenses";

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains(LicensesArgument))
        {
            WriteLicenses();

            return AgentExitCodes.Stopped;
        }

        platform.WriteStartupNotices(Console.Out);

        if (!AgentOptions.TryParse(args, platform.BootEnvironment, out AgentOptions? options, out string error))
        {
            Console.Error.WriteLine(error);

            return AgentExitCodes.ConfigurationError;
        }

        using CancellationTokenSource stop = new();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stop.Cancel();
        };

        string version = typeof(AgentLoop).Assembly.GetName().Version?.ToString(3) ?? "unknown";

        using HttpAgentServer server = ServerOf(options!.ServerUrl, options.RootCertificate);

        AgentLog log = new(TimeProvider.System, Console.Out);
        TextMachineConsole text = new(new ConsoleSignInPrompt(log, TimeProvider.System, options.KeyboardLayout), log);

        // If there's a graphical console, it starts before anything else, so it shows the update check and the
        // connection too. The text console stays underneath it and takes over if it goes away.
        string? consolePath = ConsolePathFor(options, log);
        await using PipeMachineConsole? graphical = consolePath is null
            ? null
            : new PipeMachineConsole(text, new ProcessConsoleLauncher(consolePath), log, version);
        log.MachineConsole = graphical;
        ConsoleStatus status = new(graphical ?? (IMachineConsole)text, version, options.ServerUrl, options.KeyboardLayout, options.DryRun);
        graphical?.Start();

        StartedAgent agent = new(options, server, log, status, graphical, consolePath, version);

        if (options.DryRun)
        {
            log.Information($"Dry run {options.DryRunId}: this computer stands in for a fake machine and nothing on it is changed.");
        }

        if (await BeforeRunAsync(agent, args, stop.Token).ConfigureAwait(false) is { } exitCode)
        {
            return exitCode;
        }

        return options.DryRun
            ? await platform.DryRunAsync(agent, stop.Token).ConfigureAwait(false)
            : await platform.RunAsync(agent, stop.Token).ConfigureAwait(false);
    }

    private HttpAgentServer ServerOf(Uri url, X509Certificate2? rootCertificate) =>
        new(url, rootCertificate) { Environment = platform.BootEnvironment, ReleasePlatform = platform.ReleasePlatform };

    // Null for an executable that starts no graphical console, which says so of one --console names.
    private string? ConsolePathFor(AgentOptions options, AgentLog log)
    {
        if (platform.StartsGraphicalConsole)
        {
            return PipeMachineConsole.PathFor(options, Console.IsInputRedirected, AppContext.BaseDirectory);
        }

        if (options.ConsolePath is not null)
        {
            log.Information($"This agent starts no graphical console, so {options.ConsolePath} is not started and the text console stays.");
        }

        return null;
    }

    // Console.Out encodes for the console's code page, 437 in an English WinPE, and replaces characters it lacks,
    // such as the copyright sign. When output is redirected, the texts go out in UTF-8, so a file gets them unchanged.
    private void WriteLicenses()
    {
        if (Console.IsOutputRedirected)
        {
            using StreamWriter output = new(Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            platform.WriteLicenses(output);
        }
        else
        {
            platform.WriteLicenses(Console.Out);
        }
    }

    // Waits for what the system needs before the first call to the server, then looks for a newer agent. The exit code
    // is null when this agent goes on to its run.
    private async Task<int?> BeforeRunAsync(StartedAgent agent, string[] args, CancellationToken cancellationToken)
    {
        if (platform.ReadyAsync is { } ready && !agent.Options.DryRun && !await ready(agent, cancellationToken).ConfigureAwait(false))
        {
            return AgentExitCodes.Stopped;
        }

        return await UpdateAsync(agent, args, cancellationToken).ConfigureAwait(false);
    }

    // A dry run changes nothing on the computer it runs on, and a build run from source would swap itself for the
    // published agent, so neither updates. An agent started by an update never updates again. The exit code is the
    // newer agent's, and null when this one goes on.
    private async Task<int?> UpdateAsync(StartedAgent agent, string[] args, CancellationToken cancellationToken)
    {
        if (agent.Options.DryRun || !platform.IsPublished)
        {
            agent.Log.Information("Not checking for a newer agent in a dry run or an agent that was not published.");

            return null;
        }

        if (agent.Options.NoUpdate)
        {
            return null;
        }

        if (platform.NoUpdateReason is { } reason)
        {
            agent.Log.Information(reason);

            return null;
        }

        string current = await ReleaseFiles.Sha256Async(Environment.ProcessPath!, cancellationToken).ConfigureAwait(false);
        ProcessAgentRelauncher relauncher = new(() =>
        {
            agent.Server.CloseConnections();
            agent.Graphical?.Close();
        });
        // A console passed with --console is kept. The one next to the agent is replaced by the server's version.
        AgentUpdate update = new(
            agent.Server,
            relauncher,
            agent.Log,
            TimeProvider.System,
            new RunningAgent(current, AppContext.BaseDirectory, args, agent.Options.ConsolePath is null ? agent.ConsolePath : null)
            {
                Environment = platform.BootEnvironment,
            },
            agent.Status);

        return await update.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
