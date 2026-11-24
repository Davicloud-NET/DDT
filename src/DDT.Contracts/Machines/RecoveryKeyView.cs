// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// A recovery key as the machine's page lists it, without its password. ProtectorId is the key ID BitLocker's recovery
// screen shows. ReplacedUtc is set once a later key arrived for the same volume.
public sealed record RecoveryKeyView(Guid Id, Guid ProtectorId, string Volume, Guid RunId, DateTimeOffset CreatedUtc, DateTimeOffset? ReplacedUtc);
