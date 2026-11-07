// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Sequences;

// Finds a run's state after the boot environment restarted. It takes the newest DDT\run\state.json in roots, which
// are listed again at every Find. In WinPE those are the fixed drives, whatever letters they got this time.
public sealed class LocalRunLocator(IEnumerable<string> roots)
{
    // The root of the Windows volume holding the run, such as C:\, or null when none holds one.
    public string? Find()
    {
        string? newest = null;
        DateTime newestWrite = DateTime.MinValue;

        foreach (string root in roots)
        {
            try
            {
                FileInfo state = new(RunFiles.StatePathIn(root));

                if (state.Exists && state.LastWriteTimeUtc > newestWrite)
                {
                    newest = root;
                    newestWrite = state.LastWriteTimeUtc;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A drive that can't be read holds no run this agent can continue.
            }
        }

        return newest;
    }
}
