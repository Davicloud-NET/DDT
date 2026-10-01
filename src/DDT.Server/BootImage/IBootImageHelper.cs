// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.BootImage;

// The DDT Helper service, which does as SYSTEM what the web server may not. Tests stand in for it.
public interface IBootImageHelper
{
    // Whether a helper answers on this server.
    bool Available { get; }

    // Sends the request and yields what the helper answers, line by line, until its exit code.
    IAsyncEnumerable<HelperMessage> RunAsync(HelperRequest request, CancellationToken cancellationToken);
}
