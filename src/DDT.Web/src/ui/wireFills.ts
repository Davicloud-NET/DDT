// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { WireTone } from "./FlowWires";

// The fill of an arrowhead or a dot, by the tone of its wire.
export const wireFills: Record<WireTone, string> = {
  edit: "fill-control",
  ahead: "fill-control",
  taken: "fill-ink",
  not: "fill-line",
};
