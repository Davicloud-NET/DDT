// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import type { agentBinaryQuery, AgentBinarySource, AgentBinaryView } from "../agentBinary";

// What sets the agent's panel apart from the console's: the file, where it goes, and what the page says about it.
export interface Binary {
  query: typeof agentBinaryQuery;
  upload: (file: File) => Promise<AgentBinaryView>;
  maxBytes: number;
  accept: string[];
  configurationKey: string;
  unreadable: ReactNode;
  title: ReactNode;
  runsLabel: ReactNode;
  runs: Record<AgentBinarySource, ReactNode>;
  hashLabel: ReactNode;
  sizeLabel: ReactNode;
  configured: ReactNode;
  uploadTitle: ReactNode;
  warning: ReactNode;
  explanation: ReactNode;
  dropLabel: string;
  dropHere: ReactNode;
  dropHint: ReactNode;
  chooseFirst: () => string;
  tooLarge: (name: string, limit: string) => string;
  uploaded: (uploaded: string) => ReactNode;
  confirmTitle: (name: string) => ReactNode;
  confirmLabel: ReactNode;
  confirmBody: (name: string, size: string) => ReactNode;
  reauthReason: ReactNode;
}
