// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { IconCheck, IconX } from "@tabler/icons-react";
import {
  useEffect,
  useEffectEvent,
  useMemo,
  useRef,
  useState,
  type KeyboardEvent,
  type ReactNode,
} from "react";
import { Button as AriaButton, ToggleButton } from "react-aria-components";

import { conditionSummary, type Subject } from "@/conditions/conditions";
import type { DeploymentSummary, DeploymentView } from "@/deployments/deployments";
import { formattingLocale } from "@/i18n/i18n";
import { formatDuration } from "@/lib/format";
import { flowCommand, flowTarget, nodeTitle, placeLabel } from "@/sequences/flow/flowKeyboard";
import { layoutFlow, NODE_WIDTH, type WireRoute } from "@/sequences/flow/flowLayout";
import { indexTree } from "@/sequences/flow/flowTree";
import { phaseLabel } from "@/sequences/steps";
import { cx } from "@/ui/cx";
import { FlowFrame, FlowNode, type FlowNodeState } from "@/ui/FlowNode";
import { FlowViewport, type FlowViewportHandle } from "@/ui/FlowViewport";
import { FlowDots, FlowWires, type WireTone } from "@/ui/FlowWires";
import { StateTag } from "@/ui/StateTag";

import {
  decisionOutcomes,
  decisionTitle,
  outcomesOf,
  outcomeText,
  repeatText,
  reportedText,
  runSubjects,
  testWords,
  type TestOutcome,
} from "./decisions";
import { runPath, type PathNode, type PathState } from "./runPath";
import { stepDuration } from "./runs";
import { crumbText, pathStateLabel, pathStateTone } from "./runView";

// A run on the flow of the sequence it was given: the path it took in ink, the branches it did not take dashed with
// their nodes in the muted text colour, each node with its state along its top edge. The canvas follows the node the
// run is at until the person moves it; "Follow the run" takes it back there. A node chosen with the pointer or the
// keys shows its details beside the flow: its times, why it failed, and what it decided, as the agent recorded it. It
// is loaded with the flow's code, when a run's page shows one.
export default function RunFlow({
  view,
  summary,
  now,
  onShowLog,
  children,
}: {
  view: DeploymentView;
  // The run as the machine list follows it, which is newer than the one the run was read with.
  summary: DeploymentSummary;
  now: number;
  onShowLog: (stepId: string) => void;
  // More beside the flow, under the details, such as the run's values.
  children?: ReactNode;
}) {
  const { i18n, t: translate } = useLingui();
  const definition = view.definition;
  const steps = useMemo(() => definition?.steps ?? [], [definition]);
  const layout = useMemo(() => layoutFlow(steps), [steps]);
  const index = useMemo(() => indexTree(steps), [steps]);
  const subjects = useMemo(
    () => runSubjects(definition, view.values ?? []),
    [definition, view.values],
  );
  const path = runPath(definition, view.steps, {
    activity: summary.activity,
    pause: view.pause ?? null,
  });
  const current = path.current;
  const currentId = current?.node.id ?? null;
  const active = summary.state === "Running" || summary.state === "Assigned";
  const [picked, setPicked] = useState<string | null>(null);
  const selectedId = picked ?? currentId;
  const selected = selectedId === null ? null : (path.byId.get(selectedId) ?? null);
  const [following, setFollowing] = useState(true);
  const viewport = useRef<FlowViewportHandle>(null);
  const nodes = useRef(new Map<string, HTMLElement>());
  // The canvas moves itself while it follows the run; any other move is the person's, which ends the following.
  const moving = useRef(false);

  const boxOf = (id: string) => layout.boxes.find((box) => box.id === id);

  const follow = useEffectEvent((id: string) => {
    const box = boxOf(id);

    if (box !== undefined && viewport.current !== null) {
      moving.current = true;
      viewport.current.reveal(box, true);
    }
  });

  // The node the run is at, where the run is followed; on a first look at a run that ended, the node it failed at.
  useEffect(() => {
    if (currentId !== null && (following || !active)) {
      follow(currentId);
    }
  }, [currentId, following, active]);

  const tone = (route: WireRoute): WireTone => path.tone(route);
  const tabbableId = selectedId !== null && boxOf(selectedId) !== undefined ? selectedId : null;
  const firstId = layout.boxes[0]?.id ?? null;

  const choose = (id: string, focus: boolean) => {
    setPicked(id);

    if (focus) {
      nodes.current.get(id)?.focus({ preventScroll: true });
    }
  };

  const keyDown = (event: KeyboardEvent, id: string) => {
    const command = flowCommand(event);

    if (command?.type !== "move") {
      return;
    }

    const target = flowTarget(index, id, command.move);

    if (target === null && command.move === "parent") {
      return;
    }

    event.preventDefault();

    if (target !== null) {
      choose(target, true);
    }
  };

  const hand = handover(path);
  const handBox = hand === null ? undefined : boxOf(hand);

  return (
    <div className="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_26.5rem]">
      <FlowViewport
        label={translate`Flow of this run`}
        contentWidth={Math.max(layout.width, NODE_WIDTH)}
        contentHeight={layout.height}
        tabbable={false}
        start="top"
        handle={viewport}
        className="h-[32rem] max-sm:h-[26rem]"
        onTransformChange={() => {
          if (moving.current) {
            moving.current = false;
          } else {
            setFollowing(false);
          }
        }}
        minimap={[
          ...layout.frames.map((frame) => ({ ...frame, tone: "frame" as const })),
          ...layout.boxes.map((box) => ({
            ...box,
            tone:
              path.byId.get(box.id)?.state === "notTaken" ? ("muted" as const) : ("node" as const),
          })),
        ]}
        {...(active && currentId !== null
          ? {
              controls: (
                <ToggleButton
                  isSelected={following}
                  onChange={(on) => {
                    setFollowing(on);
                  }}
                  className={cx(
                    "flex h-full cursor-pointer items-center border-l border-line-soft px-3 font-semibold text-ink motion-colors outline-none",
                    "hover:bg-hover pressed:bg-key-quiet-pressed selected:bg-selected",
                    "focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-focus",
                  )}
                >
                  <Trans>Follow the run</Trans>
                </ToggleButton>
              ),
            }
          : {})}
      >
        {layout.frames.map((frame) => (
          <FlowFrame
            key={frame.id}
            className="absolute"
            style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
          />
        ))}

        {handBox === undefined ? null : (
          <div aria-hidden="true">
            <div
              className="absolute border-t-[1.5px] border-dashed border-control"
              style={{ left: -48, width: layout.width + 96, top: handBox.y - 22 }}
            />
            <span
              className="absolute type-small whitespace-nowrap text-muted"
              style={{ left: -44, top: handBox.y - 44 }}
            >
              <Trans>In Windows PE</Trans>
            </span>
            <span
              className="absolute type-small whitespace-nowrap text-muted"
              style={{ left: -44, top: handBox.y - 16 }}
            >
              <Trans>In the installed Windows, after the hand-over</Trans>
            </span>
          </div>
        )}

        <FlowWires
          width={layout.width}
          height={layout.height}
          wires={layout.wires}
          arrows={layout.arrows}
          tone={tone}
        />

        {layout.boxes.map((box) => {
          const node = path.byId.get(box.id);
          const entry = index.byId.get(box.id);

          if (node === undefined || entry === undefined) {
            return null;
          }

          const detail = nodeDetail(node, subjects, view.variables ?? {}, now);
          const state = i18n._(pathStateLabel[node.state]);

          return (
            <div
              key={box.id}
              ref={(element) => {
                if (element === null) {
                  nodes.current.delete(box.id);
                } else {
                  nodes.current.set(box.id, element);
                }
              }}
              role="button"
              tabIndex={box.id === (tabbableId ?? firstId) ? 0 : -1}
              aria-label={`${placeLabel(index, entry)}, ${nodeTitle(node.node)}, ${state}`}
              aria-current={box.id === selectedId ? "true" : undefined}
              data-flow-node
              className="absolute cursor-pointer rounded-key outline-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
              style={{ left: box.x, top: box.y, width: box.w, height: box.h }}
              onClick={() => {
                choose(box.id, false);
              }}
              onFocus={() => {
                viewport.current?.reveal(box);
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  choose(box.id, false);
                  return;
                }

                keyDown(event, box.id);
              }}
            >
              <FlowNode
                kind={node.node.kind}
                name={node.step?.name ?? node.node.name}
                number={entry.number}
                detail={detail.text}
                code={detail.code}
                state={flowState[node.state]}
                {...(node.state === "running" ? { percent: node.step?.percent ?? 0 } : {})}
                selected={box.id === selectedId}
                branch={branchOf(node)}
                className="size-full"
              />
            </div>
          );
        })}

        <FlowDots
          width={layout.width}
          height={layout.height}
          ports={layout.ports}
          joins={layout.joins}
          selectedId={selectedId}
          tone={tone}
        />
      </FlowViewport>

      <div className="flex min-w-0 flex-col gap-4">
        {selected === null ? (
          <section
            aria-label={translate`The chosen step`}
            className="rounded-panel bg-panel p-4 type-small text-muted shadow-panel"
          >
            <Trans>Choose a step in the flow to see what it did.</Trans>
          </section>
        ) : (
          <NodeDetails
            node={selected}
            run={summary}
            subjects={subjects}
            now={now}
            onShowLog={onShowLog}
          />
        )}
        {children}
      </div>
    </div>
  );
}

const flowState: Record<PathState, FlowNodeState> = {
  waiting: "waiting",
  running: "running",
  paused: "paused",
  done: "done",
  failed: "failed",
  skipped: "skipped",
  notTaken: "notTaken",
};

function branchOf(node: PathNode): "then" | "else" | null {
  const branch = node.step?.branch ?? null;

  return node.node.kind !== "if" || node.state === "notTaken"
    ? null
    : branch === "Then"
      ? "then"
      : branch === "Else"
        ? "else"
        : null;
}

// The first node at the top that the run reached in the installed Windows after nodes in Windows PE, where the flow
// draws the line between the phases; null where the run has not handed over, or the line would not run across.
function handover(path: ReturnType<typeof runPath>): string | null {
  const top = path.nodes.filter((node) => node.entry.parent === null);
  const index = top.findIndex((node) => node.step?.phase === "Windows");

  return index > 0 && top.slice(0, index).every((node) => node.step?.phase !== "Windows")
    ? (top[index]?.node.id ?? null)
    : null;
}

// The one line a node's card shows on a run: how long it took or runs, why it was skipped, what it tested, the value
// it set.
function nodeDetail(
  node: PathNode,
  subjects: readonly Subject[],
  variables: Record<string, string>,
  now: number,
): { text: string; code: boolean } {
  const plain = (text: string) => ({ text, code: false });
  const step = node.step;
  const duration = step === null ? null : stepDuration(step, now);
  const took = duration === null ? null : formatDuration(duration);

  if (node.state === "notTaken") {
    return plain(t`Not taken`);
  }

  switch (node.node.kind) {
    case "if":
      return plain(conditionSummary(node.node.test, subjects));
    case "repeat":
      if (step !== null && (step.iteration ?? 0) > 0) {
        return plain(repeatText(node.node.maxTimes, step));
      }
      break;
    case "setVariable":
      if (node.state === "done") {
        const name = node.node.variable;
        const set =
          Object.entries(variables).find(
            ([candidate]) => candidate.toLowerCase() === name.toLowerCase(),
          )?.[1] ?? node.node.value;

        return { text: `${name} = ${set}`, code: true };
      }
      break;
    default:
      break;
  }

  switch (node.state) {
    case "waiting":
      return plain(t`Not started`);
    case "running":
      return plain(took === null ? t`Running` : t`Running for ${took}`);
    case "paused":
      return plain(took === null ? t`Paused` : t`Paused for ${took}`);
    case "failed":
      return plain(step?.error ?? t`Failed`);
    case "skipped": {
      const [reason] = outcomesOf(node.node, step, "condition").filter(
        (outcome) => !outcome.held && outcome.test !== null,
      );

      if (reason === undefined) {
        return plain(t`Skipped`);
      }

      const reasons = outcomeText(reason, subjects);

      return plain(t`Skipped: ${reasons}`);
    }
    case "done": {
      const passes = step?.pass ?? 0;

      if (took === null) {
        return plain(t`Done`);
      }

      return plain(passes > 1 ? t`${passes} passes, the last took ${took}` : t`Took ${took}`);
    }
  }
}

// The chosen node: what it is and how it stands, when it ran, why it failed, what it decided and with which values,
// and a way to its lines in the log.
function NodeDetails({
  node,
  run,
  subjects,
  now,
  onShowLog,
}: {
  node: PathNode;
  run: DeploymentSummary;
  subjects: readonly Subject[];
  now: number;
  onShowLog: (stepId: string) => void;
}) {
  const { i18n, t: translate } = useLingui();
  const step = node.step;
  const title = nodeTitle({ ...node.node, name: step?.name ?? node.node.name });
  const decision = node.state === "notTaken" ? null : decisionTitle(node.node, step);
  const outcomes = node.state === "notTaken" ? [] : decisionOutcomes(node.node, step);
  // Where it sits, for a node inside a container; the flow shows a leaf's number beside its name.
  const place = node.ancestors.length === 0 ? null : crumbText(node.ancestors);

  return (
    <section
      aria-label={translate`The chosen step`}
      className="flex flex-col gap-3 rounded-panel bg-panel p-4 shadow-panel"
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex min-w-0 flex-col gap-0.5">
          {place === null ? null : <span className="type-small text-muted">{place}</span>}
          <h3 className="type-heading text-ink">{title}</h3>
        </div>
        <StateTag tone={pathStateTone[node.state]}>{i18n._(pathStateLabel[node.state])}</StateTag>
      </div>
      <p className="type-small text-muted">{timeText(node, run, now)}</p>
      {(step?.pass ?? 0) > 1 && node.entry.number !== null ? (
        <p className="type-small text-muted">{passesText(step?.pass ?? 0)}</p>
      ) : null}
      {step?.state === "Failed" ? (
        <p className="type-small text-fail-text">
          {step.error ?? <Trans>The step failed without saying why.</Trans>}
        </p>
      ) : null}
      {decision !== null ? (
        <div className="flex flex-col gap-2.5 rounded-key bg-well p-3">
          <span className="type-label text-ink">{decision}</span>
          {outcomes.map((outcome) => (
            <Outcome key={outcome.path} outcome={outcome} subjects={subjects} />
          ))}
        </div>
      ) : null}
      {step !== null && step.startedUtc !== null ? (
        <AriaButton
          className="w-fit cursor-pointer type-label text-ink underline underline-offset-3 outline-none hover:text-ink-2 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-focus"
          onPress={() => {
            onShowLog(step.stepId);
          }}
        >
          <Trans>Show the log of this step</Trans>
        </AriaButton>
      ) : null}
    </section>
  );
}

function Outcome({ outcome, subjects }: { outcome: TestOutcome; subjects: readonly Subject[] }) {
  const words = outcome.test === null ? null : testWords(outcome.test, subjects);
  const subject = words?.subject ?? outcome.path;
  const operator = words?.operator ?? "";
  const value = words?.value ?? null;

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-start justify-between gap-3 type-body">
        <span className="min-w-0 text-ink">
          {value === null ? (
            <Trans>
              <span className="font-semibold text-ink-2">{subject}</span>{" "}
              <span className="text-muted">{operator}</span>
            </Trans>
          ) : (
            <Trans>
              <span className="font-semibold text-ink-2">{subject}</span>{" "}
              <span className="text-muted">{operator}</span> {value}
            </Trans>
          )}
        </span>
        {outcome.held ? (
          <span className="flex shrink-0 items-center gap-1 font-semibold text-ok-text">
            <IconCheck aria-hidden="true" size={14} stroke={2.25} />
            <Trans>Holds</Trans>
          </span>
        ) : (
          <span className="flex shrink-0 items-center gap-1 font-semibold text-ink-2">
            <IconX aria-hidden="true" size={14} stroke={2.25} />
            <Trans>Does not hold</Trans>
          </span>
        )}
      </div>
      <span className="type-small text-muted">{reportedText(outcome, subjects)}</span>
    </div>
  );
}

function passesText(passes: number): string {
  return t`Ran ${passes} times, the last is shown.`;
}

// When the node ran, in which phase and how long, or why it has no times.
function timeText(node: PathNode, run: DeploymentSummary, now: number): string {
  const step = node.step;
  const locale = formattingLocale();
  const clock = (utc: string) =>
    new Date(utc).toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" });

  if (node.state === "notTaken") {
    return t`The run did not go this way.`;
  }

  if (step === null || (step.startedUtc === null && step.finishedUtc === null)) {
    return t`Not started yet.`;
  }

  const phase = phaseLabel(step.phase);

  if (step.startedUtc === null) {
    const at = clock(step.finishedUtc ?? run.updatedUtc);

    return t`Skipped at ${at} in ${phase}.`;
  }

  const start = clock(step.startedUtc);
  const duration = stepDuration(step, now);
  const took = formatDuration(duration ?? 0);

  if (node.node.kind === "if" && step.branch !== null && step.branch !== undefined) {
    const into =
      run.startedUtc === null
        ? null
        : formatDuration(Date.parse(step.startedUtc) - Date.parse(run.startedUtc));

    return into === null
      ? t`Decided at ${start} in ${phase}.`
      : t`Decided at ${start} in ${phase}, ${into} into the run.`;
  }

  if (step.finishedUtc === null) {
    return node.state === "paused"
      ? t`Paused since ${start} in ${phase}, for ${took}.`
      : t`Running since ${start} in ${phase}, for ${took}.`;
  }

  const end = clock(step.finishedUtc);

  return t`Ran from ${start} to ${end} in ${phase}, took ${took}.`;
}
