// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { act, screen, waitFor, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import { applicationView, bundleView } from "@/test/applicationBuilders";
import { chooseOption, press, selectKey } from "@/test/aria";
import { flowDefinition } from "@/test/flowSequence";
import { toasts } from "@/ui/toasts";

import {
  administrator,
  field,
  opened,
  saveWait,
  serve,
  tab,
  treeView,
} from "./SequenceEditorPage.fixtures";
import type { InputDeclaration, SequenceProblem } from "./sequences";

const zip = applicationView();
const office = bundleView();
const gone = "0197a3c0-0000-7000-8000-0000000000ff";
const library = { applications: [zip], bundles: [office] };

function input(overrides: Partial<InputDeclaration> = {}): InputDeclaration {
  return {
    name: "Apps",
    label: "Applications",
    help: null,
    kind: "Text",
    choices: [],
    default: "7-Zip",
    required: false,
    maxLength: 40,
    askAt: "Both",
    account: null,
    ...overrides,
  };
}

function withInput(declared: InputDeclaration) {
  return treeView({
    definition: {
      ...flowDefinition,
      version: 6,
      inputs: [...(flowDefinition.inputs ?? []), declared],
    },
  });
}

async function openInput() {
  await opened();
  tab("Variables");
  press(screen.getByRole("button", { name: /^Apps/ }));
}

async function add(name: RegExp) {
  press(selectKey(document.body, "Add an application or bundle"));
  const listbox = await screen.findByRole("listbox");

  press(within(listbox).getByRole("option", { name }));
  await waitFor(() => {
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });
}

describe("an input that asks for applications", () => {
  afterEach(() => {
    act(() => {
      toasts.clear();
    });
    vi.unstubAllGlobals();
  });

  it("offers bundles and applications from the library in order, some ticked at the start", async () => {
    const { saves } = serve(administrator, withInput(input()), { step: "p", library });

    await openInput();
    await chooseOption(document.body, "Answer", "Applications from the library");

    expect(screen.queryByRole("textbox", { name: "Default" })).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox", { name: "Default" })).not.toBeInTheDocument();
    expect(screen.queryByText("Choices")).not.toBeInTheDocument();

    await add(/^Office basics/);
    await add(/^7-Zip/);
    const offered = field("inputs[1].offered");
    expect(within(offered).getByText("Bundle")).toBeInTheDocument();

    press(within(offered).getByRole("checkbox", { name: "7-Zip starts ticked" }));
    press(within(offered).getByRole("button", { name: "Move 7-Zip up" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.inputs?.[1]).toMatchObject({
        kind: "Applications",
        choices: [],
        default: null,
        maxLength: null,
        offered: [
          { id: zip.id, kind: "Application" },
          { id: office.id, kind: "Bundle" },
        ],
        ticked: [zip.id],
      });
    }, saveWait);
  });

  it("drops the tick of an entry that is removed", async () => {
    const declared = input({
      kind: "Applications",
      default: null,
      maxLength: null,
      offered: [
        { id: zip.id, kind: "Application" },
        { id: office.id, kind: "Bundle" },
      ],
      ticked: [zip.id, office.id],
    });
    const { saves } = serve(administrator, withInput(declared), { step: "p", library });

    await openInput();
    press(within(field("inputs[1].offered")).getByRole("button", { name: "Remove 7-Zip" }));

    await waitFor(() => {
      expect(saves.at(-1)?.definition.inputs?.[1]).toMatchObject({
        offered: [{ id: office.id, kind: "Bundle" }],
        ticked: [office.id],
      });
    }, saveWait);
  });

  it("marks an entry no longer in the library and shows the server's problems at its fields", async () => {
    const problems: SequenceProblem[] = [
      { stepId: null, field: "inputs[1].offered[0]", message: "It is not in the library." },
      { stepId: null, field: "inputs[1].offered", message: "Offer at most 50." },
      { stepId: null, field: "inputs[1].ticked[0]", message: "Tick only what it offers." },
    ];
    const declared = input({
      kind: "Applications",
      default: null,
      maxLength: null,
      offered: [{ id: gone, kind: "Application" }],
      ticked: [gone],
    });
    serve(administrator, { ...withInput(declared), problems }, { step: "p", library });

    await openInput();
    const offered = field("inputs[1].offered");

    expect(within(offered).getByText("Not in the library")).toBeInTheDocument();
    expect(within(offered).getByText("It is not in the library.")).toBeInTheDocument();
    expect(within(offered).getByText("Offer at most 50.")).toBeInTheDocument();
    expect(
      within(field("inputs[1].ticked")).getByText("Tick only what it offers."),
    ).toBeInTheDocument();
  });

  it("forgets what it offered when it asks for text again", async () => {
    const declared = input({
      kind: "Applications",
      default: null,
      maxLength: null,
      offered: [{ id: zip.id, kind: "Application" }],
      ticked: [zip.id],
    });
    const { saves } = serve(administrator, withInput(declared), { step: "p", library });

    await openInput();
    await chooseOption(document.body, "Answer", "Text");

    await waitFor(() => {
      expect(saves.at(-1)?.definition.inputs?.[1]).toMatchObject({
        kind: "Text",
        offered: null,
        ticked: null,
      });
    }, saveWait);
    expect(screen.queryByText("Offered")).not.toBeInTheDocument();
  });
});
