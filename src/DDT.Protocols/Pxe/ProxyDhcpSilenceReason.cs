// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Pxe;

// Answering the wrong client on someone else's production segment is worse than not answering, so
// every reason to stay quiet is named. This is the only thing that explains why firmware is being
// ignored when nobody has a switch port mirror.
public enum ProxyDhcpSilenceReason
{
    None = 0,
    NotABootRequest,
    MessageTypeNotHandledOnPort,
    NoVendorClass,
    UnrecognisedVendorClass,
    NoClientArchitecture,
    MalformedClientArchitecture,
    NoBootTargetForArchitecture,
    BootMethodDoesNotMatchVendorClass,
    ServerIdentifierNamesAnotherServer,
    RelayNotAuthorised,
}
