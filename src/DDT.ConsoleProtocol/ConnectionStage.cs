// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// How far a request to the server got before it failed. Each stage fails for its own reasons: DNS; a wrong address, a
// firewall or a missing route; a server that stalls or a certificate the boot image does not trust; the server itself.
public enum ConnectionStage
{
    NameLookup,
    Connection,
    SecureConnection,

    // Connected, but no answer in time, a refusal, or an answer the agent cannot read.
    Answer,
}
