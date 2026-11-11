// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type {
  ApplicationBundleView,
  SaveApplicationBundleRequest,
} from "@/applications/applications";
import { chooseMenuItem, chooseOption, fill, press } from "@/test/aria";
import { applicationView, bundleView } from "@/test/applicationBuilders";
import { administrator } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const sevenZip = applicationView({ id: "a1" });
const firefox = applicationView({ id: "a2", name: "Firefox", files: null });
const teams = applicationView({ id: "a3", name: "Teams", files: null });
const office = bundleView({ id: "b1", name: "Office basics", members: ["a1"] });

async function openBundles(
  bundles: ApplicationBundleView[],
  routes: Routes = {},
): Promise<RenderedPage> {
  const page = await renderPage({
    path: "/library/applications",
    user: administrator,
    routes: {
      "GET /api/applications": { body: [sevenZip, firefox, teams] },
      "GET /api/application-bundles": { body: bundles },
      ...routes,
    },
  });

  press(await screen.findByRole("tab", { name: "Bundles" }));

  return page;
}

function bundleRow(name: string): HTMLElement {
  return within(screen.getByRole("grid", { name: "Bundles", hidden: true })).getByRole("row", {
    name: new RegExp(`^${name}`),
    hidden: true,
  });
}

describe("the Bundles tab", () => {
  it("says when there is no bundle", async () => {
    await openBundles([]);

    expect(await screen.findByText("No bundles yet")).toBeInTheDocument();
  });

  it("adds a bundle with its applications in the order chosen", async () => {
    const posts: SaveApplicationBundleRequest[] = [];
    await openBundles([office], {
      "POST /api/application-bundles": (request) => {
        const body = request.body as SaveApplicationBundleRequest;
        posts.push(body);

        return json(bundleView({ ...body, id: "b2" }), 201);
      },
    });

    press(await screen.findByRole("button", { name: "Add bundle" }));
    const drawer = await screen.findByRole("dialog", { name: "Bundle New bundle" });
    fill(within(drawer).getByRole("textbox", { name: "Name" }), " Browsers ");
    await chooseOption(drawer, "Add an application", "Firefox");
    await chooseOption(drawer, "Add an application", "Teams");
    await chooseOption(drawer, "Add an application", "7-Zip");
    press(within(drawer).getByRole("button", { name: "Move 7-Zip up" }));
    press(within(drawer).getByRole("button", { name: "Remove Teams" }));
    press(within(drawer).getByRole("button", { name: "Save bundle" }));

    await waitFor(() => {
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    });
    expect(posts).toEqual([
      { revision: 0, name: "Browsers", description: null, members: ["a2", "a1"] },
    ]);
    expect(within(bundleRow("Browsers")).getByText("Firefox, 7-Zip")).toBeInTheDocument();
  });

  it("shows a member's problem at that member", async () => {
    await openBundles([office], {
      "PUT /api/application-bundles/b1": () =>
        json(
          {
            title: "One or more validation errors occurred.",
            errors: { "members[1]": ["This application is no longer in the library."] },
          },
          400,
        ),
    });

    press(await screen.findByRole("row", { name: /^Office basics/ }));
    const drawer = await screen.findByRole("dialog", { name: "Bundle Office basics" });
    await chooseOption(drawer, "Add an application", "Teams");
    press(within(drawer).getByRole("button", { name: "Save bundle" }));

    const members = within(drawer).getByRole("group", { name: "Applications" });
    const second = await within(members).findByText(
      "This application is no longer in the library.",
    );
    expect(second.closest("li")).toHaveTextContent(/^2Teams/);
  });

  it("deletes a bundle and keeps its applications", async () => {
    await openBundles([office], {
      "DELETE /api/application-bundles/b1": () => json(null, 204),
    });

    await screen.findByRole("grid", { name: "Bundles" });
    await chooseMenuItem(
      screen.getByRole("button", { name: "Actions for Office basics" }),
      "Delete",
    );
    const dialog = await screen.findByRole("dialog", { name: "Delete Office basics?" });

    expect(dialog).toHaveTextContent(
      "The bundle Office basics is deleted. Its applications stay in the library.",
    );
    press(within(dialog).getByRole("button", { name: "Delete bundle" }));

    expect(await screen.findByText("No bundles yet")).toBeInTheDocument();
  });

  it("takes bundles changed and removed elsewhere from the hub", async () => {
    const { hub, server } = await openBundles([office]);

    await screen.findByRole("grid", { name: "Bundles" });
    act(() => {
      hub?.push("applicationBundleChanged", { ...office, members: ["a1", "a2"], revision: 2 });
    });

    expect(
      await within(bundleRow("Office basics")).findByText("2 applications"),
    ).toBeInTheDocument();
    expect(bundleRow("Office basics")).toHaveClass("live-flash");

    act(() => {
      hub?.push("applicationBundlesRemoved", { bundleIds: ["b1"] });
    });

    expect(await screen.findByText("No bundles yet")).toBeInTheDocument();
    expect(server.count("GET /api/application-bundles")).toBe(1);
  });
});
