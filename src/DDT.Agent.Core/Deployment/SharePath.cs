// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A share's path as a sequence names it, \\host\share\folder, for a system that mounts it. Folder is empty or starts
// with a slash.
public sealed record SharePath(string Host, string Share, string Folder)
{
    // As the logs and messages name it.
    public string Unc => $@"\\{Host}\{Share}{Folder.Replace('/', '\\')}";

    // The device of a CIFS mount.
    public string Source => $"//{Host}/{Share}{Folder}";

    public static SharePath Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        string trimmed = path.Trim().Replace('/', '\\');
        string[] parts = trimmed.StartsWith(@"\\", StringComparison.Ordinal) ? trimmed[2..].TrimEnd('\\').Split('\\') : [];

        if (parts.Length < 2 || parts[0] is "?" or "." || parts.Any(part => part.Length == 0 || part is "." or ".." || part.Contains('\0', StringComparison.Ordinal)))
        {
            throw new DeploymentStepException($"{path} is not the path of a share, such as \\\\files.example.com\\drivers.");
        }

        return new SharePath(parts[0], parts[1], string.Concat(parts.Skip(2).Select(part => "/" + part)));
    }
}
