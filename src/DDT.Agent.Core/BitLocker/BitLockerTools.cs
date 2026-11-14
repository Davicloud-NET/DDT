// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.WindowsFeatures;

namespace DDT.Agent.BitLocker;

// What Enable BitLocker works with. Features enables BitLocker on Windows Server, which lacks manage-bde without it.
public sealed record BitLockerTools(IBitLocker BitLocker, ITpm Tpm, IWindowsFeatures Features, IDomainMembership Domain, IBitLockerPolicy Policy);
