// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

/// <reference types="node" />
// @vitest-environment node

import { readFileSync } from "node:fs";

import { describe, expect, it } from "vitest";

import { FLASH_MS } from "@/ui/motion";

import { tokensPath } from "../../../DDT.Design/generate.mjs";

describe("the motion the code times itself", () => {
  it("lasts as long as the style sheet's flash, from the tokens", () => {
    const tokens = JSON.parse(readFileSync(tokensPath, "utf8")) as { motion: { flash: string } };

    expect(`${String(FLASH_MS)}ms`).toBe(tokens.motion.flash);
  });
});
