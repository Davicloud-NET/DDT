// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { followingAfterScroll, rowsInView } from "./logRows";

describe("rowsInView", () => {
  it("renders the newest 100 rows before the viewport is measured", () => {
    expect(rowsInView({ total: 500, height: 0, scrollTop: 0, following: false })).toEqual({
      start: 400,
      end: 500,
    });
  });

  it("renders the rows at the bottom and the overscan while following", () => {
    expect(rowsInView({ total: 500, height: 400, scrollTop: 0, following: true })).toEqual({
      start: 450,
      end: 500,
    });
  });

  it("renders the rows in view and the overscan on both sides while paused", () => {
    expect(rowsInView({ total: 500, height: 400, scrollTop: 2000, following: false })).toEqual({
      start: 70,
      end: 150,
    });
  });
});

describe("followingAfterScroll", () => {
  it("pauses once the view leaves the bottom by more than two rows", () => {
    expect(followingAfterScroll(true, 40)).toBe(true);
    expect(followingAfterScroll(true, 41)).toBe(false);
  });

  it("follows again once the view reaches the bottom", () => {
    expect(followingAfterScroll(false, 0.5)).toBe(true);
    expect(followingAfterScroll(false, 1)).toBe(false);
  });
});
