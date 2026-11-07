// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Machines;

namespace DDT.Server.Settings;

// What differs between the uploads of the two agents: the platform, the audit rows and what they say.
internal sealed record AgentUpload(string Platform, string UploadedAction, string RemovedAction)
{
    public static AgentUpload Windows { get; } = new(AgentPlatforms.WindowsX64, AuditActions.AgentUploaded, AuditActions.AgentUploadRemoved);

    public static AgentUpload Linux { get; } = new(AgentPlatforms.LinuxX64, AuditActions.LinuxAgentUploaded, AuditActions.LinuxAgentUploadRemoved);

    public bool IsLinux => Platform == AgentPlatforms.LinuxX64;

    public string UploadedDetail(AgentRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);

        return IsLinux
            ? $"Uploaded the agent for Linux with SHA-256 {release.Sha256}, {release.Size} bytes. Machines that start the Linux boot environment run it from their next boot."
            : $"Uploaded the agent with SHA-256 {release.Sha256}, {release.Size} bytes. Netbooting machines run it from their next boot.";
    }

    public string RemovedDetail(string sha256) => IsLinux
        ? $"Removed the uploaded agent for Linux with SHA-256 {sha256}. From their next boot, machines that start the Linux boot environment run the agent the server came with, or the environment's own."
        : $"Removed the uploaded agent with SHA-256 {sha256}. From their next boot, netbooting machines run the agent the server came with, or their boot image's.";
}
