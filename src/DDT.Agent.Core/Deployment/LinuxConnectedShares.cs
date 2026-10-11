// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// A step's shares while they are mounted. Folders are only ever removed empty, so nothing on a share that is still
// mounted can be deleted through its place.
internal sealed class LinuxConnectedShares(IMounts mounts, AgentLog log) : IConnectedShares
{
    private readonly List<(string Unc, string Target)> _mounted = [];
    private bool _disposed;

    public IReadOnlyList<string> LocalPaths => [.. _mounted.Select(mount => mount.Target)];

    public void Add(string unc, string target) => _mounted.Add((unc, target));

    // Root's alone, like the share mounted on it.
    public static void Create(string target)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            Directory.CreateDirectory(target, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // The place of a share and the step's folder above it, each only when it is empty.
    public static void RemoveEmpty(string target)
    {
        try
        {
            Directory.Delete(target, recursive: false);

            if (Path.GetDirectoryName(target) is { } step && !Directory.EnumerateFileSystemEntries(step).Any())
            {
                Directory.Delete(step, recursive: false);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // What is still mounted or holds a file stays, and /run is empty again after the next start
        }
    }

    // EINVAL and ENOENT: nothing is mounted there, or the place is gone. The step's script has disconnected it itself.
    private static bool WasDisconnected(int error) => error is 22 or 2;

    // Disconnects in reverse order. A share that stays mounted only warns, because the step's result is decided.
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        for (int index = _mounted.Count - 1; index >= 0; index--)
        {
            (string unc, string target) = _mounted[index];
            int error = mounts.Unmount(target, detach: true);

            if (error == 0)
            {
                log.Information($"Disconnected {unc}.");
                RemoveEmpty(target);
            }
            else if (WasDisconnected(error))
            {
                log.Information($"{unc} was no longer connected when the step ended.");
                RemoveEmpty(target);
            }
            else
            {
                log.Warning($"{unc} could not be disconnected from {target} (error {error}). It stays mounted until Linux restarts.");
            }
        }

        _mounted.Clear();

        return ValueTask.CompletedTask;
    }
}
