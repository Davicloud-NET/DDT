// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Server.Images;

namespace DDT.Server.Sequences;

// What the steps of a sequence refer to outside of it, read once for every sequence a request checks.
public sealed record SequenceReferences(
    IReadOnlyDictionary<Guid, Image> Images,
    bool DomainConfigured,
    bool LocalAdministratorConfigured);
