// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { i18n } from "@lingui/core";
import { I18nProvider } from "@lingui/react";
import { fireEvent, render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { layoutFlow } from "@/sequences/flow/flowLayout";
import { expectNoAxeViolations } from "@/test/axe";
import { branch, leaf } from "@/test/trees";

import { FlowNode } from "./FlowNode";
import { FlowViewport } from "./FlowViewport";
import { FlowDots } from "./FlowDots";
import { FlowWires, type WireTone } from "./FlowWires";
import { Minimap } from "./Minimap";

// Inside a landmark, the way a page puts them.
function renderWithI18n(node: ReactNode) {
  return render(
    <I18nProvider i18n={i18n}>
      <main>{node}</main>
    </I18nProvider>,
  );
}

// The content layer's transform, as "x y scale".
function shown(canvas: HTMLElement): string {
  const layer = canvas.firstElementChild as HTMLElement | null;
  const match = /translate\((-?[\d.]+)px, (-?[\d.]+)px\) scale\(([\d.]+)\)/.exec(
    layer?.style.transform ?? "",
  );

  return match === null ? "" : `${match[1] ?? ""} ${match[2] ?? ""} ${match[3] ?? ""}`;
}

describe("FlowViewport", () => {
  beforeEach(() => {
    // jsdom lays nothing out: the canvas is 800 by 600 pixels, and pointers can be captured.
    vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(800);
    vi.spyOn(HTMLElement.prototype, "clientHeight", "get").mockReturnValue(600);
    Element.prototype.setPointerCapture = vi.fn();
    Element.prototype.hasPointerCapture = vi.fn(() => true);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  function canvas(onTransformChange = vi.fn()) {
    renderWithI18n(
      <FlowViewport
        label="Flow of Lab PCs"
        contentWidth={400}
        contentHeight={300}
        onTransformChange={onTransformChange}
        minimap={[{ x: 0, y: 0, w: 236, h: 64, tone: "node" }]}
      >
        <div data-flow-node>Partition the disk</div>
        <input aria-label="Name" />
      </FlowViewport>,
    );

    return screen.getByRole("group", { name: "Flow of Lab PCs" });
  }

  it("starts with the content fitted in the middle, and zooms with its keys and controls", () => {
    const group = canvas();

    expect(shown(group)).toBe("200 150 1");
    expect(screen.getByText("100%")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Zoom in" }));
    expect(screen.getByText("125%")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Zoom out" }));
    fireEvent.click(screen.getByRole("button", { name: "Zoom out" }));
    expect(screen.getByText("80%")).toBeInTheDocument();

    fireEvent.keyDown(group, { key: "0" });
    expect(screen.getByText("100%")).toBeInTheDocument();
    fireEvent.keyDown(group, { key: "+" });
    expect(screen.getByText("125%")).toBeInTheDocument();
    // Ctrl with + zooms the browser's page, and a text field keeps its own key presses.
    fireEvent.keyDown(group, { key: "-", ctrlKey: true });
    fireEvent.keyDown(screen.getByRole("textbox", { name: "Name" }), { key: "-" });
    expect(screen.getByText("125%")).toBeInTheDocument();
    fireEvent.keyDown(group, { key: "1" });
    expect(shown(group)).toBe("200 150 1");
  });

  it("zooms at the pointer with Ctrl and the wheel, and moves with the wheel", () => {
    const group = canvas();

    fireEvent.wheel(group, { deltaY: 40 });
    expect(shown(group)).toBe("200 110 1");

    fireEvent.wheel(group, { deltaY: -100, ctrlKey: true, clientX: 200, clientY: 110 });
    expect(screen.getByText("141%")).toBeInTheDocument();
    // The content's corner under the pointer stays under it.
    expect(shown(group).split(" ").slice(0, 2)).toEqual(["200", "110"]);
  });

  it("moves when the background is dragged, but not from a node or a control", () => {
    const changes = vi.fn();
    const group = canvas(changes);

    fireEvent.pointerDown(group, { pointerId: 1, button: 0, clientX: 100, clientY: 100 });
    fireEvent.pointerMove(group, { pointerId: 1, clientX: 130, clientY: 80 });
    fireEvent.pointerMove(group, { pointerId: 1, clientX: 150, clientY: 60 });
    fireEvent.pointerUp(group, { pointerId: 1 });
    expect(shown(group)).toBe("250 110 1");
    expect(changes).toHaveBeenLastCalledWith({ x: 250, y: 110, scale: 1 });

    fireEvent.pointerDown(screen.getByText("Partition the disk"), {
      pointerId: 2,
      button: 0,
      clientX: 100,
      clientY: 100,
    });
    fireEvent.pointerMove(group, { pointerId: 2, clientX: 300, clientY: 300 });
    expect(shown(group)).toBe("250 110 1");
  });

  it("scales with two fingers", () => {
    const group = canvas();

    fireEvent.pointerDown(group, { pointerId: 1, clientX: 100, clientY: 100 });
    fireEvent.pointerDown(group, { pointerId: 2, clientX: 200, clientY: 100 });
    fireEvent.pointerMove(group, { pointerId: 2, clientX: 300, clientY: 100 });

    expect(screen.getByText("200%")).toBeInTheDocument();
  });

  it("has nothing axe finds wrong", async () => {
    canvas();

    await expectNoAxeViolations();
  });
});

describe("FlowNode", () => {
  it("shows a leaf's glyph, number, name and detail, and a finding on its module", async () => {
    const { container } = renderWithI18n(
      <FlowNode
        kind="applyImage"
        name="Apply Windows 11"
        number={3}
        detail="Windows 11 Enterprise"
        mark="problem"
      />,
    );

    expect(screen.getByText("03")).toBeInTheDocument();
    expect(screen.getByText("Apply Windows 11")).toBeInTheDocument();
    expect(screen.getByText("Windows 11 Enterprise")).toHaveClass("text-fail-text");
    expect(container.querySelector(".hatch-fail")).not.toBeNull();

    await expectNoAxeViolations();
  });

  it("names containers by kind, and gives an IF its Then and Else ports", () => {
    renderWithI18n(
      <>
        <FlowNode
          kind="if"
          name="Is it a Latitude?"
          detail="Model contains Latitude"
          branch="then"
          state="done"
        />
        <FlowNode kind="group" name="Berlin office" />
        <FlowNode kind="repeat" name="Wait for the share" />
      </>,
    );

    expect(screen.getByText("If: Is it a Latitude?")).toBeInTheDocument();
    expect(screen.getByText("Group: Berlin office")).toBeInTheDocument();
    expect(screen.getByText("Repeat: Wait for the share")).toBeInTheDocument();
    expect(screen.getByText("Then")).toHaveClass("text-ink");
    expect(screen.getByText("Else")).toHaveClass("text-muted");
  });

  it("tags a run's running, paused and failed nodes, and outlines one not taken", () => {
    const { container } = renderWithI18n(
      <>
        <FlowNode kind="runScript" name="Install" state="running" percent={40} />
        <FlowNode kind="pause" name="Check the BIOS" state="paused" />
        <FlowNode kind="reboot" name="Restart" state="failed" />
        <FlowNode kind="applyImage" name="Apply Windows 10" state="notTaken" />
      </>,
    );

    expect(screen.getByText("Running")).toBeInTheDocument();
    expect(screen.getByText("Paused")).toBeInTheDocument();
    expect(screen.getByText("Failed")).toBeInTheDocument();
    expect(container.querySelector(".rail-live")).not.toBeNull();
    expect(container.querySelector(".bg-attention")).not.toBeNull();
    expect(screen.getByText("Apply Windows 10")).toHaveClass("text-muted");
  });

  it("shows a collapsed container's steps as a rail strip", () => {
    renderWithI18n(
      <FlowNode
        kind="group"
        name="Berlin office"
        collapsed
        strip={[{ state: "done" }, { state: "waiting" }]}
      />,
    );

    expect(screen.getByRole("img", { name: "Steps of Berlin office" })).toBeInTheDocument();
  });
});

describe("FlowWires and FlowDots", () => {
  const layout = layoutFlow([leaf("a"), branch("b", [leaf("c")], [leaf("d")]), leaf("e")]);
  const tone = (route: { branch: { name: string } | null; to: string | null }): WireTone =>
    route.branch?.name === "else" || route.to === "d" ? "not" : "taken";

  it("draws a path per tone, the branch not taken dashed", () => {
    const { container } = render(
      <FlowWires
        width={layout.width}
        height={layout.height}
        wires={layout.wires}
        arrows={layout.arrows}
        tone={tone}
      />,
    );
    const paths = [...container.querySelectorAll("path")];

    expect(paths.map((path) => path.getAttribute("class"))).toEqual([
      expect.stringContaining("stroke-dasharray:5_5"),
      "fill-line",
      expect.stringContaining("stroke-ink"),
      "fill-ink",
    ]);
    // Every arrowhead is drawn: into b, c, d and e.
    expect(paths.flatMap((path) => (path.getAttribute("d") ?? "").match(/Z/g) ?? [])).toHaveLength(
      4,
    );
  });

  it("draws the ports on the IF's card edge and the join", () => {
    const { container } = render(
      <FlowDots
        width={layout.width}
        height={layout.height}
        ports={layout.ports}
        joins={layout.joins}
        selectedId="b"
      />,
    );
    const circles = [...container.querySelectorAll("circle")];

    expect(circles).toHaveLength(3);
    expect(circles[0]?.getAttribute("class")).toContain("stroke-selected");
    expect(circles[2]?.getAttribute("class")).toContain("stroke-well");
  });
});

describe("Minimap", () => {
  beforeEach(() => {
    Element.prototype.setPointerCapture = vi.fn();
    Element.prototype.hasPointerCapture = vi.fn(() => true);
  });

  it("draws the content in small and moves the canvas to where it is pressed", () => {
    const onNavigate = vi.fn();
    const { container } = render(
      <Minimap
        width={1_760}
        height={1_120}
        items={[
          { x: 0, y: 0, w: 400, h: 400, tone: "frame" },
          { x: 16, y: 16, w: 236, h: 84, tone: "node" },
        ]}
        visible={{ x: 0, y: 0, w: 800, h: 600 }}
        onNavigate={onNavigate}
      />,
    );
    const map = container.querySelector("svg");

    if (map === null) {
      throw new Error("No minimap.");
    }

    expect(map).toHaveAttribute("width", "176");
    expect(map.querySelectorAll("rect")).toHaveLength(3);

    fireEvent.pointerDown(map, { pointerId: 1, clientX: 88, clientY: 56 });
    expect(onNavigate).toHaveBeenLastCalledWith({ x: 880, y: 560 });
  });
});
