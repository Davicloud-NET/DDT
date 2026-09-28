// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { chooseOption, press } from "@/test/aria";
import { toasts } from "@/ui/toasts";

import {
  account,
  administrator,
  field,
  node,
  opened,
  saveWait,
  serve,
  tab,
  treeView,
  view,
} from "./SequenceEditorPage.fixtures";
import type { SequenceProblem } from "./sequences";

describe("SequenceEditorPage", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("takes the focus from a finding to a condition's value deep in the flow, and to a variable's field", async () => {
    const problems: SequenceProblem[] = [
      { stepId: "s2", field: "when.parts[0].value", message: "Choose one of the kinds." },
      { stepId: null, field: "variables[1].default", message: "The default is too long." },
    ];

    serve(administrator, treeView({ problems }), { step: "p" });

    await opened();
    tab("Problems");
    press(
      screen.getByRole("button", {
        name: "Choose one of the kinds. Go to Install the site printer.",
      }),
    );

    await waitFor(() => {
      expect(within(field("when.parts[0].value")).getByRole("button")).toHaveFocus();
    });
    expect(node(/^Step 2 of 'Group: Berlin office'/)).toHaveAttribute("aria-current", "true");
    expect(within(field("when.parts[0].value")).getByRole("button")).toHaveAccessibleDescription(
      /Choose one of the kinds\./,
    );

    tab("Problems");
    press(screen.getByRole("button", { name: "The default is too long. Go to its field." }));
    await waitFor(() => {
      expect(screen.getByRole("combobox", { name: "Default" })).toHaveFocus();
    });
    expect(screen.getByRole("combobox", { name: "Default" })).toHaveValue("Standard");
  });

  it("shows a step's conditions of versions 1 and 2, and makes them its when at the first change", async () => {
    const { saves } = serve(administrator, view(), { step: "s" });

    await opened();
    const value = screen.getByRole("textbox", { name: "Value of condition 1" });
    expect(value).toHaveValue("Latitude 7440");
    expect(screen.getByText("Runs only where Model equals Latitude 7440.")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "Latitude 7450" } });

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({
        conditions: [],
        when: {
          kind: "all",
          parts: [{ kind: "test", variable: "Model", operator: "Equals", value: "Latitude 7450" }],
        },
      });
    }, saveWait);
  });

  it("completes a name in a template and fills it in for a sample machine", async () => {
    const { saves } = serve(administrator, treeView(), { step: "sv" });

    await opened();
    const value = screen.getByRole("combobox", { name: "Value" });
    expect(value).toHaveValue("PC-{{SerialNumber|alnum|right:8}}");
    expect(screen.getByText("PC-PF4K2Z7Q")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "WS-{{Seri" } });
    const list = screen.getByRole("listbox", { name: "Values to use" });
    expect(within(list).getAllByRole("option")[0]).toHaveTextContent("SerialNumber");
    fireEvent.keyDown(value, { key: "Enter" });

    expect(value).toHaveValue("WS-{{SerialNumber}}");
    expect(screen.queryByRole("listbox", { name: "Values to use" })).not.toBeInTheDocument();
    expect(screen.getByText("WS-PF4K2Z7Q")).toBeInTheDocument();

    fireEvent.change(value, { target: { value: "WS-{{Sreial}}" } });
    expect(
      screen.getByText(
        "{{Sreial}} uses Sreial, which is not a machine fact or a declared value. Check the spelling.",
      ),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(saves.at(-1)?.definition.steps[2]).toMatchObject({ value: "WS-{{Sreial}}" });
    }, saveWait);
  });

  it("runs a Windows script as a stored account, chosen from the server's accounts", async () => {
    const { saves } = serve(administrator, treeView(), { step: "s3" });

    await opened();
    const runAs = field("runAs");
    await waitFor(() => {
      expect(within(runAs).getByRole("button")).toHaveTextContent("SYSTEM");
    });
    press(within(runAs).getByRole("button"));
    const options = await screen.findAllByRole("option");
    expect(options.map((option) => option.textContent)).toEqual([
      "SYSTEM",
      "Deploy (CORP\\ddt-deploy)corp.example",
    ]);
    press(options[1] ?? document.body);

    await waitFor(() => {
      const repeat = saves.at(-1)?.definition.steps[6];

      expect(repeat?.kind === "repeat" ? repeat.steps[0] : null).toMatchObject({
        runAs: { accountId: account.id, input: null },
      });
    }, saveWait);
  });

  // Only an input asked at the machine can hold a run at its start: one only the web asks has to be answered there.
  it("says what a required input without an answer does, by where it is asked", async () => {
    serve(administrator, treeView(), { step: "p" });

    await opened();
    tab("Variables");
    press(screen.getByRole("button", { name: /^DeployShare/ }));

    const required = screen.getByRole("checkbox", { name: "An answer is required" });
    expect(required).toHaveAccessibleDescription(
      "Without one, and without a default, the run waits at its start until it is answered at the machine or on the machine's page.",
    );

    await chooseOption(document.body, "Asked", "On the web, when the run is assigned or approved");

    expect(
      screen.getByRole("checkbox", { name: "An answer is required" }),
    ).toHaveAccessibleDescription(
      "Without one, and without a default, it has to be answered when the sequence is assigned or approved on the web, and a run chosen at the machine or started by a rule fails at its start.",
    );
  });

  it("renames a variable everywhere it is used", async () => {
    const { saves } = serve(administrator, treeView(), { step: "p" });

    await opened();
    tab("Variables");
    const row = screen.getByRole("button", { name: /^ComputerName/ });
    expect(row).toHaveTextContent("Used by 2 nodes");
    press(row);

    fireEvent.change(screen.getByRole("textbox", { name: "Name" }), {
      target: { value: "PcName" },
    });
    press(screen.getByRole("button", { name: "Rename everywhere" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.variables?.[0]?.name).toBe("PcName");
    }, saveWait);
    const steps = saves.at(-1)?.definition.steps ?? [];
    expect(steps[2]).toMatchObject({ variable: "PcName" });
    expect(steps[7]).toMatchObject({ message: "Check the asset tag of {{PcName}}." });
  });
});
