// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsUpdates;

// One IUpdate's fields as Windows Update answers them. Type is IUpdate.Type, MaxDownloadSize a VT_DECIMAL.
public sealed record UpdateFields(string Id, int Revision, string Title, IReadOnlyList<string> Kbs)
{
    public int Type { get; init; }

    public IReadOnlyList<string> CategoryIds { get; init; } = [];

    public bool BrowseOnly { get; init; }

    public bool IsBeta { get; init; }

    public bool CanRequestUserInput { get; init; }

    public bool EulaAccepted { get; init; }

    public decimal MaxDownloadSize { get; init; }

    public AvailableUpdate ToAvailableUpdate() =>
        new(Id, Revision, Title, Kbs)
        {
            Type = Type == (int)UpdateType.Driver ? UpdateType.Driver : UpdateType.Software,
            CategoryIds = [.. CategoryIds.Select(id => Guid.TryParse(id, out Guid parsed) ? parsed : Guid.Empty).Where(id => id != Guid.Empty)],
            BrowseOnly = BrowseOnly,
            IsBeta = IsBeta,
            CanRequestUserInput = CanRequestUserInput,
            EulaAccepted = EulaAccepted,
            Size = MaxDownloadSize <= 0 ? 0 : MaxDownloadSize >= long.MaxValue ? long.MaxValue : (long)MaxDownloadSize,
        };
}
