// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { describe, expect, it } from "vitest";

import { branch, draftOfSteps, group, leaf, repeat } from "@/test/trees";

import type { SequenceDraft } from "../sequenceDraft";
import { sequenceEdits, type SequenceEdit } from "../sequenceEdits";
import type {
  ConditionNode,
  InputDeclaration,
  SequenceStep,
  VariableDeclaration,
} from "../sequences";
import { newStep } from "../steps";
import { insertCopies, insertNode, withNewIds, wrapIn } from "./flowEdits";
import { findNode, walk } from "./flowTree";

function apply(start: SequenceDraft, ...edits: SequenceEdit[]): SequenceDraft {
  return edits.reduce(sequenceEdits, start);
}

// The tree as text: ids, with a container's bodies in brackets, Then and Else split by a bar.
function shape(steps: readonly SequenceStep[]): string {
  return steps
    .map((node) => {
      switch (node.kind) {
        case "group":
        case "repeat":
          return `${node.id}[${shape(node.steps)}]`;
        case "if":
          return `${node.id}[${shape(node.then)}|${shape(node.else)}]`;
        default:
          return node.id;
      }
    })
    .join(" ");
}

// a, IF b (then c d; else e), group f (g), h
const start = draftOfSteps(
  leaf("a"),
  branch("b", [leaf("c"), leaf("d")], [leaf("e")]),
  group("f", leaf("g")),
  leaf("h"),
);

describe("inserting", () => {
  it("puts nodes at any slot, an empty body's included, with the kind's defaults", () => {
    const empty = draftOfSteps(branch("b", [leaf("c")]), repeat("r"));
    const result = apply(
      empty,
      { type: "insertNodes", slot: { parent: "b", body: "else", index: 0 }, nodes: [leaf("x")] },
      { type: "insertNodes", slot: { parent: "r", body: "steps", index: 0 }, nodes: [leaf("y")] },
      { type: "insertNodes", slot: { parent: "b", body: "then", index: 0 }, nodes: [leaf("z")] },
      { type: "insertNodes", slot: { parent: null, body: "steps", index: 2 }, nodes: [leaf("w")] },
    );

    expect(shape(result.steps)).toBe("b[z c|x] r[y] w");
  });

  it("gives new nodes ids of their own, inside copies too", () => {
    const edit = insertNode({ parent: null, body: "steps", index: 0 }, "pause");
    const node = edit.nodes[0];

    expect(node).toMatchObject({ kind: "pause", message: "", continueAfterMinutes: null });

    const copies = insertCopies({ parent: null, body: "steps", index: 0 }, [group("f", leaf("g"))]);
    const ids = walk(copies.nodes).map((entry) => entry.node.id);

    expect(ids).toHaveLength(2);
    expect(ids).not.toContain("f");
    expect(ids).not.toContain("g");
    expect(new Set(walk(withNewIds([leaf("a"), leaf("a")])).map((e) => e.node.id)).size).toBe(2);
  });

  it("adds nothing whose id is there already, or where there is no such list", () => {
    const slot = { parent: null, body: "steps" as const, index: 0 };

    expect(apply(start, { type: "insertNodes", slot, nodes: [leaf("g")] })).toBe(start);
    expect(apply(start, { type: "insertNodes", slot, nodes: [group("x", leaf("c"))] })).toBe(start);
    expect(apply(start, { type: "insertNodes", slot, nodes: [leaf("x"), leaf("x")] })).toBe(start);
    expect(apply(start, { type: "insertNodes", slot, nodes: [] })).toBe(start);
    expect(
      apply(start, {
        type: "insertNodes",
        slot: { parent: "a", body: "steps", index: 0 },
        nodes: [leaf("x")],
      }),
    ).toBe(start);
    expect(
      apply(start, {
        type: "insertNodes",
        slot: { parent: "f", body: "then", index: 0 },
        nodes: [leaf("x")],
      }),
    ).toBe(start);
  });

  it("keeps an index past the end at the end", () => {
    const result = apply(start, {
      type: "insertNodes",
      slot: { parent: "f", body: "steps", index: 9 },
      nodes: [leaf("x")],
    });

    expect(shape(result.steps)).toBe("a b[c d|e] f[g x] h");
  });
});

describe("moving", () => {
  it("moves siblings next to each other to a slot in another list", () => {
    const result = apply(start, {
      type: "moveNodes",
      ids: ["d", "c"],
      slot: { parent: "f", body: "steps", index: 1 },
    });

    expect(shape(result.steps)).toBe("a b[|e] f[g c d] h");
  });

  it("moves within a list, counting the slot before the move", () => {
    const top = { parent: null, body: "steps" as const };

    expect(
      shape(apply(start, { type: "moveNodes", ids: ["a"], slot: { ...top, index: 3 } }).steps),
    ).toBe("b[c d|e] f[g] a h");
    expect(
      shape(apply(start, { type: "moveNodes", ids: ["h"], slot: { ...top, index: 0 } }).steps),
    ).toBe("h a b[c d|e] f[g]");
    expect(
      shape(apply(start, { type: "moveNodes", ids: ["f", "h"], slot: { ...top, index: 1 } }).steps),
    ).toBe("a f[g] h b[c d|e]");
  });

  it("leaves a move to where the nodes are as it is", () => {
    const top = { parent: null, body: "steps" as const };

    for (const index of [1, 2]) {
      expect(apply(start, { type: "moveNodes", ids: ["b"], slot: { ...top, index } })).toBe(start);
    }
  });

  it("refuses a move into the nodes moved, and of nodes that are not siblings next to each other", () => {
    const refused: SequenceEdit[] = [
      { type: "moveNodes", ids: ["b"], slot: { parent: "b", body: "else", index: 0 } },
      { type: "moveNodes", ids: ["a", "b"], slot: { parent: "b", body: "then", index: 1 } },
      { type: "moveNodes", ids: ["a", "f"], slot: { parent: null, body: "steps", index: 4 } },
      { type: "moveNodes", ids: ["c", "e"], slot: { parent: null, body: "steps", index: 0 } },
      { type: "moveNodes", ids: ["a", "g"], slot: { parent: null, body: "steps", index: 4 } },
      { type: "moveNodes", ids: ["a", "a"], slot: { parent: null, body: "steps", index: 4 } },
      { type: "moveNodes", ids: [], slot: { parent: null, body: "steps", index: 4 } },
      { type: "moveNodes", ids: ["x"], slot: { parent: null, body: "steps", index: 4 } },
      { type: "moveNodes", ids: ["a"], slot: { parent: "x", body: "steps", index: 0 } },
    ];

    for (const edit of refused) {
      expect(apply(start, edit)).toBe(start);
    }

    const nested = draftOfSteps(group("g", group("h", leaf("i"))));

    expect(
      apply(nested, {
        type: "moveNodes",
        ids: ["g"],
        slot: { parent: "h", body: "steps", index: 0 },
      }),
    ).toBe(nested);
  });
});

describe("removing, wrapping and unwrapping", () => {
  it("removes nodes anywhere, with everything inside them", () => {
    const result = apply(start, { type: "removeNodes", ids: ["d", "f"] });

    expect(shape(result.steps)).toBe("a b[c|e] h");
    expect(apply(start, { type: "removeNodes", ids: ["x"] })).toBe(start);
  });

  it("wraps siblings in a group, a repeat, or the Then of an IF, where the first of them was", () => {
    const grouped = apply(start, { ...wrapIn(["h", "f"], "group"), container: group("w") });
    const repeated = apply(start, { ...wrapIn(["c"], "repeat"), container: repeat("w") });
    const branched = apply(start, { ...wrapIn(["a"], "if"), container: branch("w", []) });

    expect(shape(grouped.steps)).toBe("a b[c d|e] w[f[g] h]");
    expect(shape(repeated.steps)).toBe("a b[w[c] d|e] f[g] h");
    expect(shape(branched.steps)).toBe("w[a|] b[c d|e] f[g] h");
    expect(findNode(branched.steps, "w")).toMatchObject({ name: "If w", else: [] });
  });

  it("wraps only siblings next to each other, in a container whose id is new", () => {
    expect(apply(start, { type: "wrapNodes", ids: ["a", "f"], container: group("w") })).toBe(start);
    expect(apply(start, { type: "wrapNodes", ids: ["a"], container: group("g") })).toBe(start);
    expect(apply(start, { type: "wrapNodes", ids: ["a"], container: group("w", leaf("c")) })).toBe(
      start,
    );
  });

  it("unwraps a container into its place, keeping the IF's branch asked for", () => {
    expect(shape(apply(start, { type: "unwrapNode", id: "f" }).steps)).toBe("a b[c d|e] g h");
    expect(shape(apply(start, { type: "unwrapNode", id: "b" }).steps)).toBe("a c d e f[g] h");
    expect(shape(apply(start, { type: "unwrapNode", id: "b", keep: "then" }).steps)).toBe(
      "a c d f[g] h",
    );
    expect(shape(apply(start, { type: "unwrapNode", id: "b", keep: "else" }).steps)).toBe(
      "a e f[g] h",
    );
    expect(apply(start, { type: "unwrapNode", id: "a" })).toBe(start);
  });
});

describe("updateNode", () => {
  it("changes a node anywhere, only in fields its kind has, never its bodies", () => {
    const result = apply(
      start,
      { type: "updateNode", id: "g", patch: { script: "exit 3" } },
      { type: "updateNode", id: "b", patch: { name: "If a laptop", continueOnError: true } },
      { type: "updateNode", id: "g", patch: { imageId: "not a script field" } },
    );
    const patched = findNode(result.steps, "g");

    expect(patched).toMatchObject({ script: "exit 3" });
    expect(patched).not.toHaveProperty("imageId");
    expect(findNode(result.steps, "b")).toMatchObject({
      name: "If a laptop",
      continueOnError: true,
    });

    const bodies = { then: [], steps: [] } as unknown as Record<string, never>;
    expect(apply(start, { type: "updateNode", id: "b", patch: bodies })).toBe(start);
  });

  it("gives a node the members of version 3, and takes them away with null", () => {
    const when: ConditionNode = {
      kind: "test",
      variable: "Model",
      operator: "Matches",
      value: "L*",
    };
    const runAs = { accountId: null, input: "Installer" };
    const given = apply(
      start,
      { type: "updateNode", id: "a", patch: { when, runAs } },
      { type: "updateNode", id: "h", patch: { account: runAs } },
    );

    expect(findNode(given.steps, "a")).toMatchObject({ when, runAs });
    expect(findNode(given.steps, "h")).not.toHaveProperty("account");

    const taken = apply(given, { type: "updateNode", id: "a", patch: { when: null, runAs: null } });

    expect(findNode(taken.steps, "a")).not.toHaveProperty("when");
    expect(findNode(taken.steps, "a")).not.toHaveProperty("runAs");
    expect(taken.steps[0]).toEqual(start.steps[0]);
  });
});

describe("editCondition", () => {
  const model = (value: string): ConditionNode => ({
    kind: "test",
    variable: "Model",
    operator: "Contains",
    value,
  });
  const when = (draft: SequenceDraft, id = "a") => findNode(draft.steps, id)?.when;

  it("builds a when from nothing, and adds, changes and removes its parts by path", () => {
    const result = apply(
      start,
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [],
        change: { op: "add", part: model("A") },
      },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [],
        change: { op: "add", part: model("B") },
      },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [1],
        change: { op: "wrap", kind: "any" },
      },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [1],
        change: { op: "add", part: model("C"), index: 0 },
      },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [1, 1],
        change: { op: "update", patch: { value: "Latitude", operator: "StartsWith" } },
      },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [],
        change: { op: "group", kind: "none" },
      },
    );

    expect(when(result)).toEqual({
      kind: "none",
      parts: [
        model("A"),
        {
          kind: "any",
          parts: [
            model("C"),
            { kind: "test", variable: "Model", operator: "StartsWith", value: "Latitude" },
          ],
        },
      ],
    });

    const removed = apply(result, {
      type: "editCondition",
      id: "a",
      field: "when",
      path: [1, 0],
      change: { op: "remove" },
    });

    expect(when(removed)).toMatchObject({
      parts: [model("A"), { kind: "any", parts: [{ value: "Latitude" }] }],
    });
    expect(
      findNode(
        apply(removed, {
          type: "editCondition",
          id: "a",
          field: "when",
          path: [],
          change: { op: "remove" },
        }).steps,
        "a",
      ),
    ).not.toHaveProperty("when");
  });

  it("makes an all of a test that gets a part added, and edits an IF's test and a repeat's until", () => {
    const tree = draftOfSteps(branch("b", []), repeat("r"));
    const result = apply(
      tree,
      {
        type: "editCondition",
        id: "b",
        field: "test",
        path: [],
        change: { op: "add", part: model("X") },
      },
      {
        type: "editCondition",
        id: "r",
        field: "until",
        path: [],
        change: { op: "set", node: null },
      },
    );
    const test = findNode(result.steps, "b");

    expect(test?.kind === "if" ? test.test : null).toEqual({
      kind: "all",
      parts: [{ kind: "test", variable: "Model", operator: "Contains", value: "" }, model("X")],
    });
    expect(findNode(result.steps, "r")).toMatchObject({ until: { kind: "all", parts: [] } });
  });

  it("leaves a change that does not fit the tree or the node as it is", () => {
    const refused: SequenceEdit[] = [
      { type: "editCondition", id: "a", field: "test", path: [], change: { op: "remove" } },
      { type: "editCondition", id: "a", field: "when", path: [0], change: { op: "remove" } },
      {
        type: "editCondition",
        id: "a",
        field: "when",
        path: [],
        change: { op: "update", patch: { value: "x" } },
      },
      {
        type: "editCondition",
        id: "b",
        field: "test",
        path: [],
        change: { op: "group", kind: "any" },
      },
      { type: "editCondition", id: "b", field: "test", path: [3], change: { op: "remove" } },
      { type: "editCondition", id: "x", field: "when", path: [], change: { op: "remove" } },
    ];

    for (const edit of refused) {
      expect(apply(start, edit)).toBe(start);
    }
  });
});

describe("variables and inputs", () => {
  const office: VariableDeclaration = {
    name: "Office",
    default: "Standard",
    description: null,
    setBySteps: true,
  };
  const owner: InputDeclaration = {
    name: "Owner",
    label: "Owner",
    help: null,
    kind: "Text",
    choices: [],
    default: null,
    required: true,
    maxLength: null,
    askAt: "Web",
    account: null,
  };
  const name = (other: VariableDeclaration, value: string) => ({ ...other, name: value });

  it("adds, changes, moves and removes variables by name, never two of one name", () => {
    const result = apply(
      start,
      { type: "addVariable", variable: office },
      { type: "addVariable", variable: name(office, "Site") },
      { type: "addVariable", variable: name(office, "OFFICE") },
      { type: "addVariable", variable: name(office, "First"), index: 0 },
      { type: "updateVariable", name: "office", patch: { description: "The edition." } },
      { type: "moveVariable", name: "Site", to: 0 },
      { type: "removeVariable", name: "First" },
    );

    expect(result.variables.map((variable) => variable.name)).toEqual(["Site", "Office"]);
    expect(result.variables[1]).toEqual({ ...office, description: "The edition." });
    expect(apply(result, { type: "removeVariable", name: "x" })).toBe(result);
    expect(apply(result, { type: "moveVariable", name: "Site", to: 0 })).toBe(result);
  });

  it("adds, changes, moves and removes inputs by name", () => {
    const result = apply(
      start,
      { type: "addInput", input: owner },
      { type: "addInput", input: { ...owner, name: "Room" } },
      { type: "addInput", input: owner },
      {
        type: "updateInput",
        name: "Owner",
        patch: { label: "Who gets it", name: "Nope" } as never,
      },
      { type: "moveInput", name: "Room", to: 0 },
    );

    expect(result.inputs.map((input) => [input.name, input.label])).toEqual([
      ["Room", "Owner"],
      ["Owner", "Who gets it"],
    ]);
    expect(apply(result, { type: "removeInput", name: "room" }).inputs).toHaveLength(1);
  });

  it("renames a variable, its input and every place that names them", () => {
    const setter = {
      ...newStep("setVariable", "s"),
      variable: "office",
      value: "{{Office|upper}}",
    };
    const join = {
      ...newStep("joinDomain", "j"),
      organizationalUnit: "OU={{ Office }},DC=corp",
      account: { accountId: null, input: "Office" },
    } as SequenceStep;
    const draft: SequenceDraft = {
      ...draftOfSteps(
        {
          ...leaf("a"),
          when: { kind: "test", variable: "Office", operator: "Equals", value: "Office" },
        },
        setter,
        join,
      ),
      variables: [office, { ...office, name: "ComputerName", default: "PC-{{office}}" }],
      inputs: [{ ...owner, name: "Office" }],
    };
    const result = apply(draft, { type: "renameVariable", from: "Office", to: "Edition" });

    expect(result.variables.map((variable) => variable.name)).toEqual(["Edition", "ComputerName"]);
    expect(result.variables[1]?.default).toBe("PC-{{Edition}}");
    expect(result.inputs[0]?.name).toBe("Edition");
    expect(findNode(result.steps, "a")?.when).toEqual({
      kind: "test",
      variable: "Edition",
      operator: "Equals",
      value: "Office",
    });
    expect(findNode(result.steps, "s")).toMatchObject({
      variable: "Edition",
      value: "{{Edition|upper}}",
    });
    expect(findNode(result.steps, "j")).toMatchObject({
      organizationalUnit: "OU={{ Edition }},DC=corp",
      account: { accountId: null, input: "Edition" },
    });
  });

  it("renames only to a free, valid name, and only what is declared", () => {
    const draft = { ...draftOfSteps(), variables: [office, name(office, "Site")] };
    const refused: SequenceEdit[] = [
      { type: "renameVariable", from: "Office", to: "site" },
      { type: "renameVariable", from: "Office", to: "" },
      { type: "renameVariable", from: "Office", to: "2nd" },
      { type: "renameVariable", from: "Office", to: "Office" },
      { type: "renameVariable", from: "Missing", to: "Other" },
    ];

    for (const edit of refused) {
      expect(apply(draft, edit)).toBe(draft);
    }

    expect(
      apply(draft, { type: "renameVariable", from: "Office", to: "OFFICE" }).variables[0]?.name,
    ).toBe("OFFICE");
  });
});
