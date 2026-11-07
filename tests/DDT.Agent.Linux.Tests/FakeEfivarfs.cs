// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.RegularExpressions;
using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// A folder that behaves as efivarfs does where plain files do not. A file cannot seek and has no length to ask for.
// One write is the whole variable. A variable the UEFI specification does not name is immutable until that is
// cleared. The kernel refuses a write shorter than the attributes and a boot entry that is none.
internal sealed partial class FakeEfivarfs
{
    // EPERM, EINVAL of errno.h.
    private const int NotPermitted = 1;
    private const int InvalidArgument = 22;

    private readonly HashSet<string> _mutable = new(StringComparer.Ordinal);

    // What the firmware answers every write with, such as a full variable store.
    public IOException? WriteFailure { get; set; }

    // How many writes each variable took, by its file's name.
    public Dictionary<string, int> Writes { get; } = new(StringComparer.Ordinal);

    public void MakeMutable(string path) => _mutable.Add(path);

    public Stream Open(string path, FileAccess access)
    {
        if (access == FileAccess.Read)
        {
            return new UnseekableStream(File.OpenRead(path), this, path);
        }

        if (File.Exists(path) && IsImmutable(path))
        {
            throw new UnauthorizedAccessException("Operation not permitted", new IOException("Operation not permitted", NotPermitted));
        }

        return new UnseekableStream(null, this, path);
    }

    // The kernel leaves the variables of the UEFI specification itself mutable, which all have its vendor GUID.
    private bool IsImmutable(string path) =>
        !path.EndsWith(LinuxUefiVariables.Global.ToString("D"), StringComparison.Ordinal) && !_mutable.Contains(path);

    private void Take(string path, ReadOnlySpan<byte> content, bool again)
    {
        if (again)
        {
            throw new IOException("A second write would be a second value: efivarfs takes a variable in one write.");
        }

        string name = Path.GetFileName(path);

        ReadOnlySpan<byte> value = content.Length < LinuxUefiVariables.AttributesLength ? [] : content[LinuxUefiVariables.AttributesLength..];

        if (content.Length < LinuxUefiVariables.AttributesLength || (value.Length > 0 && BootEntry().IsMatch(name) && !IsLoadOption(value)))
        {
            throw new IOException("Invalid argument", InvalidArgument);
        }

        if (WriteFailure is { } failure)
        {
            throw failure;
        }

        // Attributes without a value take the variable away.
        if (value.Length == 0)
        {
            File.Delete(path);

            return;
        }

        // A variable that is new is immutable again, whatever one of its name was before.
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            _mutable.Remove(path);
        }

        File.WriteAllBytes(path, content.ToArray());
        Writes[name] = Writes.GetValueOrDefault(name) + 1;
    }

    // As the kernel checks it: attributes, the length of the paths, a description that ends, and paths of that length.
    private static bool IsLoadOption(ReadOnlySpan<byte> value)
    {
        if (value.Length < 8)
        {
            return false;
        }

        int paths = value[4] | (value[5] << 8);

        for (int index = 6; index + 1 < value.Length; index += 2)
        {
            if (value[index] == 0 && value[index + 1] == 0)
            {
                return value.Length - (index + 2) >= paths;
            }
        }

        return false;
    }

    [GeneratedRegex("^Boot[0-9A-F]{4}-")]
    private static partial Regex BootEntry();

    // What a program gets when it opens a variable: no place in it, no length, and for a write one call.
    private sealed class UnseekableStream(FileStream? reading, FakeEfivarfs files, string path) : Stream
    {
        private bool _written;

        public override bool CanRead => reading is not null;

        public override bool CanSeek => false;

        public override bool CanWrite => reading is null;

        public override long Length => throw new NotSupportedException("Stream does not support seeking.");

        public override long Position
        {
            get => throw new NotSupportedException("Stream does not support seeking.");
            set => throw new NotSupportedException("Stream does not support seeking.");
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            (reading ?? throw new NotSupportedException()).Read(buffer, offset, count);

        public override void Write(byte[] buffer, int offset, int count)
        {
            files.Take(path, buffer.AsSpan(offset, count), _written);
            _written = true;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("Stream does not support seeking.");

        public override void SetLength(long value) => throw new NotSupportedException("Stream does not support seeking.");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                reading?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
