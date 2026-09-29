// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Pxe;

// Answering the wrong client on someone else's segment is worse than not answering, so every reason to stay quiet has
// a name. Without a switch port mirror, it's the only way to tell why firmware is ignored.
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
