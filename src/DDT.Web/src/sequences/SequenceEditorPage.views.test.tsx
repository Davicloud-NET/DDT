// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { toasts } from "@/ui/toasts";

import {
  administrator,
  node,
  opened,
  serve,
  treeView,
  viewer,
} from "./SequenceEditorPage.fixtures";

describe("SequenceEditorPage", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("shows a viewer the flow without letting anything change", async () => {
    const { saves } = serve(viewer, treeView(), { step: "s1" });

    expect(
      await screen.findByText(
        "Only administrators change task sequences. You can look at this one.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("complementary", { name: "Add to the flow" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Add a step/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Undo" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Remove/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Wrap/ })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add a condition" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Add a share" })).not.toBeInTheDocument();
    expect(screen.queryByText("All changes saved")).not.toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveAttribute("readonly");
    expect(screen.getByRole("textbox", { name: "Script" })).toHaveAttribute("readonly");
    // A choice reads as text.
    expect(screen.getByRole("textbox", { name: "Interpreter" })).toHaveValue("PowerShell");

    const share = node(/^Step 1 of 'Group: Berlin office'/);
    act(() => {
      share.focus();
    });
    fireEvent.keyDown(share, { key: "Delete" });
    fireEvent.keyDown(share, { key: "ArrowDown", altKey: true });
    fireEvent.keyDown(share, { key: "x", ctrlKey: true });
    expect(node(/^Step 1 of 'Group: Berlin office'/)).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 1_000));
    expect(saves).toHaveLength(0);
  });

  it("shows the outline on a phone, and a node's fields in a drawer", async () => {
    serve(administrator, treeView(), { narrow: true });

    await opened();
    expect(screen.queryByRole("group", { name: /^Flow of / })).not.toBeInTheDocument();
    const outline = screen.getByRole("treegrid", { name: "Outline of Lab PCs" });
    const row = within(outline).getByRole("row", { name: /^2 Step 2, If: Is it a Latitude\?/ });
    expect(within(outline).getByRole("row", { name: /^2\.1 Then/ })).toBeInTheDocument();
    expect(
      within(outline).getByRole("row", { name: /^2\.1\.1 Step 1 of Then of/ }),
    ).toBeInTheDocument();

    press(row);

    const drawer = await screen.findByRole("dialog", { name: "If: Is it a Latitude?" });
    expect(within(drawer).getByRole("textbox", { name: "Name" })).toHaveValue("Is it a Latitude?");
  });
});
