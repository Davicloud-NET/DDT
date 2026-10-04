// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { press } from "@/test/aria";
import {
  administrator,
  bootImageView,
  imageSummary,
  machineSummary,
  operator,
} from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { rowOf } from "@/test/rowOf";

import type { SetupChecklist } from "./checklist";

const fresh: SetupChecklist = {
  password: true,
  netboot: true,
  bootImage: false,
  image: false,
  sequence: true,
  machine: false,
};

function open(checklist: SetupChecklist, user = administrator) {
  return renderPage({
    path: "/machines",
    user,
    routes: {
      "GET /api/machines": { body: [] },
      "GET /api/server/checklist": { body: checklist },
    },
  });
}

// The step's mark, by the words of the step
function mark(panel: HTMLElement, words: RegExp): string | null {
  return within(rowOf(within(panel).getByText(words), "li"))
    .getByRole("img")
    .getAttribute("aria-label");
}

describe("SetupChecklist", () => {
  afterEach(() => {
    window.localStorage.clear();
  });

  it("shows what is still to do, ticks a step when the hub says it is done, and goes once all are", async () => {
    const { hub, server } = await open(fresh);

    const panel = rowOf(
      await screen.findByRole("heading", { name: "Before the first deployment" }),
      "section",
    );
    expect(mark(panel, /which the installer left/)).toBe("Done");
    expect(mark(panel, /that machines netboot into/)).toBe("Open");
    expect(within(panel).getByRole("link", { name: "Build the boot image" })).toHaveAttribute(
      "href",
      "/boot/image",
    );

    act(() => {
      hub?.push("bootImageChanged", bootImageView());
    });
    await waitFor(() => {
      expect(mark(panel, /that machines netboot into/)).toBe("Done");
    });

    act(() => {
      hub?.push("imageChanged", imageSummary());
      hub?.push("machineChanged", machineSummary());
    });
    await waitFor(() => {
      expect(
        screen.queryByRole("heading", { name: "Before the first deployment" }),
      ).not.toBeInTheDocument();
    });
    expect(server.count("GET /api/server/checklist")).toBe(1);
  });

  it("stays away on the first page once dismissed", async () => {
    const first = await open(fresh);

    press(await screen.findByRole("button", { name: "Dismiss" }));
    expect(
      screen.queryByRole("heading", { name: "Before the first deployment" }),
    ).not.toBeInTheDocument();
    expect(window.localStorage.getItem("ddt.checklist.dismissed")).toBe("1");
    first.queryClient.clear();
  });

  it("asks nothing for someone who is no administrator", async () => {
    const { server } = await open(fresh, operator);

    await waitFor(() => {
      expect(server.count("GET /api/machines")).toBeGreaterThan(0);
    });
    expect(server.count("GET /api/server/checklist")).toBe(0);
  });
});
