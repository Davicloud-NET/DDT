// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { ReactNode } from "react";

import { fieldFindings, type Findings } from "../problems";

// A problem marks the field invalid and shows under it. A warning shows under it in the attention colour.
export function findingProps(findings: Findings, field: string, hint: ReactNode) {
  const { problems, warnings } = fieldFindings(findings, field);
  const warning = warnings.join(" ");

  return {
    isInvalid: problems.length > 0,
    errorMessage: problems.join(" "),
    hint:
      warning === "" ? (
        hint
      ) : (
        <>
          {hint ? <>{hint} </> : null}
          <span className="text-attention-text">{warning}</span>
        </>
      ),
  };
}
