// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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

    // What cloud-init and its scripts printed. Never read: cloud-init can print the passwords it made there.
    public const string OutputPath = "var/log/cloud-init-output.log";

    public const string StartIdPath = "proc/sys/kernel/random/boot_id";

    public bool Enabled => File.Exists(At(EnabledPath));

    public string? Result() => Read(ResultPath);

    public string? Status() => Read(StatusPath);

    public string? Configuration() => Read(ConfigurationPath);

    public string? ScheduledShutdown() => Read(ScheduledShutdownPath);

    public string? StartId() => Read(StartIdPath)?.Trim() is { Length: > 0 } id ? id : null;

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
}
