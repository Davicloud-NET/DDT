// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Sequences;

namespace DDT.Agent.Tests;

// A disk as a power loss leaves it, the way ext4 and xfs do. A file's bytes are on it once they are written through.
// A folder's names are on it once the folder is written out, or any number of them earlier, in the order they were made.
internal sealed class JournaledDisk : IReplacementDisk
{
    private readonly Dictionary<int, byte[]> _bytes = [];
    private readonly Dictionary<string, int> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _written = new(StringComparer.Ordinal);
    private readonly List<Action<Dictionary<string, int>>> _notWritten = [];
    private int _files;

    // Each call by what it did, with file names only.
    public List<string> Steps { get; } = [];

    // Called after each step, with the step.
    public Action<string>? AfterStep { get; set; }

    // False for a file system that cannot say whether a folder is on the disk.
    public bool Flushes { get; init; } = true;

    // A file that is on the disk, name and bytes.
    public void Put(string path, byte[] content)
    {
        _bytes[++_files] = content;
        _names[path] = _files;
        _written[path] = _files;
    }

    // Every way a power loss right now could leave the folder: each file's bytes by its name.
    public List<Dictionary<string, byte[]>> AfterPowerLoss()
    {
        List<Dictionary<string, byte[]>> ways = [];

        for (int made = 0; made <= _notWritten.Count; made++)
        {
            Dictionary<string, int> names = new(_written, StringComparer.Ordinal);

            foreach (Action<Dictionary<string, int>> change in _notWritten.Take(made))
            {
                change(names);
            }

            ways.Add(names.ToDictionary(name => Path.GetFileName(name.Key), name => _bytes[name.Value], StringComparer.Ordinal));
        }

        return ways;
    }

    public Task WriteNewAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        if (_names.ContainsKey(path))
        {
            throw new IOException($"{path} exists.");
        }

        int file = ++_files;
        _bytes[file] = content;
        Name(names => names[path] = file);
        Did($"write {Path.GetFileName(path)}");

        return Task.CompletedTask;
    }

    public bool Link(string path, string second)
    {
        bool linked = _names.TryGetValue(path, out int file);

        if (linked)
        {
            Name(names => names[second] = file);
        }

        Did($"link {Path.GetFileName(path)} {Path.GetFileName(second)}");

        return linked;
    }

    public void Rename(string from, string to)
    {
        int file = _names[from];
        Name(names =>
        {
            names.Remove(from);
            names[to] = file;
        });
        Did($"rename {Path.GetFileName(from)} {Path.GetFileName(to)}");
    }

    public bool Flush(string directory)
    {
        if (Flushes)
        {
            foreach (Action<Dictionary<string, int>> change in _notWritten)
            {
                change(_written);
            }

            _notWritten.Clear();
        }

        Did($"flush {Path.GetFileName(directory)}");

        return Flushes;
    }

    // As the agent's own wipe: only a file with one name loses its bytes, and they are written through at once.
    public void Remove(string path, bool overwrite)
    {
        if (_names.TryGetValue(path, out int file))
        {
            if (overwrite && _names.Values.Count(named => named == file) == 1)
            {
                _bytes[file] = new byte[_bytes[file].Length];
            }

            Name(names => names.Remove(path));
        }

        Did($"{(overwrite ? "wipe" : "delete")} {Path.GetFileName(path)}");
    }

    private void Name(Action<Dictionary<string, int>> change)
    {
        change(_names);
        _notWritten.Add(change);
    }

    private void Did(string step)
    {
        Steps.Add(step);
        AfterStep?.Invoke(step);
    }
}
