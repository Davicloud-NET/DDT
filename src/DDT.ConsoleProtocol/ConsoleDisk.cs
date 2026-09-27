// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A disk DDT can install on. Number is its number in Windows PE, which can change when the machine restarts. BusType is
// how it is attached, such as Nvme or Sata. PartitionCount says whether anything is on it.
public sealed record ConsoleDisk(int Number, string? Model, long SizeBytes, string BusType, int PartitionCount);
