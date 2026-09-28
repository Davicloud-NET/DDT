// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { act, renderHook, render, screen, waitFor } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { press } from "@/test/aria";

import { Button } from "./Button";
import { Dialog } from "./Dialog";
import { useReplay } from "./motion";
import { SequenceRailStrip } from "./SequenceRailStrip";
import { ToastRegion } from "./ToastRegion";
import { showToast, toasts } from "./toasts";

function renderWithI18n(node: ReactNode) {
  return render(<I18nProvider i18n={i18n}>{node}</I18nProvider>);
}

// Lets the exits run as in a browser: an element that is leaving runs an animation until the test finishes it.
function runningExits() {
  let finish: () => void = () => undefined;
  const finished = new Promise<void>((resolve) => {
    finish = resolve;
  });

  vi.spyOn(Element.prototype, "getAnimations").mockImplementation(function (this: Element) {
    const leaving = this.hasAttribute("data-exiting") || this.classList.contains("animate-pop-out");

    return leaving ? [{ finished } as unknown as Animation] : [];
  });

  return {
    finish: async () => {
      await act(async () => {
        finish();
        await finished;
      });
    },
  };
}

function Opener() {
  const [isOpen, setOpen] = useState(false);

  return (
    <>
      <Button
        onPress={() => {
          setOpen(true);
        }}
      >
        Rename
      </Button>
      <Dialog
        isOpen={isOpen}
        onOpenChange={setOpen}
        title="Rename the machine"
        footer={
          <Button
            onPress={() => {
              setOpen(false);
            }}
          >
            Cancel
          </Button>
        }
      >
        <p>A new name.</p>
      </Dialog>
    </>
  );
}

describe("an overlay that closes", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    act(() => {
      toasts.clear();
    });
  });

  it("stays while its exit runs, then leaves the page and gives the focus back", async () => {
    const exits = runningExits();
    renderWithI18n(<Opener />);

    const opener = screen.getByRole("button", { name: "Rename" });
    act(() => {
      opener.focus();
    });
    press(opener);
    const dialog = await screen.findByRole("dialog", { name: "Rename the machine" });

    press(screen.getByRole("button", { name: "Cancel" }));

    // Still there while it leaves, but no longer usable.
    expect(dialog).toBeInTheDocument();
    expect(dialog.closest("[data-exiting]")).not.toBeNull();

    await exits.finish();

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await waitFor(() => {
      expect(opener).toHaveFocus();
    });
  });

  it("leaves at once where nothing runs, as with reduce motion", async () => {
    renderWithI18n(<Opener />);

    press(screen.getByRole("button", { name: "Rename" }));
    await screen.findByRole("dialog");
    press(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("keeps a closed toast until it has left", async () => {
    const exits = runningExits();
    renderWithI18n(<ToastRegion />);

    act(() => {
      showToast({ title: "Lab-PC-07 is done", tone: "ok" });
    });
    press(await screen.findByRole("button", { name: "Close" }));

    expect(screen.getByText("Lab-PC-07 is done")).toBeInTheDocument();

    await exits.finish();

    expect(screen.queryByText("Lab-PC-07 is done")).not.toBeInTheDocument();
    expect(toasts.visibleToasts).toEqual([]);
  });

  it("removes a closed toast at once where nothing runs", async () => {
    renderWithI18n(<ToastRegion />);

    act(() => {
      showToast({ title: "Lab-PC-08 failed", tone: "fail" });
    });
    press(await screen.findByRole("button", { name: "Close" }));

    expect(screen.queryByText("Lab-PC-08 failed")).not.toBeInTheDocument();
  });
});

describe("replaying an entrance", () => {
  it("plays nothing first, then alternates with every change of the value", () => {
    const { result, rerender } = renderHook(({ value }) => useReplay(value), {
      initialProps: { value: "/machines" },
    });

    expect(result.current).toBeNull();

    rerender({ value: "/machines" });
    expect(result.current).toBeNull();

    rerender({ value: "/machines/runs" });
    const first = result.current;
    rerender({ value: "/admin/audit" });
    const second = result.current;

    expect(first).not.toBeNull();
    expect(second).not.toBeNull();
    expect(second).not.toBe(first);
  });
});

describe("the sequence rail's fill", () => {
  it("is one element that a step keeps as it runs and finishes, so its width can move", () => {
    const { container, rerender } = renderWithI18n(
      <SequenceRailStrip steps={[{ state: "running", percent: 40 }]} label="Step 1 running" />,
    );
    const fill = container.querySelector(".motion-fill");

    expect(fill).toHaveStyle({ width: "40%" });

    rerender(
      <I18nProvider i18n={i18n}>
        <SequenceRailStrip steps={[{ state: "done" }]} label="Step 1 done" />
      </I18nProvider>,
    );

    expect(container.querySelector(".motion-fill")).toBe(fill);
    expect(fill).toHaveStyle({ width: "100%" });
    expect(fill).not.toHaveClass("rail-live");
  });
});
