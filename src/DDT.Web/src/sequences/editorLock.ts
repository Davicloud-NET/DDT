// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { createContext } from "react";

// Whether the sequence on the page can be changed: not for someone who may only look, and not once it was deleted.
// The fields read it, so each kind of step need not pass it on.
export const EditorLock = createContext(false);
