// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;

namespace DDT.Agent.Deployment;

// The UEFI variables through efivarfs: one file per variable, named <name>-<vendor GUID>, whose first four bytes are
// the variable's attributes. efivarfs takes a variable in one write and cannot seek.
public sealed class LinuxUefiVariables(string root, Action<string>? makeMutable = null) : IUefiVariables, IVendorVariables
{
    // EFI_GLOBAL_VARIABLE, under which BootOrder, Boot#### and SecureBoot live.
    public static readonly Guid Global = new("8be4df61-93ca-11d2-aa0d-00e098032b8c");

    // EFI_IMAGE_SECURITY_DATABASE_GUID, under which db and dbx live.
    public static readonly Guid ImageSecurityDatabase = new("d719b2cb-3d3a-4596-a3bc-dad00e67656f");

    public const int AttributesLength = 4;

    // NON_VOLATILE | BOOTSERVICE_ACCESS | RUNTIME_ACCESS, which the specification requires for boot variables. A
    // vendor's variable gets the same, so it outlasts the restart and the next boot environment reads it.
    public const uint BootVariableAttributes = 0x7;

    // ENOSPC, which .NET hands on as the HResult of a plain IOException.
    private const int NoSpace = 28;

    private const int MaxLength = 1024 * 1024;

    // The kernel makes a variable immutable unless the UEFI specification names it, as it does the boot variables.
    private readonly Action<string> _makeMutable = makeMutable ?? ImmutableFlag.Clear;

    public string Directory { get; } = Path.Combine(root, "sys", "firmware", "efi", "efivars");

    // False where efivarfs is not mounted, so a variable that is missing says nothing about the firmware.
    public bool HasVariables
    {
        get
        {
            try
            {
                return System.IO.Directory.EnumerateFileSystemEntries(Directory).Any();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public static string FileName(string name, Guid vendor) => $"{name}-{vendor:D}";

    public byte[]? Read(string name) => Read(name, Global);

    public void Write(string name, byte[] value) => Write(name, Global, value);

    public void Delete(string name) => Delete(name, Global);

    // The certificates and hashes the firmware trusts with Secure Boot on. Null when there are none.
    public byte[]? ReadSignatureDatabase() => Read("db", ImageSecurityDatabase);

    public byte[]? Read(string name, Guid vendor)
    {
        string path = PathOf(name, vendor);

        try
        {
            if (!File.Exists(path))
            {
                RequireDirectory("read");

                return null;
            }

            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
            byte[] content = ReadAll(file, name);

            return content.Length <= AttributesLength ? [] : content[AttributesLength..];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"The firmware variable {name} cannot be read ({exception.Message}).", exception);
        }
    }

    public void Write(string name, Guid vendor, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string path = PathOf(name, vendor);
        byte[] content = new byte[AttributesLength + value.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(content, BootVariableAttributes);
        value.CopyTo(content, AttributesLength);
        RequireDirectory("written");

        try
        {
            if (File.Exists(path))
            {
                _makeMutable(path);
            }

            // Not truncated first: the variable changes with the one write, or not at all.
            using FileStream file = new(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, bufferSize: 0);
            file.Write(content);

            // efivarfs gives the file the write's length and cannot seek. A plain file in its place can, and keeps the
            // end of a longer value.
            if (file.CanSeek && file.Length > content.Length)
            {
                file.SetLength(content.Length);
            }
        }
        catch (IOException exception) when (exception.HResult == NoSpace)
        {
            throw new DeploymentStepException(
                $"The firmware variable {name} cannot be written: the firmware's variable store is full. Delete boot entries nobody " +
                "needs in the firmware setup, or reset its settings there.",
                exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"The firmware variable {name} cannot be written ({exception.Message}).", exception);
        }
    }

    public void Delete(string name, Guid vendor)
    {
        string path = PathOf(name, vendor);
        RequireDirectory("deleted");

        try
        {
            if (File.Exists(path))
            {
                _makeMutable(path);
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DeploymentStepException($"The firmware variable {name} cannot be deleted ({exception.Message}).", exception);
        }
    }

    private static byte[] ReadAll(FileStream file, string name)
    {
        using MemoryStream content = new();
        byte[] buffer = new byte[4096];
        int read;

        while ((read = file.Read(buffer)) > 0)
        {
            content.Write(buffer, 0, read);

            if (content.Length > MaxLength + AttributesLength)
            {
                throw new DeploymentStepException($"The firmware variable {name} is larger than {MaxLength} bytes.");
            }
        }

        return content.ToArray();
    }

    // The name becomes a file name, so it may not lead anywhere else.
    private string PathOf(string name, Guid vendor)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.AsSpan().ContainsAny('/', '\\') || name is "." or "..")
        {
            throw new ArgumentException($"{name} is no name of a firmware variable.", nameof(name));
        }

        return Path.Combine(Directory, FileName(name, vendor));
    }

    private void RequireDirectory(string done)
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            throw new DeploymentStepException(
                $"This machine did not start in UEFI mode, or efivarfs is not mounted, so its firmware variables cannot be {done}.");
        }
    }
}
