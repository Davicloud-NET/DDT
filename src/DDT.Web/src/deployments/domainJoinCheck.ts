// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { apiPost } from "@/lib/api";
import { serverText, type ServerArguments } from "@/lib/serverText";

export type DomainJoinFindingLevel = "Passed" | "Warning" | "Problem";

// text is the server's English. domainFindingText translates it into the person's language.
export interface DomainJoinFinding {
  level: DomainJoinFindingLevel;
  text: string;
  code?: string | null;
  args?: ServerArguments | null;
}

export function domainFindingText(finding: DomainJoinFinding): string {
  return serverText(finding.code, finding.args, finding.text);
}

// What the domain said about the join account, in the order it was asked. Container is the organizational unit or
// the default Computers container. It's null if the check stopped before it got there.
export interface DomainJoinCheckView {
  canJoin: boolean;
  domain: string | null;
  userName: string | null;
  controller: string | null;
  container: string | null;
  findings: DomainJoinFinding[];
  checkedUtc: string;
}

// Signs in to the domain as the join account, so only administrators may call it. A null unit uses the configured
// default.
export function checkDomainJoin(organizationalUnit: string | null): Promise<DomainJoinCheckView> {
  return apiPost<DomainJoinCheckView>("/api/deployments/domain-check", { organizationalUnit });
}
