// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.LinuxPhase;

// cloud-init's files under root, which is / on a machine. Those under /run are of this start alone. boot-finished and
// the copies under /var/lib/cloud are left out: they stay from an earlier start when cloud-init is disabled.
public sealed class CloudInitFiles(string root) : ICloudInit
{
    public const string EnabledPath = "run/cloud-init/enabled";
    public const string ResultPath = "run/cloud-init/result.json";
    public const string StatusPath = "run/cloud-init/status.json";
    public const string ConfigurationPath = "run/cloud-init/combined-cloud-config.json";
    public const string ScheduledShutdownPath = "run/systemd/shutdown/scheduled";
    public const string OutputPath = "var/log/cloud-init-output.log";

    // The end of the output is read no further back than this.
    private const int MaxTailBytes = 64 * 1024;

    private const int MaxLineLength = 512;

    public bool Enabled => File.Exists(At(EnabledPath));

    public string? Result() => Read(ResultPath);

    public string? Status() => Read(StatusPath);

    public string? Configuration() => Read(ConfigurationPath);

    public string? ScheduledShutdown() => Read(ScheduledShutdownPath);

    public IReadOnlyList<string> OutputTail(int lines)
    {
        try
        {
            using FileStream file = new(At(OutputPath), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            bool cut = file.Length > MaxTailBytes;
            file.Seek(cut ? -MaxTailBytes : 0, cut ? SeekOrigin.End : SeekOrigin.Begin);
            using StreamReader reader = new(file, Encoding.UTF8);

            // Where the read began in the middle of a line, that line is no line of the output.
            IEnumerable<string> read = reader.ReadToEnd().Split('\n').Skip(cut ? 1 : 0);

            return [.. read.Select(Clean).Where(line => line.Length > 0).TakeLast(lines)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private string At(string path) => Path.Combine(root, path);

    // A file that is there and cannot be read is empty, so it still counts as there.
    private string? Read(string path)
    {
        string file = At(path);

        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    // A terminal's control characters mean nothing in a log line.
    private static string Clean(string line)
    {
        string text = new string([.. line.Select(character => char.IsControl(character) ? ' ' : character)]).TrimEnd();

        return text.Length > MaxLineLength ? text[..MaxLineLength] : text;
    }
}
