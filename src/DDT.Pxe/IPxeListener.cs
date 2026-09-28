// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// A listener PxeHost starts and stops. Start binds its socket and throws a SocketException when that fails.
internal interface IPxeListener
{
    void Start();

    Task StopAsync();
}
