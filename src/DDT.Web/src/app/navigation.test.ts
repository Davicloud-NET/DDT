// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { categories, locate } from "./navigation";

describe("the navigation", () => {
  it("has five categories in a fixed order", () => {
    expect(categories.map((category) => category.id)).toEqual([
      "machines",
      "deployment",
      "library",
      "boot",
      "admin",
    ]);
  });

  it("keeps a machine's own page under All machines", () => {
    expect(locate("/machines/5f0c")?.page.to).toBe("/machines");
    expect(locate("/machines")?.page.to).toBe("/machines");
  });

  it("prefers the longest matching page", () => {
    expect(locate("/machines/runs")?.page.to).toBe("/machines/runs");
    expect(locate("/deployment/sequences/42")?.page.to).toBe("/deployment/sequences");
  });

  it("places pages outside the categories nowhere", () => {
    expect(locate("/account")).toBeNull();
    expect(locate("/machinesx")).toBeNull();
  });
});
