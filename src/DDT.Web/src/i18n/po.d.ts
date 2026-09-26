// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

// Lingui's Vite plugin compiles a PO catalog into a module when it is imported.
declare module "*.po" {
  import type { Messages } from "@lingui/core";

  export const messages: Messages;
}
