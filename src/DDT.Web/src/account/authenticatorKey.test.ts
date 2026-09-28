// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { groupKey } from "./authenticatorKey";

describe("groupKey", () => {
  it("groups the key in fours, whatever spaces it came with", () => {
    expect(groupKey("ABCDEF GHIJ\tKLMN")).toBe("ABCD EFGH IJKL MN");
  });
});
