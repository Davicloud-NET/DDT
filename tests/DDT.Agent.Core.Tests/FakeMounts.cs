// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;

namespace DDT.Agent.Tests;

// Stands in for the kernel's mount calls. Every call goes into Calls, without the options, which Options keeps apart.
// Errors answers a mount by its source, AddressErrors by the address in its options, UnmountErrors an unmount by its
// place. Whatever is not mounted answers an unmount with EINVAL, as the kernel does.
internal sealed class FakeMounts : IMounts
{
    public const int NotMounted = 22;

    private readonly Lock _lock = new();
    private readonly List<string> _calls = [];
    private readonly List<string> _options = [];
    private readonly HashSet<string> _mounted = new(StringComparer.Ordinal);

    public Dictionary<string, int> Errors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> AddressErrors { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, int> UnmountErrors { get; } = new(StringComparer.Ordinal);

    // Runs once a mount worked, with its source and its place, to put there what the file system holds.
    public Action<string, string>? Mounted { get; set; }

    public List<string> Calls
    {
        get
        {
            lock (_lock)
            {
                return [.. _calls];
            }
        }
    }

    public List<string> Options
    {
        get
        {
            lock (_lock)
            {
                return [.. _options];
            }
        }
    }

    public int Mount(string source, string target, string fileSystem, string options)
    {
        lock (_lock)
        {
            _calls.Add($"mount {fileSystem} {source} at {target}");
            _options.Add(options);

            if (Errors.TryGetValue(source, out int error)
                || (options.StartsWith("ip=", StringComparison.Ordinal) && AddressErrors.TryGetValue(options.Split(',')[0][3..], out error)))
            {
                return error;
            }

            _mounted.Add(target);
        }

        Mounted?.Invoke(source, target);

        return 0;
    }

    public int Unmount(string target, bool detach)
    {
        lock (_lock)
        {
            if (UnmountErrors.TryGetValue(target, out int error))
            {
                return error;
            }

            if (!_mounted.Remove(target))
            {
                return NotMounted;
            }

            _calls.Add($"unmount {target}{(detach ? " detached" : string.Empty)}");

            return 0;
        }
    }
}
