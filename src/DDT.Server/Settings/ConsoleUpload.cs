// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Server.Machines;

namespace DDT.Server.Settings;

// What differs between the uploads of the two consoles: the platform, the audit rows and what they say.
internal sealed record ConsoleUpload(string Platform, string UploadedAction, string RemovedAction)
{
    public static ConsoleUpload Windows { get; } = new(AgentPlatforms.WindowsX64, AuditActions.ConsoleUploaded, AuditActions.ConsoleUploadRemoved);

    public static ConsoleUpload Linux { get; } = new(AgentPlatforms.LinuxX64, AuditActions.LinuxConsoleUploaded, AuditActions.LinuxConsoleUploadRemoved);

    public bool IsLinux => Platform == AgentPlatforms.LinuxX64;

    public string UploadedDetail(string sha256, long size) => IsLinux
        ? $"Uploaded the console for Linux with ddt-console of SHA-256 {sha256}, {size} bytes in all. Machines show it from the next start of the Linux boot environment or of their agent in the installed Linux."
        : $"Uploaded the console with ddt-console.exe of SHA-256 {sha256}, {size} bytes in all. Netbooting machines show it from their next boot.";

    public string RemovedDetail(string sha256) => IsLinux
        ? $"Removed the uploaded console for Linux with ddt-console of SHA-256 {sha256}. Machines then show the console the server came with, or the Linux boot environment's own."
        : $"Removed the uploaded console with ddt-console.exe of SHA-256 {sha256}. From their next boot, netbooting machines show the console the server came with, or their boot image's.";
}
