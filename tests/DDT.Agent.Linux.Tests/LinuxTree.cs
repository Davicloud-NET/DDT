// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// A folder that stands in for the root of a Linux machine, with what a test puts under /sys, /proc and /dev.
internal sealed class LinuxTree : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "ddt-linux-tree-" + Guid.NewGuid().ToString("N"));

    public string Variables => Path.Combine(Root, "sys", "firmware", "efi", "efivars");

    public LinuxTree() => Directory.CreateDirectory(Root);

    public string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    public string Write(string text, params string[] parts) => Write(System.Text.Encoding.ASCII.GetBytes(text), parts);

    public string Write(byte[] content, params string[] parts)
    {
        string path = PathOf(parts);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Root);
        File.WriteAllBytes(path, content);

        return path;
    }

    // A firmware variable as efivarfs shows it: the attributes, then the value.
    public string WriteVariable(string name, Guid vendor, params byte[] value) =>
        Write([0x07, 0x00, 0x00, 0x00, .. value], "sys", "firmware", "efi", "efivars", LinuxUefiVariables.FileName(name, vendor));

    public byte[] ReadVariableFile(string name, Guid vendor) => File.ReadAllBytes(Path.Combine(Variables, LinuxUefiVariables.FileName(name, vendor)));

    // An entry of /sys/block with what the kernel shows of a disk. The size is in units of 512 bytes.
    public string AddDisk(string name, long sizeBytes, bool removable = false, int sectorSize = 512, string? model = null)
    {
        Write($"{sizeBytes / 512}\n", "sys", "block", name, "size");
        Write(removable ? "1\n" : "0\n", "sys", "block", name, "removable");
        Write($"{sectorSize}\n", "sys", "block", name, "queue", "logical_block_size");

        if (model is not null)
        {
            Write(model + "\n", "sys", "block", name, "device", "model");
        }

        return PathOf("sys", "block", name);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
