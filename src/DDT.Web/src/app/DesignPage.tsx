// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { IconPlus } from "@tabler/icons-react";
import { queryOptions, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState, type ReactNode } from "react";

import { useLiveMarks } from "@/live/useLiveMarks";
import { layoutFlow, type WireRoute } from "@/sequences/flow/flowLayout";
import { indexTree } from "@/sequences/flow/flowTree";
import type { SequenceStep } from "@/sequences/sequences";
import { newStep } from "@/sequences/steps";
import { Button } from "@/ui/Button";
import { Checkbox, Switch } from "@/ui/Checkbox";
import { FilterSelector, NumberField, ProgressBar, SearchField } from "@/ui/Controls";
import { DeviceGlyph, type DeviceKind } from "@/ui/DeviceGlyph";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { FlowFrame, FlowNode, type FlowNodeState } from "@/ui/FlowNode";
import { FlowViewport } from "@/ui/FlowViewport";
import { FlowDots, FlowWires, type WireTone } from "@/ui/FlowWires";
import { EmptyState, Facts, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { SecretValue } from "@/ui/SecretValue";
import { ComboBox, ListBoxItem, Select } from "@/ui/Select";
import { SequenceRail, SequenceRailStrip, type RailStep } from "@/ui/SequenceRail";
import { StateTag, type StateTone } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";
import { TextField } from "@/ui/TextField";
import { showToast } from "@/ui/toasts";
import { Tooltip } from "@/ui/Tooltip";

// Every token and component in one place, in the theme and language chosen in the user menu. It exists in
// development builds only and is not translated.

const colours = [
  "frame",
  "page",
  "panel",
  "raised",
  "well",
  "field",
  "hover",
  "selected",
  "line",
  "line-soft",
  "control",
  "ink",
  "ink-2",
  "muted",
  "run",
  "attention",
  "fail",
  "ok",
  "key-primary",
  "rail-done",
  "console",
];

const rail: RailStep[] = [
  { state: "done", name: "Partition the disk", meta: "6 s" },
  { state: "running", percent: 62, name: "Apply image", meta: "62%" },
  { state: "waiting", name: "Inject drivers" },
  { state: "skipped", name: "Write the answer file", meta: "skipped" },
  { state: "waiting", name: "Restart into Windows" },
  { state: "waiting", name: "Join the domain" },
  { state: "failed", name: "Run script: baseline", meta: "failed" },
  { state: "waiting", name: "Restart" },
];

// A list patched as the hub patches one, to see the flash of a changed row, a new row entering and the rail filling.
interface DemoMachine {
  id: string;
  name: string;
  state: "Waiting" | "Deploying" | "Done" | "Failed";
  step: number;
  percent: number;
}

const demoTones: Record<DemoMachine["state"], StateTone> = {
  Waiting: "attention",
  Deploying: "run",
  Done: "ok",
  Failed: "fail",
};

const demoSteps = 4;

const demoQuery = queryOptions({
  queryKey: ["design-live-demo"],
  queryFn: () =>
    Promise.resolve<DemoMachine[]>([
      { id: "a", name: "LAB-PC-014", state: "Deploying", step: 1, percent: 30 },
      { id: "b", name: "BUILD-VM-02", state: "Deploying", step: 2, percent: 70 },
    ]),
  staleTime: Infinity,
});

function demoRail(machine: DemoMachine): RailStep[] {
  return Array.from({ length: demoSteps }, (_, index): RailStep => {
    if (machine.state === "Done" || index < machine.step) {
      return { state: "done" };
    }

    if (index > machine.step || machine.state === "Waiting") {
      return { state: "waiting" };
    }

    return machine.state === "Failed"
      ? { state: "failed" }
      : { state: "running", percent: machine.percent };
  });
}

function LiveDemo() {
  const queryClient = useQueryClient();
  const machines = useQuery(demoQuery).data ?? [];
  const mark = useLiveMarks({
    queryKey: demoQuery.queryKey,
    items: (list) => list,
    id: (machine) => machine.id,
    signature: (machine) => machine.state,
    tone: (machine) => demoTones[machine.state],
  });

  const patch = (change: (machine: DemoMachine) => DemoMachine) => {
    queryClient.setQueryData(demoQuery.queryKey, (list) => list?.map(change));
  };

  const advance = (machine: DemoMachine): DemoMachine => {
    if (machine.state !== "Deploying") {
      return machine;
    }

    if (machine.percent < 100) {
      return { ...machine, percent: Math.min(100, machine.percent + 35) };
    }

    return machine.step + 1 < demoSteps
      ? { ...machine, step: machine.step + 1, percent: 0 }
      : { ...machine, state: "Done" };
  };

  return (
    <Panel
      title="Live changes"
      flush
      actions={
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            onPress={() => {
              patch(advance);
            }}
          >
            Go on
          </Button>
          <Button
            size="sm"
            onPress={() => {
              patch((machine) =>
                machine.state === "Deploying" ? { ...machine, state: "Failed" } : machine,
              );
            }}
          >
            Fail
          </Button>
          <Button
            size="sm"
            onPress={() => {
              queryClient.setQueryData(demoQuery.queryKey, (list) =>
                list === undefined
                  ? list
                  : [
                      {
                        id: String(list.length + 1),
                        name: `LAB-PC-${String(20 + list.length)}`,
                        state: "Waiting" as const,
                        step: 0,
                        percent: 0,
                      },
                      ...list,
                    ],
              );
            }}
          >
            A machine appears
          </Button>
          <Button
            size="sm"
            variant="quiet"
            onPress={() => {
              patch((machine) =>
                machine.state === "Waiting" ? { ...machine, state: "Deploying" } : machine,
              );
            }}
          >
            Start the waiting
          </Button>
        </div>
      }
    >
      <Table aria-label="Machines, patched live" className="table-fixed">
        <TableHeader>
          <TableColumn id="name" isRowHeader className="w-48 pl-4">
            Machine
          </TableColumn>
          <TableColumn id="state" className="w-36">
            State
          </TableColumn>
          <TableColumn id="run" className="pr-4">
            Sequence
          </TableColumn>
        </TableHeader>
        <TableBody items={machines} dependencies={[mark]}>
          {(machine) => (
            <TableRow id={machine.id} className={mark(machine.id)}>
              <TableCell className="pl-4">{machine.name}</TableCell>
              <TableCell>
                <StateTag tone={demoTones[machine.state]}>{machine.state}</StateTag>
              </TableCell>
              <TableCell className="pr-4">
                <SequenceRailStrip steps={demoRail(machine)} label={machine.state} />
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </Panel>
  );
}

// The design canvas's flow: every kind of node, an IF, a group and a repeat, laid out by the flow builder's layout.
interface DemoNode {
  kind: SequenceStep["kind"];
  name: string;
  detail: string;
  run: FlowNodeState;
  runDetail?: string;
  code?: boolean;
  problem?: boolean;
  inside?: DemoNode[];
  otherwise?: DemoNode[];
}

const demoFlow: DemoNode[] = [
  {
    kind: "partition",
    name: "Partition the disk",
    detail: "Erases the disk and makes the partitions",
    run: "done",
    runDetail: "Took 48 s",
  },
  {
    kind: "if",
    name: "Is it a Latitude?",
    detail: 'Model contains "Latitude"',
    run: "done",
    runDetail: 'Took Then: Latitude 7450 contains "Latitude"',
    inside: [
      {
        kind: "applyImage",
        name: "Apply Windows 11 for Latitudes",
        detail: "Windows 11 25H2 Enterprise, with Office",
        run: "done",
        runDetail: "Took 3 min 10 s",
      },
      {
        kind: "injectDrivers",
        name: "Add the Latitude drivers",
        detail: "Driver packages matched to the model",
        run: "done",
        runDetail: "Took 41 s",
      },
    ],
    otherwise: [
      {
        kind: "applyImage",
        name: "Apply Windows 11",
        detail: "Windows 11 25H2 Enterprise",
        run: "notTaken",
        runDetail: "Not taken",
      },
    ],
  },
  {
    kind: "setVariable",
    name: "Name the computer",
    detail: "ComputerName = PC-{{SerialNumber|alnum|right:8}}",
    code: true,
    run: "done",
    runDetail: "ComputerName = PC-G2341KXQ",
  },
  {
    kind: "joinDomain",
    name: "Join the domain",
    detail: "With an account asked at the machine",
    run: "done",
    runDetail: "Took 14 s",
  },
  {
    kind: "group",
    name: "Berlin office",
    detail: "Only when Subnet is in 10.20.0.0/16",
    run: "done",
    inside: [
      {
        kind: "runScript",
        name: "Map the site share",
        detail: "Deploy share may not connect to fs01.berlin",
        problem: true,
        run: "done",
        runDetail: "Took 6 s",
      },
      {
        kind: "runScript",
        name: "Install the site printer",
        detail: "Only when Device kind is Desktop",
        run: "skipped",
        runDetail: "Skipped: this machine is a laptop",
      },
    ],
  },
  {
    kind: "repeat",
    name: "Wait for the share",
    detail: "Until LastStepFailed is No, at most 5 times",
    run: "running",
    runDetail: "Time 2 of at most 5",
    inside: [
      {
        kind: "runScript",
        name: "Test the share",
        detail: "Run script, cmd",
        run: "running",
        runDetail: "Running for 12 s",
      },
    ],
  },
  {
    kind: "pause",
    name: "Check the asset tag",
    detail: "Waits until someone lets the run go on",
    run: "paused",
    runDetail: "Paused for 4 min",
  },
  {
    kind: "reboot",
    name: "Restart",
    detail: "Restarts into the finished Windows",
    run: "waiting",
    runDetail: "Not started",
  },
];

// The demo as a sequence's steps, with ids from their places, and each node's demo by id.
function demoSequence(
  nodes: DemoNode[],
  prefix: string,
  byId: Map<string, DemoNode>,
): SequenceStep[] {
  return nodes.map((node, index) => {
    const id = `${prefix}${String(index)}`;
    const base = { ...newStep(node.kind, id), name: node.name };
    const inside = demoSequence(node.inside ?? [], `${id}.`, byId);
    const otherwise = demoSequence(node.otherwise ?? [], `${id}!`, byId);

    byId.set(id, node);

    switch (base.kind) {
      case "group":
      case "repeat":
        return { ...base, steps: inside };
      case "if":
        return { ...base, then: inside, else: otherwise };
      default:
        return base;
    }
  });
}

// The node states a card can show, one card each.
const nodeStates: { detail: string; state?: FlowNodeState; mark?: "problem" | "warning" }[] = [
  { detail: "Edit" },
  { detail: "Choose the image to apply.", mark: "problem" },
  { detail: "A warning", mark: "warning" },
  { detail: "62 %", state: "running" },
  { detail: "Took 3 min", state: "done" },
  { detail: "Not signed for this firmware", state: "failed" },
  { detail: "Skipped", state: "skipped" },
  { detail: "Paused for 4 min", state: "paused" },
  { detail: "Not taken", state: "notTaken" },
];

function FlowDemo() {
  const [run, setRun] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [selected, setSelected] = useState<string | null>("1");
  const { steps, byId } = useMemo(() => {
    const nodes = new Map<string, DemoNode>();

    return { steps: demoSequence(demoFlow, "", nodes), byId: nodes };
  }, []);
  const tree = indexTree(steps);
  const layout = layoutFlow(steps, { collapsed: new Set(collapsed ? ["4"] : []) });
  const stateOf = (id: string | null): FlowNodeState =>
    id === null ? "done" : (byId.get(id)?.run ?? "waiting");
  const tone = (route: WireRoute): WireTone => {
    if (!run) {
      return "edit";
    }

    if (route.branch?.name === "else") {
      return "not";
    }

    const state = stateOf(route.to ?? route.from);

    return state === "notTaken" ? "not" : state === "waiting" ? "ahead" : "taken";
  };

  return (
    <Panel
      title="Flow"
      actions={
        <div className="flex flex-wrap gap-4">
          <Switch isSelected={run} onChange={setRun}>
            A run
          </Switch>
          <Switch isSelected={collapsed} onChange={setCollapsed}>
            Collapse the group
          </Switch>
        </div>
      }
    >
      <div className="flex flex-col gap-4">
        <FlowViewport
          label="Flow of the demo sequence"
          contentWidth={layout.width}
          contentHeight={layout.height}
          className="h-[36rem]"
          minimap={[
            ...layout.frames.map((frame) => ({ ...frame, tone: "frame" as const })),
            ...layout.boxes.map((box) => ({
              ...box,
              tone: run && stateOf(box.id) === "notTaken" ? ("muted" as const) : ("node" as const),
            })),
          ]}
        >
          {layout.frames.map((frame) => (
            <FlowFrame
              key={frame.id}
              className="absolute"
              style={{ left: frame.x, top: frame.y, width: frame.w, height: frame.h }}
            />
          ))}
          <FlowWires
            width={layout.width}
            height={layout.height}
            wires={layout.wires}
            arrows={layout.arrows}
            tone={tone}
          />
          {layout.boxes.map((box) => {
            const node = byId.get(box.id);
            const leaves = tree.entries.filter(
              (entry) => entry.number !== null && entry.node.id.startsWith(`${box.id}.`),
            );

            return node === undefined ? null : (
              <FlowNode
                key={box.id}
                kind={node.kind}
                name={node.name}
                number={tree.byId.get(box.id)?.number ?? null}
                detail={run ? (node.runDetail ?? node.detail) : node.detail}
                code={node.code === true && !run}
                state={run ? node.run : "edit"}
                percent={40}
                selected={!run && selected === box.id}
                {...(node.problem === true ? { mark: "problem" as const } : {})}
                branch={run && node.kind === "if" ? "then" : null}
                collapsed={box.kind === "collapsed"}
                strip={leaves.map((entry) => ({
                  state: !run
                    ? "waiting"
                    : stateOf(entry.node.id) === "skipped"
                      ? "skipped"
                      : "done",
                }))}
                className="absolute"
                style={{ left: box.x, top: box.y, width: box.w, height: box.h }}
              />
            );
          })}
          <FlowDots
            width={layout.width}
            height={layout.height}
            ports={layout.ports}
            joins={layout.joins}
            selectedId={run ? null : selected}
            tone={tone}
          />
          {run
            ? null
            : layout.slots.map((slot, index) => (
                <span
                  key={index}
                  aria-hidden="true"
                  className="absolute flex size-5 -translate-x-1/2 -translate-y-1/2 items-center justify-center rounded-full bg-raised text-muted shadow-[inset_0_0_0_1px_var(--color-line)]"
                  style={{ left: slot.x, top: slot.y }}
                >
                  <IconPlus size={12} stroke={2} />
                </span>
              ))}
        </FlowViewport>
        <div className="flex flex-wrap gap-2">
          {["0", "1", "4"].map((id) => (
            <Button
              key={id}
              size="sm"
              variant="quiet"
              onPress={() => {
                setSelected(id);
              }}
            >
              Select {byId.get(id)?.name}
            </Button>
          ))}
        </div>
        <div className="grid grid-cols-[repeat(auto-fill,236px)] gap-4">
          {nodeStates.map((card, index) => (
            <FlowNode
              key={index}
              kind="applyImage"
              name="Apply image"
              number={2}
              detail={card.detail}
              percent={62}
              {...(card.state === undefined ? {} : { state: card.state })}
              {...(card.mark === undefined ? {} : { mark: card.mark })}
              className="h-16"
            />
          ))}
          <FlowNode
            kind="applyImage"
            name="Apply image"
            number={2}
            detail="Selected"
            selected
            className="h-16"
          />
          <FlowNode
            kind="if"
            name="Is it a Latitude?"
            detail='Model contains "Latitude"'
            className="h-25"
          />
          <FlowNode
            kind="group"
            name="Berlin office"
            collapsed
            state="running"
            strip={[{ state: "done" }, { state: "running", percent: 50 }, { state: "waiting" }]}
            className="h-21"
          />
        </div>
      </div>
    </Panel>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Panel title={title}>
      <div className="flex flex-col gap-4">{children}</div>
    </Panel>
  );
}

export function DesignPage() {
  const [filter, setFilter] = useState("all");
  const [confirm, setConfirm] = useState<"plain" | "erase" | null>(null);
  const [drawer, setDrawer] = useState(false);

  return (
    <Page>
      <PageHeader title="Design system">
        <span className="text-muted">Switchgear, from src/DDT.Design/tokens.json</span>
      </PageHeader>

      <Section title="Colour">
        <div className="grid grid-cols-[repeat(auto-fill,minmax(7.5rem,1fr))] gap-3">
          {colours.map((name) => (
            <div key={name} className="flex flex-col gap-1">
              <span
                className="h-10 rounded-key shadow-[inset_0_0_0_1px_var(--color-line)]"
                style={{ background: `var(--color-${name})` }}
              />
              <span className="type-data text-ink-2">{name}</span>
            </div>
          ))}
        </div>
      </Section>

      <Section title="Type">
        <span className="type-display text-run-text">62%</span>
        <span className="type-title">All machines</span>
        <span className="type-heading">Windows 11 24H2 with Office</span>
        <span className="type-label">Assign sequence</span>
        <span className="type-body">
          Leave this machine on. It restarts by itself and finishes in Windows.
        </span>
        <span className="type-small text-muted">Registered at 09:51 from 10.20.4.130</span>
        <span className="type-data">3C:52:82:6A:1F:0B</span>
      </Section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Section title="Keys">
          <div className="flex flex-wrap gap-2">
            <Button variant="primary">Assign sequence</Button>
            <Button>Approve</Button>
            <Button variant="quiet">Cancel</Button>
            <Button variant="danger">Stop run</Button>
            <Button isDisabled>Reject</Button>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button variant="primary" size="sm">
              Approve
            </Button>
            <Button size="sm">Assign</Button>
            <Tooltip content="Explains the key it sits on.">
              <Button size="sm" variant="quiet">
                With a tooltip
              </Button>
            </Tooltip>
          </div>
        </Section>

        <Section title="State">
          <div className="flex flex-wrap gap-2">
            <StateTag tone="run">Deploying</StateTag>
            <StateTag tone="attention">Waiting</StateTag>
            <StateTag tone="fail">Failed</StateTag>
            <StateTag tone="ok">Done</StateTag>
            <StateTag tone="idle">Ready</StateTag>
            <StateTag tone="retired">Retired</StateTag>
          </div>
          <FilterSelector
            label="Show machines by state"
            selected={filter}
            onChange={setFilter}
            options={[
              { id: "all", label: "All", count: 38 },
              { id: "running", label: "Deploying", count: 3 },
              { id: "waiting", label: "Waiting", count: 2, tone: "attention" },
              { id: "failed", label: "Failed", count: 1, tone: "fail" },
              { id: "done", label: "Done", count: 26 },
            ]}
          />
          <div className="flex gap-2">
            {(["laptop", "desktop", "tablet", "server", "virtual", "unknown"] as DeviceKind[]).map(
              (kind) => (
                <DeviceGlyph key={kind} kind={kind} />
              ),
            )}
          </div>
        </Section>
      </div>

      <FlowDemo />

      <Section title="Sequence rail">
        <SequenceRailStrip
          steps={rail}
          label="Step 2 of 8 running, 62 percent"
          className="max-w-md"
        />
        <SequenceRail
          steps={rail}
          phases={[
            { label: "In Windows PE", steps: 5 },
            { label: "In the installed Windows", steps: 3 },
          ]}
          describe={(step, index) =>
            `Step ${String(index + 1)}, ${typeof step.name === "string" ? step.name : ""}, ${step.state}`
          }
        />
        <ProgressBar label="Uploading win11-24h2.wim" value={42} className="max-w-md" />
        <ProgressBar label="Checking the image" className="max-w-md" />
      </Section>

      <div className="grid gap-4 lg:grid-cols-2">
        <Section title="Fields">
          <SearchField label="Find a machine" placeholder="Name, MAC, serial or address" />
          <TextField
            label="Computer name"
            hint="Up to 15 letters, digits and hyphens."
            mono
            defaultValue="LAB-PC-016"
          />
          <TextField
            label="Computer name"
            mono
            defaultValue="LAB-PC-016!"
            isInvalid
            errorMessage='Use letters, digits and hyphens only. Remove the "!".'
          />
          <Select
            label="Task sequence"
            placeholder="Choose a sequence"
            hint="Chosen by the rule for Dell Latitude 7450."
          >
            <ListBoxItem id="w11">Windows 11 24H2 with Office</ListBoxItem>
            <ListBoxItem id="ubuntu" description="Writes a raw disk image">
              Ubuntu 24.04 LTS
            </ListBoxItem>
            <ListBoxItem id="broken" isDisabled>
              Kiosk (2 problems)
            </ListBoxItem>
          </Select>
          <ComboBox label="Model" placeholder="Type to narrow the list">
            <ListBoxItem id="7450">Dell Latitude 7450</ListBoxItem>
            <ListBoxItem id="t14">Lenovo ThinkPad T14 Gen 5</ListBoxItem>
            <ListBoxItem id="840">HP EliteBook 840 G10</ListBoxItem>
          </ComboBox>
          <NumberField label="Timeout" hint="Minutes." defaultValue={60} minValue={1} />
          <SecretValue label="One-time password" value="Tq8v-Rk3m-Wz6p-Hd2n" />
          <Checkbox defaultSelected>Restart after this step</Checkbox>
          <Switch defaultSelected>Require web approval</Switch>
        </Section>

        <Section title="Feedback">
          <Notice tone="fail" title="BUILD-VM-02 failed at step 2">
            The image is not signed under a CA this machine trusts.
          </Notice>
          <Notice tone="attention">The live connection to the server is lost.</Notice>
          <Notice title="Nothing needs you">Every machine is deploying or done.</Notice>
          <div className="flex flex-wrap gap-2">
            <Button
              onPress={() => {
                showToast({
                  title: "ACC-PC-004 is done",
                  description: "Windows 11 24H2 with Office took 36 minutes.",
                  tone: "ok",
                });
              }}
            >
              Toast
            </Button>
            <Button
              onPress={() => {
                showToast({
                  title: "BUILD-VM-02 failed",
                  description: "Step 2: not signed for this firmware.",
                  tone: "fail",
                });
              }}
            >
              Failure toast
            </Button>
            <Button
              onPress={() => {
                setConfirm("plain");
              }}
            >
              Confirm
            </Button>
            <Button
              variant="danger"
              onPress={() => {
                setConfirm("erase");
              }}
            >
              Erase
            </Button>
            <Button
              onPress={() => {
                setDrawer(true);
              }}
            >
              Drawer
            </Button>
          </div>
          <EmptyState title="No machines yet" action={<Button>How netboot works</Button>}>
            Netboot a PC on a network DDT answers. It shows up here within a few seconds.
          </EmptyState>
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-1/2" />
        </Section>
      </div>

      <Section title="Facts">
        <Facts
          layout="plate"
          items={[
            { label: "Serial", value: "7XK2QH3", mono: true },
            { label: "MAC address", value: "3C:52:82:6A:1F:0B", mono: true },
            { label: "Address", value: "10.20.4.114", mono: true },
            { label: "Secure Boot", value: "On, trusts UEFI CA 2023" },
          ]}
        />
        <Facts
          items={[
            { label: "Erases", value: "Disk 0, SK hynix PC801, 512 GB" },
            { label: "Chosen", value: "At the machine by m.huber" },
          ]}
        />
      </Section>

      <Panel title="Table" flush>
        <Table
          aria-label="Machines"
          selectionMode="multiple"
          sortDescriptor={{ column: "name", direction: "ascending" }}
        >
          <TableHeader>
            <TableColumn id="name" isRowHeader allowsSorting>
              Machine
            </TableColumn>
            <TableColumn id="state">State</TableColumn>
            <TableColumn id="seen" allowsSorting>
              Seen
            </TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow id="a">
              <TableCell>
                <span className="flex items-center gap-3">
                  <DeviceGlyph kind="laptop" />
                  LAB-PC-014
                </span>
              </TableCell>
              <TableCell>
                <StateTag tone="run">Deploying</StateTag>
              </TableCell>
              <TableCell className="text-muted">Now</TableCell>
            </TableRow>
            <TableRow id="b">
              <TableCell>
                <span className="flex items-center gap-3">
                  <DeviceGlyph kind="virtual" />
                  BUILD-VM-02
                </span>
              </TableCell>
              <TableCell>
                <StateTag tone="fail">Failed</StateTag>
              </TableCell>
              <TableCell className="text-muted">4 min</TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </Panel>

      <LiveDemo />

      <Panel title="Tabs">
        <Tabs>
          <TabList aria-label="Machine">
            <Tab id="run">Current run</Tab>
            <Tab id="history">Run history</Tab>
            <Tab id="log">Log</Tab>
          </TabList>
          <TabPanel id="run">The steps and the current one.</TabPanel>
          <TabPanel id="history">Earlier runs.</TabPanel>
          <TabPanel id="log">Every line the agent sent.</TabPanel>
        </Tabs>
      </Panel>

      <ConfirmDialog
        isOpen={confirm !== null}
        onOpenChange={(open) => {
          if (!open) setConfirm(null);
        }}
        title={confirm === "erase" ? "Erase disk 0 on LAB-PC-016?" : "Stop the run on LAB-PC-014?"}
        confirmLabel={confirm === "erase" ? "Erase and run" : "Stop the run"}
        danger
        {...(confirm === "erase" ? { typedWord: "ERASE" } : {})}
        onConfirm={() => {
          setConfirm(null);
        }}
      >
        {confirm === "erase" ? (
          <p>
            Windows 11 24H2 with Office removes every partition on disk 0, a SK hynix PC801 with 512
            GB.
          </p>
        ) : (
          <p>The machine stops after the step it is on. Nothing already written is undone.</p>
        )}
      </ConfirmDialog>

      <Drawer
        isOpen={drawer}
        onOpenChange={setDrawer}
        title="LAB-PC-014"
        footer={<Button variant="primary">Open machine</Button>}
      >
        <p className="text-ink-2">Details of the machine picked in the list.</p>
      </Drawer>
    </Page>
  );
}
