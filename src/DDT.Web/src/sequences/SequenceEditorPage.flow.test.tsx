// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { flowProblems } from "@/test/flowSequence";
import { toasts } from "@/ui/toasts";

import {
  administrator,
  ids,
  key,
  node,
  opened,
  saveWait,
  serve,
  treeView,
  view,
} from "./SequenceEditorPage.fixtures";
import { SEQUENCE_VERSION, type IfStep } from "./sequences";

describe("SequenceEditorPage", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("draws the flow with every node, marks the findings and says where each node is", async () => {
    serve(administrator, treeView({ problems: flowProblems }), { step: "if1" });

    await opened();
    expect(screen.getByText("1 problem")).toBeInTheDocument();
    expect(node("Step 2, If: Is it a Latitude?")).toHaveAttribute("aria-current", "true");
    expect(
      node("Step 1 of Then of 'If: Is it a Latitude?', Apply Windows 11 for Latitudes"),
    ).toHaveAccessibleName(
      "Step 1 of Then of 'If: Is it a Latitude?', Apply Windows 11 for Latitudes, Apply image",
    );
    expect(node(/^Step 1 of 'Group: Berlin office'/)).toHaveAccessibleName(
      "Step 1 of 'Group: Berlin office', Map the site share, Run script, 1 problem",
    );
    // The IF's inspector shows its test.
    expect(screen.getByRole("group", { name: "Go along Then when" })).toBeInTheDocument();
    expect(
      screen.getByText("Machines where Model contains Latitude go along Then."),
    ).toBeInTheDocument();
    // One stop of the Tab key among the nodes and the gaps: the chosen node.
    expect(
      [...document.querySelectorAll<HTMLElement>("[data-flow-node], [aria-haspopup=menu]")].filter(
        (element) => element.tabIndex === 0 && element.closest("[role=group]") !== null,
      ),
    ).toEqual([node("Step 2, If: Is it a Latitude?")]);
  });

  it("passes axe", async () => {
    serve(administrator, treeView({ problems: flowProblems }), { step: "s1" });

    await opened();
    await screen.findByRole("group", { name: "Run only when" });
    await expectNoAxeViolations();
  });

  it("adds a step at a wire with the mouse, and keeps a flat sequence flat in what it saves", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    press(
      screen.getByRole("button", { name: "Add a step between Partition the disk and Apply image" }),
    );
    const menu = await screen.findByRole("menu");
    press(within(menu).getByRole("menuitem", { name: "Run script" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "runScript",
        "applyImage",
        "runScript",
      ]);
    }, saveWait);

    const saved = saves.at(-1);
    // The page sends the highest version it knows; the server stores the lowest the steps need.
    expect(saved?.definition.version).toBe(SEQUENCE_VERSION);
    expect(Object.keys(saved?.definition ?? {})).toEqual(["version", "steps"]);

    for (const step of saved?.definition.steps ?? []) {
      expect(Object.keys(step)).not.toContain("when");
      expect(Object.keys(step)).not.toContain("shares");
      expect(Object.keys(step)).not.toContain("runAs");
    }

    // The new step is chosen, with the focus on it.
    await waitFor(() => {
      expect(node("Step 2, Run script")).toHaveFocus();
    });
    expect(node("Step 2, Run script")).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("textbox", { name: "Name" })).toHaveValue("Run script");
  });

  it("adds a step from the palette by dragging it with the keyboard", async () => {
    const { saves } = serve(administrator, view(), { step: "p" });

    await opened();
    const palette = screen.getByRole("complementary", { name: "Add to the flow" });
    const pause = within(palette).getByRole("button", { name: /^Pause/ });

    act(() => {
      pause.focus();
    });
    key(pause, "Enter");

    // The drag starts on the next frame, at a gap.
    await waitFor(() => {
      expect(document.activeElement?.getAttribute("aria-label")).toMatch(/^Add a step/);
    });

    for (
      let tries = 0;
      tries < 10 &&
      document.activeElement?.getAttribute("aria-label") !== "Add a step after Set wallpaper";
      tries++
    ) {
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "Tab" });
    }

    expect(document.activeElement).toHaveAccessibleName("Add a step after Set wallpaper");
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Enter" });
    fireEvent.keyUp(document.activeElement ?? document.body, { key: "Enter" });

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps.map((step) => step.kind)).toEqual([
        "partition",
        "applyImage",
        "runScript",
        "pause",
      ]);
    }, saveWait);
    expect(node("Step 4, Pause")).toHaveAttribute("aria-current", "true");
  });

  it("moves through the flow with the arrow keys, opens a node's fields with Enter and goes back with Escape", async () => {
    serve(administrator, treeView(), { step: "p" });

    await opened();
    const first = node("Step 1, Partition the disk");

    act(() => {
      first.focus();
    });
    fireEvent.keyDown(first, { key: "ArrowDown" });
    expect(node("Step 2, If: Is it a Latitude?")).toHaveFocus();

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowDown" });
    expect(node(/^Step 1 of Then of/)).toHaveFocus();

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowRight" });
    const otherwise = node(/^Step 1 of Else of/);
    expect(otherwise).toHaveFocus();
    expect(otherwise).toHaveAttribute("aria-current", "true");

    fireEvent.keyDown(otherwise, { key: "Enter" });
    const name = screen.getByRole("textbox", { name: "Name" });
    await waitFor(() => {
      expect(name).toHaveFocus();
    });
    expect(name).toHaveValue("Apply Windows 11");

    fireEvent.keyDown(name, { key: "Escape" });
    await waitFor(() => {
      expect(node(/^Step 1 of Else of/)).toHaveFocus();
    });

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });
    expect(node("Step 2, If: Is it a Latitude?")).toHaveFocus();
  });

  it("moves a node within its list with Alt and an arrow key, says where it went, and duplicates it with Ctrl+D", async () => {
    const { saves } = serve(administrator, treeView(), { step: "a1" });

    await opened();
    const apply = node(/^Step 1 of Then of/);

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "ArrowDown", altKey: true });

    expect(
      node(/^Step 2 of Then of 'If: Is it a Latitude\?', Apply Windows 11 for Latitudes/),
    ).toHaveFocus();
    expect(
      screen.getByText("Apply Windows 11 for Latitudes moved to position 2 of 2."),
    ).toBeInTheDocument();

    // At the end of its list it stays.
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "ArrowDown", altKey: true });
    fireEvent.keyDown(document.activeElement ?? document.body, { key: "d", ctrlKey: true });

    await waitFor(() => {
      const branch = saves.at(-1)?.definition.steps[1] as IfStep | undefined;

      expect(branch?.then.map((step) => step.name)).toEqual([
        "Add the Latitude drivers",
        "Apply Windows 11 for Latitudes",
        "Apply Windows 11 for Latitudes",
      ]);
    }, saveWait);
    await waitFor(() => {
      expect(node(/^Step 3 of Then of/)).toHaveFocus();
    });
  });

  it("wraps a node in an IF from its menu", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    const apply = node("Step 2, Apply image");

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "F10", shiftKey: true });
    const menu = await screen.findByRole("menu", { name: "Actions for Apply image" });
    press(within(menu).getByRole("menuitem", { name: "Wrap in an If" }));

    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual([
        "p",
        { [(saves.at(-1)?.definition.steps[1] as IfStep).id]: { then: ["i"], else: [] } },
        "s",
      ]);
    }, saveWait);
    await waitFor(() => {
      expect(node("Step 2, If: If")).toHaveFocus();
    });
  });

  it("removes a node with Delete, brings it back from the toast, and undoes and redoes with Ctrl+Z and Ctrl+Y", async () => {
    const { saves } = serve(administrator, view(), { step: "i" });

    await opened();
    const apply = node("Step 2, Apply image");

    act(() => {
      apply.focus();
    });
    fireEvent.keyDown(apply, { key: "Delete" });

    // A removal is saved at once, and the node after it takes the focus.
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    });
    await waitFor(() => {
      expect(node(/^Step 2, Set wallpaper/)).toHaveFocus();
    });

    const toast = screen.getByRole("alertdialog");
    expect(toast).toHaveTextContent("Removed Apply image.");
    press(within(toast).getByRole("button", { name: "Undo" }));

    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "i", "s"]);
    }, saveWait);

    // In a text field, the keys are the field's own.
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Name" }), { key: "z", ctrlKey: true });
    expect(node(/^Step 2, Apply image/)).toBeInTheDocument();

    fireEvent.keyDown(document.body, { key: "z", ctrlKey: true });
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    }, saveWait);

    fireEvent.keyDown(document.body, { key: "y", ctrlKey: true });
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "i", "s"]);
    }, saveWait);

    // The header's keys do the same.
    press(screen.getByRole("button", { name: "Undo" }));
    await waitFor(() => {
      expect(ids(saves.at(-1)?.definition.steps ?? [])).toEqual(["p", "s"]);
    }, saveWait);
  });

  it("copies and pastes a node with new ids, through the clipboard and without it", async () => {
    let clip = "";
    const writeText = vi.fn((text: string) => {
      clip = text;
      return Promise.resolve();
    });
    const readText = vi.fn(() => Promise.resolve(clip));

    Object.defineProperty(window.navigator, "clipboard", {
      configurable: true,
      value: { writeText, readText },
    });

    try {
      const { saves } = serve(administrator, view(), { step: "s" });

      await opened();
      const script = node(/^Step 3, Set wallpaper/);

      act(() => {
        script.focus();
      });
      fireEvent.keyDown(script, { key: "c", ctrlKey: true });
      expect(writeText).toHaveBeenCalledOnce();
      expect(JSON.parse(clip)).toMatchObject({
        ddtFlow: 1,
        nodes: [{ id: "s", name: "Set wallpaper" }],
      });

      const first = node("Step 1, Partition the disk");
      act(() => {
        first.focus();
      });
      fireEvent.keyDown(first, { key: "v", ctrlKey: true });

      await waitFor(() => {
        expect(saves.at(-1)?.definition.steps).toHaveLength(4);
      }, saveWait);
      const pasted = saves.at(-1)?.definition.steps[1];
      expect(pasted).toMatchObject({ kind: "runScript", name: "Set wallpaper", script: "exit 0" });
      expect(pasted?.id).not.toBe("s");
      await waitFor(() => {
        expect(node(/^Step 2, Set wallpaper/)).toHaveFocus();
      });

      // A browser that keeps the clipboard from the page pastes what the page copied.
      readText.mockRejectedValue(new Error("Not allowed"));
      fireEvent.keyDown(document.activeElement ?? document.body, { key: "v", ctrlKey: true });

      await waitFor(() => {
        expect(saves.at(-1)?.definition.steps).toHaveLength(5);
      }, saveWait);
      expect(new Set(saves.at(-1)?.definition.steps.map((step) => step.id)).size).toBe(5);
    } finally {
      Reflect.deleteProperty(window.navigator, "clipboard");
    }
  });
});
