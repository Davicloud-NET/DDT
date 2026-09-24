// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import type { AboutInfo } from "@/about/about";

import { AboutPage } from "./AboutPage";

const about: AboutInfo = {
  product: "DDT",
  version: "1.0.0+0f1e2d3c",
  attribution: "DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud.",
  license: "GPL-3.0-or-later",
  sourceUrl: "https://github.com/Davicloud-NET/DDT",
  legalDocuments: [
    "LICENSE",
    "NOTICE",
    "THIRD-PARTY-NOTICES.md",
    "licenses/npgsql/LICENSE",
    "licenses/web/THIRD-PARTY-LICENSES.txt",
    "licenses/wimlib/COPYING.MIT",
  ],
};

const webLicences = "licenses/web/THIRD-PARTY-LICENSES.txt";

function renderWith(status: number, body?: unknown) {
  vi.stubGlobal("scrollTo", vi.fn());
  vi.stubGlobal(
    "fetch",
    vi.fn((input: RequestInfo | URL) => {
      const url = input instanceof Request ? input.url : input.toString();

      return Promise.resolve(
        url.replace("http://localhost", "") === "/api/about"
          ? new Response(body === undefined ? null : JSON.stringify(body), { status })
          : new Response(null, { status: 404 }),
      );
    }),
  );

  const rootRoute = createRootRoute();
  const router = createRouter({
    routeTree: rootRoute.addChildren([
      createRoute({ getParentRoute: () => rootRoute, path: "/", component: () => <h1>DDT</h1> }),
      createRoute({ getParentRoute: () => rootRoute, path: "/about", component: AboutPage }),
    ]),
    history: createMemoryHistory({ initialEntries: ["/about"] }),
  });

  render(
    <QueryClientProvider
      client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}
    >
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

function link(name: string): HTMLElement {
  return screen.getByRole("link", { name });
}

describe("AboutPage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the legal notices and the version the server runs", async () => {
    renderWith(200, about);

    expect(await screen.findByText("Version 1.0.0+0f1e2d3c")).toBeInTheDocument();
    expect(
      screen.getByText("DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud."),
    ).toBeInTheDocument();
    expect(screen.getByText(/^DDT comes with ABSOLUTELY NO WARRANTY/)).toBeInTheDocument();
    expect(
      screen.getByText(
        /^DDT is free software: .* GNU General Public License .* version 3 .* any later version, together with additional terms under section 7/,
      ),
    ).toBeInTheDocument();
  });

  it("links to the licence, the notices and the source code", async () => {
    renderWith(200, about);

    expect(await screen.findByText("Version 1.0.0+0f1e2d3c")).toBeInTheDocument();
    expect(link("LICENSE")).toHaveAttribute("href", "/api/about/legal/LICENSE");
    expect(link("NOTICE")).toHaveAttribute("href", "/api/about/legal/NOTICE");
    expect(link("THIRD-PARTY-NOTICES.md")).toHaveAttribute(
      "href",
      "/api/about/legal/THIRD-PARTY-NOTICES.md",
    );
    expect(link("https://github.com/Davicloud-NET/DDT")).toHaveAttribute(
      "href",
      "https://github.com/Davicloud-NET/DDT",
    );

    for (const web of screen.getAllByRole("link", { name: webLicences })) {
      expect(web).toHaveAttribute("href", `/api/about/legal/${webLicences}`);
    }
  });

  it("links to every licence text the server carries", async () => {
    renderWith(200, about);

    expect(await screen.findByRole("link", { name: "licenses/npgsql/LICENSE" })).toHaveAttribute(
      "href",
      "/api/about/legal/licenses/npgsql/LICENSE",
    );
    expect(link("licenses/wimlib/COPYING.MIT")).toHaveAttribute(
      "href",
      "/api/about/legal/licenses/wimlib/COPYING.MIT",
    );
    // Once among the notices, once among the texts.
    expect(screen.getAllByRole("link", { name: webLicences })).toHaveLength(2);
  });

  it("shows the notices without the server", async () => {
    renderWith(503);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "The version and the licence texts could not be loaded.",
    );
    expect(
      screen.getByText("DDT, the Davicloud Deployment Toolkit. Copyright (C) 2026 Davicloud."),
    ).toBeInTheDocument();
    expect(screen.getByText(/^DDT comes with ABSOLUTELY NO WARRANTY/)).toBeInTheDocument();
    expect(link("LICENSE")).toHaveAttribute("href", "/api/about/legal/LICENSE");
    expect(link(webLicences)).toHaveAttribute("href", `/api/about/legal/${webLicences}`);
    expect(screen.queryByText(/^Version /)).not.toBeInTheDocument();
  });
});
