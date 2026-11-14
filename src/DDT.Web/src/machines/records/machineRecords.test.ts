// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { keyIdDigits } from "./machineRecords";

describe("keyIdDigits", () => {
  it("takes a key ID as the recovery screen shows it, whole or by its start", () => {
    expect(keyIdDigits("{8F0A1C2D-3E4F-4A5B-9C6D-7E8F9A0B1C2D}")).toBe(
      "8f0a1c2d3e4f4a5b9c6d7e8f9a0b1c2d",
    );
    expect(keyIdDigits(" 8F0A1C2D ")).toBe("8f0a1c2d");
    expect(keyIdDigits("8f0a1c2d-3e4f")).toBe("8f0a1c2d3e4f");
  });

  it("refuses fewer than 8 digits and anything that is no hex", () => {
    expect(keyIdDigits("8f0a1c2")).toBeNull();
    expect(keyIdDigits("PC-04211")).toBeNull();
    expect(keyIdDigits("")).toBeNull();
  });
});
