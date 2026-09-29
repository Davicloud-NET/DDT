// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, group, leaf } from "@/test/trees";

import { clipboardText, nodesFromClipboard } from "./flowClipboard";

describe("the clipboard", () => {
  it("holds nodes as DDT's and gives them back", () => {
    const text = clipboardText([branch("b", [leaf("c"), leaf("d")], [leaf("e")])]);

    expect(JSON.parse(text)).toMatchObject({ ddtFlow: 1, nodes: [{ id: "b", kind: "if" }] });
    expect(nodesFromClipboard(text)?.map((node) => node.id)).toEqual(["b"]);
  });

  it("refuses other text", () => {
    expect(nodesFromClipboard("Apply image")).toBeNull();
    expect(nodesFromClipboard(JSON.stringify({ ddtFlow: 2, nodes: [leaf("a")] }))).toBeNull();
    expect(nodesFromClipboard(JSON.stringify({ ddtFlow: 1, nodes: [] }))).toBeNull();
    expect(
      nodesFromClipboard(JSON.stringify({ ddtFlow: 1, nodes: [{ ...leaf("a"), kind: "format" }] })),
    ).toBeNull();
    expect(
      nodesFromClipboard(
        JSON.stringify({ ddtFlow: 1, nodes: [{ ...group("g"), steps: [{ id: "x" }] }] }),
      ),
    ).toBeNull();
  });
});
