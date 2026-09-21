// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { createContext } from "react";

import type { LiveConnection } from "./liveConnection";

// The shell's live connection. Pages rendered without the shell, as in their tests, have none.
export const LiveContext = createContext<LiveConnection | null>(null);
