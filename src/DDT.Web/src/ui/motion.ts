// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState } from "react";

// How long a flash lasts, in milliseconds: motion.flash in src/DDT.Design/tokens.json, which the style sheet has as
// --duration-flash. A test keeps the two equal.
export const FLASH_MS = 1400;

// Which of two names of the same entrance to use, so that each change of `value` plays it again without mounting
// anything anew: none before the first change, so nothing enters on a first render, then the other one each time.
export function useReplay(value: unknown): 0 | 1 | null {
  const [shown, setShown] = useState<{ value: unknown; cycle: 0 | 1 | null }>({
    value,
    cycle: null,
  });

  if (!Object.is(shown.value, value)) {
    const cycle = shown.cycle === 0 ? 1 : 0;
    setShown({ value, cycle });

    return cycle;
  }

  return shown.cycle;
}
