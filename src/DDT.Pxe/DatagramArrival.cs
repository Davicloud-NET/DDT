// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Pxe;

// Where a datagram came in, as IP_PKTINFO reports it: the interface index and our address it was sent to. Source is
// the sender.
public sealed record DatagramArrival(int Interface, IPAddress Address, IPEndPoint Source);
