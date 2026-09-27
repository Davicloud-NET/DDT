// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import { ConfirmDialog } from "./Dialog";
import { SequenceRail, SequenceRailPicker, SequenceRailStrip } from "./SequenceRail";

function renderWithI18n(node: ReactNode) {
  return render(<I18nProvider i18n={i18n}>{node}</I18nProvider>);
}

describe("ConfirmDialog", () => {
  it("keeps the confirm key off until the word is typed exactly", () => {
    const onConfirm = vi.fn();

    renderWithI18n(
      <ConfirmDialog
        isOpen
        onOpenChange={() => undefined}
        title="Erase disk 0?"
        confirmLabel="Erase and run"
        danger
        typedWord="ERASE"
        onConfirm={onConfirm}
      >
        <p>Every partition on disk 0 goes.</p>
      </ConfirmDialog>,
    );

    const confirm = screen.getByRole("button", { name: "Erase and run" });
    const field = screen.getByRole("textbox", { name: "Type ERASE to go on" });

    expect(field).toHaveFocus();
    expect(confirm).toBeDisabled();

    fireEvent.change(field, { target: { value: "erase" } });
    expect(confirm).toBeDisabled();

    fireEvent.change(field, { target: { value: "ERASE" } });
    expect(confirm).toBeEnabled();
  });

  it("puts the focus on the safe way out when nothing has to be typed", () => {
    renderWithI18n(
      <ConfirmDialog
        isOpen
        onOpenChange={() => undefined}
        title="Stop the run?"
        confirmLabel="Stop the run"
        danger
        onConfirm={() => undefined}
      >
        <p>The machine stops after the step it is on.</p>
      </ConfirmDialog>,
    );

    expect(screen.getByRole("button", { name: "Cancel" })).toHaveFocus();
  });
});

describe("the sequence rail", () => {
  it("says each step in words for screen readers", () => {
    renderWithI18n(
      <SequenceRail
        steps={[
          { state: "done", name: "Partition the disk" },
          { state: "running", percent: 62, name: "Apply image" },
        ]}
        describe={(step, index) => `Step ${String(index + 1)}, ${step.state}`}
      />,
    );

    expect(screen.getByText("Step 1, done")).toBeInTheDocument();
    expect(screen.getByText("Step 2, running")).toBeInTheDocument();
  });

  it("labels the strip of a table row as one image", () => {
    renderWithI18n(
      <SequenceRailStrip steps={[{ state: "running", percent: 18 }]} label="Step 1 of 1 running" />,
    );

    expect(screen.getByRole("img", { name: "Step 1 of 1 running" })).toBeInTheDocument();
  });
});

describe("the rail of a sequence being edited", () => {
  it("picks a step, and moves the focused one with Alt and an arrow key", () => {
    const onSelect = vi.fn();
    const onMove = vi.fn();

    renderWithI18n(
      <SequenceRailPicker
        label="Steps"
        steps={[
          { id: "a", state: "waiting", name: "Partition", label: "Step 1, Partition" },
          { id: "b", state: "waiting", name: "Apply", label: "Step 2, Apply", mark: "problem" },
        ]}
        selectedId="a"
        onSelect={onSelect}
        onMove={onMove}
      />,
    );

    expect(screen.getByRole("option", { name: "Step 1, Partition" })).toHaveAttribute(
      "aria-selected",
      "true",
    );

    fireEvent.click(screen.getByRole("option", { name: "Step 2, Apply" }));
    expect(onSelect).toHaveBeenCalledWith("b");

    const first = screen.getByRole("option", { name: "Step 1, Partition" });
    act(() => {
      first.focus();
    });
    fireEvent.keyDown(first, { key: "ArrowRight", altKey: true });
    expect(onMove).toHaveBeenCalledWith("a", 1);

    // The first step goes no further ahead.
    fireEvent.keyDown(first, { key: "ArrowLeft", altKey: true });
    expect(onMove).toHaveBeenCalledTimes(1);
  });
});
