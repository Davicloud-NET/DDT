// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { firstValue } from "./distinguishedName";

describe("firstValue", () => {
  it("takes the value of a distinguished name's first part", () => {
    expect(firstValue("CN=Deployment admins,OU=Groups,DC=corp")).toBe("Deployment admins");
  });

  it("keeps an escaped comma in the value and drops its backslash", () => {
    expect(firstValue("CN=Berger\\, Jonas,OU=Staff,DC=corp")).toBe("Berger, Jonas");
  });
});
