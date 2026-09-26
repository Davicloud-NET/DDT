// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// ChassisType is the System Enclosure's chassis type as DMTF DSP0134 numbers it, without the lock bit, and null when the
// table has no System Enclosure structure.
public sealed record SmbiosSystemInformation(Guid Uuid, string? Manufacturer, string? ProductName, string? SerialNumber, byte? ChassisType);
