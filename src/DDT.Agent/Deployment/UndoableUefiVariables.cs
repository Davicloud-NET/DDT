// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// Passes every call on to variables and remembers what each variable held before it was first changed, so Undo can
// put the firmware back as it was.
public sealed class UndoableUefiVariables(IUefiVariables variables) : IUefiVariables
{
    private readonly List<(string Name, byte[]? Value)> _before = [];

    public byte[]? Read(string name) => variables.Read(name);

    public void Write(string name, byte[] value)
    {
        Remember(name);
        variables.Write(name, value);
    }

    public void Delete(string name)
    {
        Remember(name);
        variables.Delete(name);
    }

    // Undoes the last change first. Returns false when nothing was changed.
    public bool Undo()
    {
        bool changed = _before.Count > 0;

        for (int index = _before.Count - 1; index >= 0; index--)
        {
            (string name, byte[]? value) = _before[index];

            if (value is null)
            {
                variables.Delete(name);
            }
            else
            {
                variables.Write(name, value);
            }

            _before.RemoveAt(index);
        }

        return changed;
    }

    private void Remember(string name)
    {
        if (!_before.Exists(change => change.Name == name))
        {
            _before.Add((name, variables.Read(name)));
        }
    }
}
