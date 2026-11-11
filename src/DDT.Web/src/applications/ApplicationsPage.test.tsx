// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import type { ApplicationBundleView, ApplicationView } from "@/applications/applications";
import { chooseMenuItem, fill, press } from "@/test/aria";
import { expectNoAxeViolations } from "@/test/axe";
import { applicationView, bundleView } from "@/test/applicationBuilders";
import { administrator, currentUser, viewer } from "@/test/builders";
import { renderPage, type RenderedPage } from "@/test/renderPage";
import { json, type Routes } from "@/test/server";

const sevenZip = applicationView({
  id: "a1",
  usedBy: [
    { id: "b1", name: "Office basics", kind: "Bundle" },
    { id: "s1", name: "Windows 11 office PCs", kind: "Sequence" },
  ],
});

const firefox = applicationView({
  id: "a2",
  name: "Firefox",
  publisher: "Mozilla",
  version: null,
  kind: "Winget",
  files: null,
  command: null,
  detection: null,
  restartExitCodes: [],
  wingetId: "Mozilla.Firefox",
});

const office = bundleView({
  id: "b1",
  name: "Office basics",
  description: "Every office PC",
  members: ["a2", "a1"],
});

function open(
  applications: ApplicationView[],
  routes: Routes = {},
  user = administrator,
  bundles: ApplicationBundleView[] = [office],
): Promise<RenderedPage> {
  return renderPage({
    path: "/library/applications",
    user,
    routes: {
      "GET /api/applications": { body: applications },
      "GET /api/application-bundles": { body: bundles },
      "GET /api/images/uploads": { body: [] },
      ...routes,
    },
  });
}

function table(): HTMLElement {
  return screen.getByRole("grid", { name: "Applications", hidden: true });
}

function row(name: string): HTMLElement {
  return within(table()).getByRole("row", { name: new RegExp(`^${name}`), hidden: true });
}

describe("ApplicationsPage", () => {
  it("lists each application with its publisher, version, kind, files and what uses it", async () => {
    await open([sevenZip, firefox]);

    await screen.findByRole("grid", { name: "Applications" });
    const zip = within(row("7-Zip"));

    expect(zip.getByText("Igor Pavlov")).toBeInTheDocument();
    expect(zip.getByText("24.09")).toBeInTheDocument();
    expect(zip.getByText("Command")).toBeInTheDocument();
    expect(zip.getByText("1.5 MB")).toBeInTheDocument();
    expect(zip.getByText("7z2409-x64.msi")).toBeInTheDocument();
    expect(zip.getByText("Office basics")).toBeInTheDocument();
    expect(zip.getByRole("link", { name: "Windows 11 office PCs" })).toHaveAttribute(
      "href",
      "/deployment/sequences/s1",
    );

    const fox = within(row("Firefox"));
    expect(fox.getByText("winget package")).toBeInTheDocument();
    expect(fox.getByText("Mozilla.Firefox")).toBeInTheDocument();
    expect(fox.getByText("None")).toBeInTheDocument();
    expect(fox.getByText("Not used")).toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("says when the library has no application", async () => {
    await open([]);

    expect(await screen.findByText("No applications yet")).toBeInTheDocument();
    expect(screen.queryByRole("searchbox")).not.toBeInTheDocument();
  });

  it("finds an application by its publisher", async () => {
    await open([sevenZip, firefox]);

    await screen.findByRole("grid", { name: "Applications" });
    fill(screen.getByRole("searchbox", { name: "Find an application" }), "mozilla");

    await waitFor(() => {
      expect(within(table()).queryByRole("row", { name: /^7-Zip/, hidden: true })).toBeNull();
    });
    expect(row("Firefox")).toBeInTheDocument();

    fill(screen.getByRole("searchbox", { name: "Find an application" }), "nothing like it");
    expect(await screen.findByText("No application matches")).toBeInTheDocument();
  });

  it("shows the bundles on their own tab with their applications in order", async () => {
    await open([sevenZip, firefox]);

    press(await screen.findByRole("tab", { name: "Bundles" }));
    const bundles = await screen.findByRole("grid", { name: "Bundles" });
    const bundle = within(within(bundles).getByRole("row", { name: /^Office basics/ }));

    expect(bundle.getByText("Every office PC")).toBeInTheDocument();
    expect(bundle.getByText("2 applications")).toBeInTheDocument();
    expect(bundle.getByText("Firefox, 7-Zip")).toBeInTheDocument();
    expect(bundle.getByText("Not used")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Add bundle" })).toBeInTheDocument();
  });

  it.each([
    ["a viewer", viewer],
    ["an operator", currentUser("Operator")],
  ])("shows %s the library without a way to change it", async (_, user) => {
    await open([sevenZip], {}, user);

    await screen.findByRole("grid", { name: "Applications" });
    expect(screen.queryByRole("button", { name: "Add application" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Actions for/ })).not.toBeInTheDocument();
    press(row("7-Zip"));
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    press(screen.getByRole("tab", { name: "Bundles" }));
    await screen.findByRole("grid", { name: "Bundles" });
    expect(screen.queryByRole("button", { name: "Add bundle" })).not.toBeInTheDocument();
  });

  it("takes applications changed and removed elsewhere from the hub and marks them", async () => {
    const { server, hub } = await open([sevenZip]);

    await screen.findByRole("grid", { name: "Applications" });
    act(() => {
      hub?.push("applicationChanged", { ...sevenZip, version: "24.10", revision: 2 });
      hub?.push("applicationChanged", firefox);
    });

    expect(await within(row("Firefox")).findByText("Mozilla.Firefox")).toBeInTheDocument();
    expect(row("Firefox")).toHaveClass("live-new");
    expect(row("7-Zip")).toHaveClass("live-flash");
    expect(within(row("7-Zip")).getByText("24.10")).toBeInTheDocument();

    act(() => {
      hub?.push("applicationsRemoved", { applicationIds: ["a1"] });
    });

    await waitFor(() => {
      expect(within(table()).queryByRole("row", { name: /^7-Zip/, hidden: true })).toBeNull();
    });
    expect(server.count("GET /api/applications")).toBe(1);
  });

  it("deletes an application nothing uses", async () => {
    await open([sevenZip, firefox], {
      "DELETE /api/applications/a2": () => json(null, 204),
    });

    await screen.findByRole("grid", { name: "Applications" });
    await chooseMenuItem(screen.getByRole("button", { name: "Actions for Firefox" }), "Delete");
    const dialog = await screen.findByRole("dialog", { name: "Delete Firefox?" });

    expect(dialog).toHaveTextContent("Firefox is deleted from the library.");
    press(within(dialog).getByRole("button", { name: "Delete application" }));

    await waitFor(() => {
      expect(within(table()).queryByRole("row", { name: /^Firefox/, hidden: true })).toBeNull();
    });
  });

  it("says what uses an application when the server keeps it", async () => {
    await open([sevenZip], {
      "DELETE /api/applications/a1": () =>
        json(
          {
            title:
              "This application is used by Office basics, Windows 11 office PCs. Take it out there first.",
            code: "application.inUse",
            args: { users: "Office basics, Windows 11 office PCs" },
          },
          409,
        ),
    });

    await screen.findByRole("grid", { name: "Applications" });
    await chooseMenuItem(screen.getByRole("button", { name: "Actions for 7-Zip" }), "Delete");
    const dialog = await screen.findByRole("dialog", { name: "Delete 7-Zip?" });

    expect(dialog).toHaveTextContent("7-Zip and its files are deleted from the library.");
    press(within(dialog).getByRole("button", { name: "Delete application" }));

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "This application is used by Office basics, Windows 11 office PCs. Take it out there first.",
    );
    expect(row("7-Zip")).toBeInTheDocument();
  });
});
