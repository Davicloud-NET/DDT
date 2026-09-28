// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { createContext } from "react";

// Whether the sequence on the page is locked: for someone who can only view it, and once it's been deleted. The
// fields read it, so each kind of step doesn't have to pass it on.
export const EditorLock = createContext(false);
