// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// An update as the search found it. Kbs are the digits alone, as KBArticleIDs has them. UpdateKinds reads the rest.
public sealed record AvailableUpdate(string Id, int Revision, string Title, IReadOnlyList<string> Kbs)
{
    public UpdateType Type { get; init; } = UpdateType.Software;

    public IReadOnlyList<Guid> CategoryIds { get; init; } = [];

    public bool BrowseOnly { get; init; }

    public bool IsBeta { get; init; }

    public bool CanRequestUserInput { get; init; }

    public bool EulaAccepted { get; init; } = true;

    public long Size { get; init; }
}
