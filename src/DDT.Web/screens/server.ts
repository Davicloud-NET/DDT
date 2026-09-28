// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { Page } from "@playwright/test";

import type { CurrentUser } from "@/auth/auth";

// The server a page is shown against. The API answers from answers, keyed "METHOD path" with or without the query. The
// hub connects and then stays quiet, so the page is live and shows no lost-connection banner. Returns the requests
// nothing answered, which a test can print to see what a page reads.
export async function serve(
  page: Page,
  user: CurrentUser | null,
  answers: Record<string, unknown>,
): Promise<string[]> {
  const unanswered: string[] = [];

  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
      const request = route.request();
      const url = new URL(request.url());

      if (url.pathname === "/api/auth/me") {
        await (user === null ? route.fulfill({ status: 401 }) : route.fulfill({ json: user }));
        return;
      }

      if (url.pathname === "/api/auth/session") {
        await route.fulfill({ status: 204, headers: { "X-CSRF-TOKEN": "token" } });
        return;
      }

      const answer =
        answers[`${request.method()} ${url.pathname}${url.search}`] ??
        answers[`${request.method()} ${url.pathname}`];

      if (answer === undefined) {
        unanswered.push(`${request.method()} ${url.pathname}${url.search}`);
        await route.fulfill({ status: 404 });
        return;
      }

      // A Buffer is a picture, such as the console's logo; anything else is JSON.
      await (Buffer.isBuffer(answer)
        ? route.fulfill({ body: answer, contentType: "image/png" })
        : route.fulfill({ json: answer }));
    },
  );

  await quietHub(page);

  return unanswered;
}

// SignalR over long polling. The negotiation offers only that transport. The first poll opens the connection and the
// second brings the handshake's answer. Later polls are never answered, just as a hub with nothing to say holds them.
async function quietHub(page: Page): Promise<void> {
  let polls = 0;

  await page.route(
    (url) => url.pathname.startsWith("/hubs/live"),
    async (route) => {
      const request = route.request();

      if (new URL(request.url()).pathname.endsWith("/negotiate")) {
        await route.fulfill({
          json: {
            negotiateVersion: 1,
            connectionId: "screens",
            connectionToken: "screens",
            availableTransports: [
              { transport: "LongPolling", transferFormats: ["Text", "Binary"] },
            ],
          },
        });
        return;
      }

      if (request.method() !== "GET") {
        await route.fulfill({ status: 200, body: "" });
        return;
      }

      polls++;

      if (polls === 1) {
        await route.fulfill({ status: 200, body: "" });
      } else if (polls === 2) {
        await route.fulfill({ status: 200, contentType: "text/plain", body: "{}\u001e" });
      }
    },
  );
}
