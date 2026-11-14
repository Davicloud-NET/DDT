// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Text.RegularExpressions;
using DDT.Agent.Deployment;

namespace DDT.Agent.Servicing;

// Windows PE's DISM, which services the applied Windows with that Windows' own servicing stack. It logs at level 2:
// at the default level one cumulative update wrote a log of 542 MB.
public sealed partial class DismOfflineServicing(IToolRunner tools, string dismPath, AgentLog log) : IOfflineServicing
{
    // ERROR_SUCCESS_REBOOT_REQUIRED.
    public const int RestartNeeded = 3010;

    // CBS_E_NOT_APPLICABLE.
    public const int NotApplicable = unchecked((int)0x800F081E);

    public const int LogTailLines = 40;

    // A cumulative update took 8.7 minutes on a test VM.
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(2);

    public async Task<ServicingOutcome> AddAsync(OfflineImage image, IReadOnlyList<string> packages, IProgress<int> percent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(percent);

        int exitCode = await tools.RunForExitCodeAsync(
            dismPath,
            [$"/Image:{image.Windows}", "/Add-Package", .. packages.Select(package => $"/PackagePath:{package}"), .. Common(image)],
            new ToolRunOptions(Timeout: Timeout, Lines: line => Progress(line, percent)),
            cancellationToken).ConfigureAwait(false);

        return exitCode switch
        {
            0 or RestartNeeded => ServicingOutcome.Added,
            NotApplicable => ServicingOutcome.NotApplicable,
            _ => throw Failed("could not add the packages", exitCode, image.Log),
        };
    }

    public async Task CleanupAsync(OfflineImage image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);

        int exitCode = await tools.RunForExitCodeAsync(
            dismPath,
            [$"/Image:{image.Windows}", "/Cleanup-Image", "/StartComponentCleanup", .. Common(image)],
            new ToolRunOptions(Timeout: Timeout, Lines: line => Progress(line, null)),
            cancellationToken).ConfigureAwait(false);

        if (exitCode is not (0 or RestartNeeded))
        {
            throw Failed("could not clean up the image", exitCode, image.Log);
        }
    }

    private static string[] Common(OfflineImage image) => [$"/ScratchDir:{image.Scratch}", $"/LogPath:{image.Log}", "/LogLevel:2", "/English"];

    // DISM draws its progress bar on one line again and again. The bar only gives the percent, and the log gets the rest.
    private static string? Progress(string line, IProgress<int>? percent)
    {
        if (Bar().Match(line) is not { Success: true } bar)
        {
            return line;
        }

        percent?.Report((int)double.Parse(bar.Groups["percent"].Value, CultureInfo.InvariantCulture));

        return null;
    }

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

    [GeneratedRegex(@"^\[[=\s]*(?<percent>\d{1,3}\.\d)%[=\s]*\]$", RegexOptions.CultureInvariant)]
    private static partial Regex Bar();
}
