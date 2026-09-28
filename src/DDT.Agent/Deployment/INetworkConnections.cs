// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

// The connections of the logon session the calling thread acts for, as mpr.dll keeps them. Every call blocks, and a
// failure comes back as the Windows error with its text, read on the same thread, as the network provider's own text
// is kept per thread.
public interface INetworkConnections
{
    // A temporary connection without a drive letter. Null when it worked.
    NetworkError? Add(string remoteName, string userName, string password);

    // Closes the connection even with files open. Null when it worked.
    NetworkError? Cancel(string remoteName);

    // The remote names of every connection, such as \\files\drivers.
    IReadOnlyList<string> Connected();
}

public sealed record NetworkError(int Code, string Message);
