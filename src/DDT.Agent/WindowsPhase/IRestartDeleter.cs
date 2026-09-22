// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

public interface IRestartDeleter
{
    // Has Windows delete path, a file or an empty directory, when it next starts: a file in use, such as the running
    // agent, cannot go before. Windows deletes in the order of the calls. A failure only leaves the path behind.
    void DeleteAtRestart(string path);
}
