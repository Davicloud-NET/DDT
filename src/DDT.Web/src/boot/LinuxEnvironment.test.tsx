// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { expectNoAxeViolations } from "@/test/axe";
import { administrator, bootImageView, linuxBootView, operator } from "@/test/builders";
import { renderPage } from "@/test/renderPage";
import { rowOf } from "@/test/rowOf";

import type { BootImageView } from "./bootImage";

function open(view: BootImageView, user = administrator) {
  return renderPage({
    path: "/boot/image",
    user,
    routes: { "GET /api/boot-image": { body: view } },
  });
}

async function panel(): Promise<HTMLElement> {
  return rowOf(await screen.findByRole("heading", { name: "Linux boot environment" }), "section");
}

describe("LinuxEnvironment", () => {
  it("says what the environment a release brought is made of, who starts it and what runs in it", async () => {
    await open(bootImageView({ linux: linuxBootView() }));

    const linux = await panel();

    expect(linux).toHaveTextContent(
      "It comes with DDT, so there is nothing to build. Task sequences that write a Linux image run in it, and Windows sequences run in Windows PE.",
    );
    expect(within(linux).getByText("7.2.6+deb13-amd64")).toBeInTheDocument();
    expect(within(linux).getByText("1.51~1+deb13u1+16.1-2~deb13u1")).toBeInTheDocument();
    expect(within(linux).getByText("1+2.12+9+deb13u2")).toBeInTheDocument();
    expect(within(linux).getByText("linux/shimx64.efi")).toBeInTheDocument();
    expect(linux).toHaveTextContent(/Debian packages as of.*October.*2026/);
    expect(linux).toHaveTextContent(
      "English (United States), English (United Kingdom), German, German (Switzerland), French, Italian, Spanish",
    );
    expect(linux).toHaveTextContent(
      "With Secure Boot on, a machine starts it when its firmware trusts one of Microsoft's third-party UEFI CAs 2011 and 2023. A machine whose firmware trusts neither is not started in it.",
    );
    expect(
      within(linux).getByRole("link", { name: "the source packages it was built from" }),
    ).toHaveAttribute("href", "/api/about/legal/licenses/linux-boot/SOURCES.md");
    expect(within(linux).queryByRole("button")).not.toBeInTheDocument();
    await expectNoAxeViolations();
  });

  it("names the one certificate authority of a shim signed under one", async () => {
    await open(bootImageView({ linux: linuxBootView({ shimSignedUnder: "Microsoft2023" }) }));

    expect(await panel()).toHaveTextContent(
      "With Secure Boot on, a machine starts it when its firmware trusts Microsoft's third-party UEFI CA 2023. A machine whose firmware does not is not started in it.",
    );
  });

  it("says nothing about Secure Boot where the server found no certificate authority for the shim", async () => {
    await open(bootImageView({ linux: linuxBootView({ shimSignedUnder: null }) }));

    expect(await panel()).not.toHaveTextContent(/With Secure Boot on/);
  });

  it("says why no machine can be started in it, in the words of the server's code", async () => {
    await open(
      bootImageView({
        linux: linuxBootView({
          available: false,
          problem: "English from the server.",
          problemText: { code: "linuxBoot.noRoot", args: {} },
          shimSignedUnder: null,
        }),
      }),
    );

    const linux = await panel();

    expect(linux).toHaveTextContent(
      "The Linux boot environment cannot be started, because this server has no root certificate of its own to hand to the machines",
    );
    // What the description says is still shown.
    expect(within(linux).getByText("7.2.6+deb13-amd64")).toBeInTheDocument();
  });

  it("shows only the reason for an environment that is not there", async () => {
    await open(
      bootImageView({
        linux: linuxBootView({
          available: false,
          problem: "This server has no Linux boot environment.",
          problemText: null,
          snapshot: null,
          kernel: null,
          shim: null,
          grub: null,
          shimSignedUnder: null,
          keymaps: [],
        }),
      }),
    );

    const linux = await panel();

    expect(linux).toHaveTextContent("This server has no Linux boot environment.");
    expect(within(linux).queryByText("Kernel")).not.toBeInTheDocument();
  });

  it("is shown to an operator too", async () => {
    await open(bootImageView({ linux: linuxBootView() }), operator);

    expect(await panel()).toBeInTheDocument();
  });

  it("is left out for a server that does not say", async () => {
    await open(bootImageView());
    await screen.findByRole("heading", { name: "Served build" });

    expect(
      screen.queryByRole("heading", { name: "Linux boot environment" }),
    ).not.toBeInTheDocument();
  });
});
