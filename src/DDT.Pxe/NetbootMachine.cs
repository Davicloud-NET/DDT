// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Who asks ProxyDHCP: Mac as twelve upper case hex digits, and the sixteen octets of the machine's UUID as option 97
// carried them, or none. Firmware sends the UUID in either byte order.
public sealed record NetbootMachine(string Mac, ReadOnlyMemory<byte> Uuid);
