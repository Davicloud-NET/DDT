// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { screen, waitFor, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { chooseOption } from "@/test/aria";
import { deploymentOptions, machineSummary } from "@/test/builders";

import { answers, machine as pageMachine, open as openMachine } from "./MachinePage.fixtures";
import {
  assignable,
  assigning,
  installWindows,
  linux,
  now,
  open as openMachines,
  openAssign,
} from "./MachinesPage.fixtures";

const refusal =
  /^This machine is not started in the Linux boot environment: Secure Boot is on, and its firmware does not trust Microsoft's third-party UEFI CA 2011, which signs the environment's first loader\./;

// A machine with Secure Boot on whose firmware trusts only the newer of Microsoft's two CAs, and a shim signed under
// the older one.
const untrusting = { secureBootEnabled: true, trustedUefiCas: "Microsoft2023" } as const;
const options = deploymentOptions({
  serverUtc: now.toISOString(),
  bootEnvironments: ["WindowsPE", "LinuxBoot"],
  linuxShimSignedUnder: "Microsoft2011",
});

describe("why a machine is not offered the Linux boot environment", () => {
  it("is said on the machine's page", async () => {
    const routes = answers(
      [pageMachine({ state: "Approved", deployment: null, ...untrusting })],
      [],
      [],
    );
    routes["GET /api/deployments/options"] = { body: options };

    await openMachine(routes);

    expect(await screen.findByText(refusal)).toBeInTheDocument();
  });

  it("is not said on the page of a machine whose firmware trusts the shim's certificate authority", async () => {
    const routes = answers(
      [
        pageMachine({
          state: "Approved",
          deployment: null,
          secureBootEnabled: true,
          trustedUefiCas: "Microsoft2011, Microsoft2023",
        }),
      ],
      [],
      [],
    );
    routes["GET /api/deployments/options"] = { body: options };

    const { server } = await openMachine(routes);

    await waitFor(() => {
      expect(server.count("GET /api/deployments/options")).toBe(1);
    });
    expect(screen.queryByText(refusal)).not.toBeInTheDocument();
  });

  it("is said in the assign dialog for a Linux sequence, and not for a Windows one", async () => {
    const machine = machineSummary(untrusting);
    await openMachines(
      [machine],
      assigning({
        "GET /api/sequences": {
          body: [installWindows, { ...linux, rawImageBootCapability: "SecureBootOk" }],
        },
        "GET /api/deployments/options": { body: options },
      }),
    );

    const dialog = await openAssign();
    await assignable(dialog);
    expect(within(dialog).queryByText(refusal)).not.toBeInTheDocument();

    await chooseOption(dialog, "Task sequence", "Install Linux");
    expect(await within(dialog).findByText(refusal)).toBeInTheDocument();

    await chooseOption(dialog, "Task sequence", "Install Windows");
    expect(within(dialog).queryByText(refusal)).not.toBeInTheDocument();
  });

  it("is not said in the assign dialog of a server that has no Linux boot environment", async () => {
    const machine = machineSummary(untrusting);
    await openMachines(
      [machine],
      assigning({
        "GET /api/sequences": {
          body: [installWindows, { ...linux, rawImageBootCapability: "SecureBootOk" }],
        },
      }),
    );

    const dialog = await openAssign();
    await assignable(dialog);
    await chooseOption(dialog, "Task sequence", "Install Linux");

    expect(within(dialog).queryByText(refusal)).not.toBeInTheDocument();
  });
});
