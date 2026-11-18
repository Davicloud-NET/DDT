// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Servicing;

// Windows PE's DISM, which services the applied Windows with that Windows' own servicing stack. It logs at level 2:
// at the default level one cumulative update wrote a log of 542 MB.
public sealed class DismOfflineServicing(IToolRunner tools, string dismPath, AgentLog log) : IOfflineServicing
{
    // ERROR_SUCCESS_REBOOT_REQUIRED.
    public const int RestartNeeded = 3010;

    // CBS_E_NOT_APPLICABLE.
    public const int NotApplicable = unchecked((int)0x800F081E);

    public const int LogTailLines = 40;

    // A cumulative update took 8.7 minutes on a test VM.
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(2);

    public static readonly TimeSpan VersionTimeout = TimeSpan.FromMinutes(15);

    public async Task<ServicingAnswer> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(percent);

        DismLines lines = new(percent, startsOver: packages.Any(package => package.EndsWith(".msu", StringComparison.OrdinalIgnoreCase)));
        int exitCode = await tools.RunForExitCodeAsync(
            dismPath,
            [$"/Image:{image.Windows}", "/Add-Package", .. packages.Select(package => $"/PackagePath:{package}"), .. Common(image)],
            new ToolRunOptions(Timeout: Timeout, Lines: lines.Read),
            cancellationToken).ConfigureAwait(false);

        return exitCode switch
        {
            0 or RestartNeeded => new ServicingAnswer(ServicingOutcome.Added, lines.ImageVersion),
            NotApplicable => new ServicingAnswer(ServicingOutcome.NotApplicable, lines.ImageVersion),
            _ => throw Failed("could not add the packages", exitCode, image.Log),
        };
    }

    public async Task<Version?> CleanupAsync(OfflineImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        DismLines lines = new(null);
        int exitCode = await tools.RunForExitCodeAsync(
            dismPath,
            [$"/Image:{image.Windows}", "/Cleanup-Image", "/StartComponentCleanup", .. Common(image)],
            new ToolRunOptions(Timeout: Timeout, Lines: lines.Read),
            cancellationToken).ConfigureAwait(false);

        return exitCode is 0 or RestartNeeded ? lines.ImageVersion : throw Failed("could not clean up the image", exitCode, image.Log);
    }

    // The package list is long and says nothing new, so it stays out of the log.
    public async Task<Version?> VersionAsync(OfflineImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        PrivateOutput output = new();

        try
        {
            int exitCode = await tools.RunForExitCodeAsync(
                dismPath,
                [$"/Image:{image.Windows}", "/Get-Packages", .. Common(image)],
                new ToolRunOptions(Timeout: VersionTimeout, Private: output),
                cancellationToken).ConfigureAwait(false);

            return exitCode == 0 ? DismLines.VersionIn(output.Lines) : null;
        }
        catch (DeploymentStepException)
        {
            return null;
        }
    }

    private static string[] Common(OfflineImage image) => [$"/ScratchDir:{image.Scratch}", $"/LogPath:{image.Log}", "/LogLevel:2", "/English"];

    // The log's last lines go into the step's log, because the log itself is deleted with the step.
    private DeploymentStepException Failed(string what, int exitCode, string logPath)
    {
        foreach (string line in Tail(logPath))
        {
            log.Warning($"dism.log: {line}");
        }

        return new DeploymentStepException(
            $"DISM {what} (exit code {ToolRunner.ExitCodeText(exitCode)}). The last lines of its log are in the machine log.");
    }

    private static IEnumerable<string> Tail(string path)
    {
        Queue<string> lines = new(LogTailLines);

        try
        {
            using StreamReader reader = new(path, detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } line)
            {
                if (lines.Count == LogTailLines)
                {
                    lines.Dequeue();
                }

                lines.Enqueue(line);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"The log {path} could not be read ({exception.Message})."];
        }

        return lines;
    }
}
