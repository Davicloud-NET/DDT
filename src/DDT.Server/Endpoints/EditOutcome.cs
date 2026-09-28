// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Endpoints;

// The result of saving an item a page edits: Saved, or Current for a save on an older revision, the fields' Problems,
// or a Refusal of the whole save (409). With none of them set, the item doesn't exist.
internal sealed record EditOutcome<T>(T? Saved, T? Current, FieldProblems? Problems, ServerMessage? Refusal)
    where T : class
{
    public static EditOutcome<T> NotFound { get; } = new(null, null, null, null);

    public static EditOutcome<T> Done(T saved) => new(saved, null, null, null);

    public static EditOutcome<T> Newer(T current) => new(null, current, null, null);

    public static EditOutcome<T> Invalid(FieldProblems problems) => new(null, null, problems, null);

    public static EditOutcome<T> Refused(ServerMessage refusal) => new(null, null, null, refusal);
}
