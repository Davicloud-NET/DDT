// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import {
  IconArrowBackUp,
  IconArrowForwardUp,
  IconAdjustmentsHorizontal,
} from "@tabler/icons-react";
import { Link, useBlocker, useNavigate, useSearch } from "@tanstack/react-router";
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
import { Button as AriaButton } from "react-aria-components";

import type { AutosaveState } from "@/lib/autosave";
import { useMediaQuery } from "@/lib/useMediaQuery";
import { buttonClass } from "@/ui/buttonClass";
import { FilterSelector } from "@/ui/Controls";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { Panel } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { showToast } from "@/ui/toasts";
import { Tooltip } from "@/ui/Tooltip";

import { focusField } from "../editorFocus";
import { EditorLock } from "../editorLock";
import { TextSetting } from "../fields";
import { insertCopies, insertNode, wrapIn } from "../flow/flowEdits";
import {
  afterRemoval,
  clipboardText,
  nodesFromClipboard,
  nodeTitle,
  placeLabel,
  shiftEdit,
  slotAfter,
  type FlowCommand,
} from "../flow/flowKeyboard";
import { layoutFlow } from "../flow/flowLayout";
import { bodiesOf, indexTree, type Slot } from "../flow/flowTree";
import { declarationPlace, sequenceFindings, stepFindings, type Findings } from "../problems";
import { SaveState } from "../SaveState";
import { SequenceConflict } from "../SequenceConflict";
import type { SequenceEdit } from "../sequenceEdits";
import type { ContainerKind, SequenceStep, SequenceView, StepKind } from "../sequences";
import { useSequenceEditor } from "../useSequenceEditor";
import { AddNodeMenu } from "./AddNodeMenu";
import { addLabel } from "./nodeKinds";
import { BuilderContext, useBuilderData } from "./builderData";
import { FlowCanvas, type FlowDrag, type FlowReveal, type NodeAction } from "./FlowCanvas";
import { FlowOutline } from "./FlowOutline";
import { Inspector, type InspectorTab } from "./Inspector";
import { nodeDetail } from "./nodeDetail";
import { NodeInspector } from "./NodeInspector";
import { Palette } from "./Palette";
import { ProblemsPanel } from "./ProblemsPanel";
import { VariablesPanel, type OpenRow } from "./VariablesPanel";

// How long a removed node can be brought back from its toast; Ctrl+Z brings it back for longer.
const UNDO_MS = 10_000;

// The server's refusal of the name or the description, such as a name another sequence has.
function refused(state: AutosaveState, field: string): Findings {
  const messages = state.kind === "refused" ? (state.problem?.errors?.[field] ?? []) : [];

  return { problems: messages.map((message) => ({ stepId: null, field, message })), warnings: [] };
}

function leaveConsequence(state: AutosaveState): string {
  switch (state.kind) {
    case "conflict":
      return t`Someone else saved this sequence, so your changes since your last save are not saved. Leaving throws them away.`;
    case "retrying": {
      const message = state.message;

      return t`${message} Your latest changes are not saved, and leaving throws them away.`;
    }
    case "refused":
    case "stopped": {
      const message = state.message;

      return t`The server did not save your latest changes: ${message} Leaving throws them away.`;
    }
    default:
      return t`Your latest changes are not saved yet. Leaving throws them away.`;
  }
}

// The containers shown closed in this browser, for each sequence.
function useCollapsed(sequenceId: string): [ReadonlySet<string>, (id: string) => void] {
  const key = `ddt.flow.collapsed.${sequenceId}`;
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(() => {
    try {
      const stored: unknown = JSON.parse(window.localStorage.getItem(key) ?? "[]");

      return new Set(Array.isArray(stored) ? stored.filter((id) => typeof id === "string") : []);
    } catch {
      return new Set();
    }
  });

  const toggle = (id: string) => {
    setCollapsed((current) => {
      const next = new Set(current);

      if (!next.delete(id)) {
        next.add(id);
      }

      try {
        window.localStorage.setItem(key, JSON.stringify([...next]));
      } catch {
        // Kept for this page only.
      }

      return next;
    });
  };

  return [collapsed, toggle];
}

// The system clipboard where the browser lets the page use it; the builder keeps its own copy beside it.
function systemClipboard(): Clipboard | undefined {
  return (navigator as { clipboard?: Clipboard }).clipboard;
}

// Edits a sequence as a flow: the palette on the left, the flow in the middle, and the inspector on the right. Every
// change is saved as it is made, and other administrators' saves appear while this page has nothing unsaved. On a
// phone the flow is its outline, and the inspector opens over it.
export function FlowBuilder({ initial, readOnly }: { initial: SequenceView; readOnly: boolean }) {
  const { t: translate } = useLingui();
  const editor = useSequenceEditor(initial, readOnly);
  const search = useSearch({ from: "/shell/deployment/sequences/$sequenceId" });
  const navigate = useNavigate({ from: "/deployment/sequences/$sequenceId" });
  const phone = useMediaQuery("(max-width: 767px)");
  const { draft, state, locked } = editor;
  const data = useBuilderData(draft);
  const index = useMemo(() => indexTree(draft.steps), [draft.steps]);
  const [collapsed, toggleCollapsed] = useCollapsed(initial.id);
  const layout = useMemo(() => layoutFlow(draft.steps, { collapsed }), [draft.steps, collapsed]);
  // The node shown. The address holds it too, so a reload shows the same node, but it is read only when the page
  // opens: a new address arrives a moment after the edit that chose the node, such as adding it.
  const [selectedId, setSelectedId] = useState<string | null>(search.step ?? null);
  const [tab, setTab] = useState<InspectorTab>("node");
  const [view, setView] = useState<"flow" | "outline">("flow");
  const [drag, setDrag] = useState<FlowDrag>(null);
  const [reveal, setReveal] = useState<FlowReveal | null>(null);
  const [addAfter, setAddAfter] = useState<string | null>(null);
  const [openRow, setOpenRow] = useState<OpenRow | null>(null);
  const [drawer, setDrawer] = useState(false);
  const [announcement, setAnnouncement] = useState("");
  const inspector = useRef<HTMLDivElement>(null);
  // Where a finding sends the focus, once its field shows, and how many renders it has waited.
  const pendingFocus = useRef<{ field: string; waited: number } | null>(null);
  // What Ctrl+C copied, for a browser that keeps the system clipboard from the page.
  const clipboard = useRef<SequenceStep[] | null>(null);
  const requests = useRef(0);

  const selected = selectedId === null ? undefined : index.byId.get(selectedId);
  const problemCount = editor.findings.problems.length;
  const warningCount = editor.findings.warnings.length;
  const name = draft.name.trim() === "" ? translate`Unnamed sequence` : draft.name;

  // Leaving saves first and goes once that worked; it asks only when the changes cannot be saved. Choosing another
  // node stays on the page, and signing out ends the session a save needs, so neither is held up.
  const leaving = useBlocker({
    shouldBlockFn: async ({ current, next }) =>
      next.pathname !== current.pathname && next.routeId !== "/sign-in" && !(await editor.flush()),
    enableBeforeUnload: false,
    disabled: !editor.dirty,
    withResolver: true,
  });

  // Another node's fields start at their top. Before the focus a finding sends, which scrolls to its field.
  useEffect(() => {
    const panel = inspector.current?.querySelector<HTMLElement>('[role="tabpanel"]');

    if (panel !== null && panel !== undefined) {
      panel.scrollTop = 0;
    }
  }, [selectedId]);

  useEffect(() => {
    const pending = pendingFocus.current;

    if (pending === null) {
      return;
    }

    if (pending.field !== "" && focusField(inspector.current, pending.field)) {
      pendingFocus.current = null;
      return;
    }

    const list = inspector.current?.querySelector<HTMLElement>("[data-findings]");

    if (list !== null && list !== undefined && tab === "node") {
      list.focus();
      pendingFocus.current = null;
    } else if (pending.waited > 2) {
      pendingFocus.current = null;
    } else {
      pending.waited++;
    }
  });

  // The node the address names shows in the flow when the page opens.
  useEffect(() => {
    if (selectedId !== null && index.byId.has(selectedId)) {
      requests.current++;
      setReveal({ id: selectedId, focus: false, center: true, count: requests.current });
    }
    // Once, when the page opens.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const announce = (text: string) => {
    setAnnouncement(text);
  };

  const select = useCallback(
    (id: string | null) => {
      setSelectedId(id);
      setTab("node");
      void navigate({
        search: id === null ? {} : { step: id },
        replace: true,
        resetScroll: false,
      });
    },
    [navigate],
  );

  // Chooses a node and shows it in the flow, with the focus unless a field takes it.
  const show = (id: string, { focus = true, center = false } = {}) => {
    select(id);
    requests.current++;
    setReveal({ id, focus, center, count: requests.current });
  };

  const edit = (change: SequenceEdit) => {
    editor.edit(change);
  };

  const add = (slot: Slot, kind: StepKind) => {
    const insert = insertNode(slot, kind);
    const [node] = insert.nodes;

    edit(insert);

    if (node !== undefined) {
      const label = addLabel(kind);

      show(node.id);
      announce(t`Added ${label}.`);
    }
  };

  const remove = (id: string, notify = true) => {
    const next = afterRemoval(index, id);
    const removed = editor.remove(id);

    if (removed === null) {
      return;
    }

    const title = nodeTitle(removed.node);

    if (next === null) {
      select(null);
    } else {
      show(next);
    }

    if (notify) {
      showToast(
        {
          title: t`Removed ${title}.`,
          action: {
            label: t`Undo`,
            onAction: () => {
              editor.restore(removed);
              show(removed.node.id);
            },
          },
        },
        UNDO_MS,
      );
    }
  };

  const copy = (nodes: SequenceStep[]) => {
    clipboard.current = nodes;
    void systemClipboard()
      ?.writeText(clipboardText(nodes))
      .catch(() => undefined);
  };

  const paste = async (after: string | null) => {
    let nodes = clipboard.current;

    try {
      const text = await systemClipboard()?.readText();
      const read = text === undefined ? null : nodesFromClipboard(text);

      nodes = read ?? nodes;
    } catch {
      // The browser keeps the clipboard from the page; the builder's own copy is used.
    }

    if (nodes === null) {
      announce(t`Nothing to paste. Copy a step first.`);
      return;
    }

    const insert = insertCopies(slotAfter(index, after), nodes);
    const [first] = insert.nodes;
    const count = insert.nodes.length;

    edit(insert);

    if (first !== undefined) {
      show(first.id);
    }

    announce(plural(count, { one: "Pasted # step.", other: "Pasted # steps." }));
  };

  const command = (given: FlowCommand, id: string) => {
    const entry = index.byId.get(id);

    if (entry === undefined) {
      return;
    }

    const node = entry.node;
    const title = nodeTitle(node);

    switch (given.type) {
      case "open":
        select(id);

        if (phone) {
          setDrawer(true);
        } else {
          pendingFocus.current = { field: "name", waited: 0 };
        }

        break;
      case "remove":
        remove(id);
        break;
      case "copy":
        copy([node]);
        announce(t`Copied ${title}.`);
        break;
      case "cut":
        copy([node]);
        remove(id, false);
        announce(t`Cut ${title}.`);
        break;
      case "paste":
        void paste(id);
        break;
      case "duplicate": {
        const insert = insertCopies(slotAfter(index, id), [node]);
        const [copied] = insert.nodes;

        edit(insert);

        if (copied !== undefined) {
          show(copied.id);
        }

        announce(t`Duplicated ${title}.`);
        break;
      }
      case "shift": {
        const move = shiftEdit(index, id, given.by);

        if (move !== null) {
          const position = entry.index + 1 + given.by;
          const count = index.entries.filter(
            (other) => other.parent === entry.parent && other.body === entry.body,
          ).length;

          edit(move);
          announce(t`${title} moved to position ${position} of ${count}.`);
        }

        break;
      }
      case "move":
      case "menu":
        break;
    }
  };

  const wrap = (id: string, kind: ContainerKind) => {
    const wrapping = wrapIn([id], kind);

    edit(wrapping);
    show(wrapping.container.id);
  };

  const unwrap = (id: string) => {
    const node = index.byId.get(id)?.node;
    const first = node === undefined ? undefined : bodiesOf(node).flatMap((body) => body.steps)[0];

    edit({ type: "unwrapNode", id });

    if (first !== undefined) {
      show(first.id);
    }
  };

  const action = (given: NodeAction, id: string) => {
    switch (given.type) {
      case "wrap":
        wrap(id, given.kind);
        break;
      case "unwrap":
        unwrap(id);
        break;
      case "collapse":
        toggleCollapsed(id);
        break;
      case "addAfter":
        setAddAfter(id);
        break;
      case "command":
        command(given.command, id);
        break;
    }
  };

  const move = (ids: string[], slot: Slot) => {
    edit({ type: "moveNodes", ids, slot });

    const [first] = ids;

    if (first !== undefined) {
      show(first);
    }
  };

  // A finding takes the focus to its field: a node's in the node's fields, the sequence's own in its tab.
  const goTo = (stepId: string | null, field: string | null) => {
    if (stepId !== null && index.byId.has(stepId)) {
      pendingFocus.current = { field: field ?? "", waited: 0 };
      show(stepId, { focus: false, center: true });

      if (phone) {
        setDrawer(true);
      }

      return;
    }

    const place = declarationPlace(field);

    if (place !== null && field !== null) {
      setOpenRow({ list: place.list, index: place.index });
      setTab("variables");
      pendingFocus.current = { field, waited: 0 };
    } else if (field === "name" || field === "description") {
      setTab("sequence");
      pendingFocus.current = { field, waited: 0 };
    }
  };

  // Escape in the node's fields goes back to the node in the flow.
  const inspectorKey = (event: KeyboardEvent<HTMLDivElement>) => {
    if (
      event.key === "Escape" &&
      !event.defaultPrevented &&
      selectedId !== null &&
      tab === "node" &&
      view === "flow" &&
      !phone
    ) {
      event.preventDefault();
      show(selectedId);
    }
  };

  const nodeTab =
    selected === undefined ? (
      <p className="type-small text-muted">
        {draft.steps.length === 0 ? (
          <Trans>Add a step to the flow to see its settings here.</Trans>
        ) : (
          <Trans>Choose a node in the flow to see its settings here.</Trans>
        )}
      </p>
    ) : (
      <NodeInspector
        node={selected.node}
        place={placeLabel(index, selected)}
        phases={editor.phases.get(selected.node.id) ?? ["WindowsPE"]}
        findings={stepFindings(editor.findings, selected.node.id)}
        catalog={editor.catalog}
        onEdit={edit}
        onRemove={() => {
          remove(selected.node.id);
        }}
        onWrap={() => {
          wrap(selected.node.id, "group");
        }}
        onUnwrap={() => {
          unwrap(selected.node.id);
        }}
        onShift={(by) => {
          command({ type: "shift", by }, selected.node.id);
        }}
      />
    );

  const inspectorPanel = (
    <Inspector
      tab={tab}
      onTab={setTab}
      problemCount={problemCount}
      warningCount={warningCount}
      panelRef={inspector}
      onKeyDown={inspectorKey}
      className={phone ? "-mx-5 -my-4 rounded-none bg-transparent shadow-none" : "w-92 shrink-0"}
      node={nodeTab}
      variables={
        <VariablesPanel
          draft={draft}
          findings={sequenceFindings(editor.findings, draft.steps)}
          open={openRow}
          onEdit={edit}
          onGoToNode={(id) => {
            show(id, { center: true });
          }}
        />
      }
      problems={<ProblemsPanel index={index} findings={editor.findings} onGoTo={goTo} />}
      sequence={
        <>
          <TextSetting
            label={<Trans>Sequence name</Trans>}
            field="name"
            findings={refused(state, "name")}
            value={draft.name}
            onChange={(text) => {
              edit({ type: "rename", name: text });
            }}
          />
          <TextSetting
            label={<Trans>Description</Trans>}
            field="description"
            findings={refused(state, "description")}
            hint={<Trans>Shown in the list of task sequences.</Trans>}
            multiline
            rows={3}
            value={draft.description}
            onChange={(text) => {
              edit({ type: "describe", description: text });
            }}
          />
        </>
      }
    />
  );

  const outline = (
    <FlowOutline
      label={translate`Outline of ${name}`}
      steps={draft.steps}
      index={index}
      selectedId={selectedId}
      findings={editor.findings}
      locked={locked}
      onSelect={(id) => {
        select(id);

        if (phone) {
          setDrawer(true);
        }
      }}
      onMove={move}
      className="min-h-0 flex-1"
    />
  );

  const selectedTitle = selected === undefined ? "" : nodeTitle(selected.node);
  const addToOutline = locked ? null : (
    <AddNodeMenu
      label={<Trans>Add a step</Trans>}
      fullLabel={
        selected === undefined
          ? translate`Add a step at the end`
          : translate`Add a step after ${selectedTitle}`
      }
      onAdd={(kind) => {
        add(slotAfter(index, selected?.node.id ?? null), kind);
      }}
    />
  );

  return (
    <BuilderContext value={data}>
      <EditorLock value={locked}>
        <header className="flex flex-wrap items-end justify-between gap-x-5 gap-y-3">
          <span className="flex min-w-0 flex-wrap items-center gap-x-3.5 gap-y-2">
            <h1 className="min-w-0 type-title break-words text-ink">{name}</h1>
            <StateTag tone={problemCount > 0 ? "fail" : "ok"}>
              {problemCount > 0 ? (
                plural(problemCount, { one: "# problem", other: "# problems" })
              ) : (
                <Trans>Ready to run</Trans>
              )}
            </StateTag>
          </span>
          <div className="flex flex-wrap items-center gap-3">
            {readOnly ? null : (
              <SaveState
                state={state}
                savedElsewhere={editor.savedElsewhere}
                onRetry={() => {
                  void editor.flush();
                }}
              />
            )}
            {locked ? null : (
              <div className="flex gap-1">
                <Tooltip content={<Trans>Undo, Ctrl+Z</Trans>}>
                  <AriaButton
                    aria-label={translate`Undo`}
                    aria-keyshortcuts="Control+Z"
                    isDisabled={!editor.canUndo}
                    className={buttonClass("secondary", "md", "w-9 px-0")}
                    onPress={editor.undo}
                  >
                    <IconArrowBackUp aria-hidden="true" size={18} stroke={2} />
                  </AriaButton>
                </Tooltip>
                <Tooltip content={<Trans>Redo, Ctrl+Y</Trans>}>
                  <AriaButton
                    aria-label={translate`Redo`}
                    aria-keyshortcuts="Control+Y"
                    isDisabled={!editor.canRedo}
                    className={buttonClass("secondary", "md", "w-9 px-0")}
                    onPress={editor.redo}
                  >
                    <IconArrowForwardUp aria-hidden="true" size={18} stroke={2} />
                  </AriaButton>
                </Tooltip>
              </div>
            )}
            {phone ? null : (
              <FilterSelector
                label={translate`View`}
                options={[
                  { id: "flow", label: <Trans>Flow</Trans> },
                  { id: "outline", label: <Trans>Outline</Trans> },
                ]}
                selected={view}
                onChange={(id) => {
                  setView(id === "outline" ? "outline" : "flow");
                }}
              />
            )}
          </div>
        </header>

        {readOnly ? (
          <Notice>
            <Trans>Only administrators change task sequences. You can look at this one.</Trans>
          </Notice>
        ) : null}

        {editor.deleted ? (
          <Notice tone="fail" title={<Trans>This sequence was deleted</Trans>}>
            <Trans>
              Someone deleted it while it was open here, so nothing more is saved.{" "}
              <Link to="/deployment/sequences" className="font-semibold underline">
                Go to the task sequences
              </Link>
              .
            </Trans>
          </Notice>
        ) : null}

        {editor.conflict !== null ? (
          <SequenceConflict
            theirs={editor.conflict.view}
            changes={editor.conflict.changes}
            onTakeTheirs={editor.takeTheirs}
            onKeepMine={editor.keepMine}
          />
        ) : null}

        {phone ? (
          <div className="flex flex-col gap-3">
            <div className="flex flex-wrap gap-2">
              {addToOutline}
              <AriaButton
                className={buttonClass("quiet", "sm")}
                onPress={() => {
                  setTab(selected === undefined ? "sequence" : "node");
                  setDrawer(true);
                }}
              >
                <IconAdjustmentsHorizontal aria-hidden="true" size={16} stroke={2} />
                <Trans>Details</Trans>
              </AriaButton>
            </div>
            <Panel flush>{outline}</Panel>
            <Drawer
              isOpen={drawer}
              onOpenChange={setDrawer}
              title={selected === undefined ? name : nodeTitle(selected.node)}
            >
              {inspectorPanel}
            </Drawer>
          </div>
        ) : (
          <div className="flex min-h-[36rem] flex-1 gap-4">
            {locked || view === "outline" ? null : (
              <Palette
                className="hidden w-54 shrink-0 xl:flex"
                onDragChange={setDrag}
                onAdd={(kind) => {
                  add(slotAfter(index, selected?.node.id ?? null), kind);
                }}
              />
            )}
            {view === "flow" ? (
              <FlowCanvas
                label={translate`Flow of ${name}`}
                steps={draft.steps}
                index={index}
                layout={layout}
                selectedId={selectedId}
                reveal={reveal}
                findings={editor.findings}
                phases={editor.phases}
                collapsed={collapsed}
                locked={locked}
                drag={drag}
                onDragChange={setDrag}
                detailOf={(node) =>
                  nodeDetail(node, {
                    catalog: editor.catalog,
                    subjects: data.subjects,
                    findings: stepFindings(editor.findings, node.id),
                    accounts: data.accounts,
                  })
                }
                onSelect={select}
                onCommand={command}
                onAction={action}
                onAdd={add}
                onMove={move}
                addAfter={addAfter}
                onAddAfterDone={() => {
                  setAddAfter(null);
                }}
                className="min-h-0 min-w-0 flex-1"
              />
            ) : (
              <Panel
                flush
                title={<Trans>Outline</Trans>}
                actions={addToOutline}
                className="min-h-0 min-w-0 flex-1"
              >
                {outline}
              </Panel>
            )}
            {inspectorPanel}
          </div>
        )}

        <p role="status" className="sr-only">
          {announcement}
        </p>

        <ConfirmDialog
          isOpen={leaving.status === "blocked"}
          onOpenChange={(open) => {
            if (!open) {
              leaving.reset?.();
            }
          }}
          title={<Trans>Leave without saving?</Trans>}
          confirmLabel={<Trans>Leave without saving</Trans>}
          danger
          onConfirm={() => {
            leaving.proceed?.();
          }}
        >
          <p>{leaveConsequence(state)}</p>
        </ConfirmDialog>
      </EditorLock>
    </BuilderContext>
  );
}
