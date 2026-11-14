// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { MessageDescriptor } from "@lingui/core";
import { msg } from "@lingui/core/macro";

// Features often enabled, by the names DISM lists, for Windows client and then Windows Server. Any other name works too.
export const KNOWN_FEATURES: readonly { name: string; label: MessageDescriptor }[] = [
  { name: "NetFx3", label: msg`.NET Framework 3.5` },
  { name: "Microsoft-Hyper-V-All", label: msg`Hyper-V` },
  { name: "Microsoft-Windows-Subsystem-Linux", label: msg`Windows Subsystem for Linux` },
  { name: "VirtualMachinePlatform", label: msg`Virtual Machine Platform` },
  { name: "Containers-DisposableClientVM", label: msg`Windows Sandbox` },
  { name: "TelnetClient", label: msg`Telnet client` },
  { name: "TFTP", label: msg`TFTP client` },
  { name: "IIS-WebServerRole", label: msg`Internet Information Services` },
  { name: "Microsoft-Hyper-V", label: msg`Hyper-V on Windows Server` },
  { name: "DNS-Server-Full-Role", label: msg`DNS Server` },
  { name: "DHCPServer", label: msg`DHCP Server` },
  { name: "DirectoryServices-DomainController", label: msg`Active Directory Domain Services` },
  { name: "FailoverCluster-FullServer", label: msg`Failover Clustering` },
];
