// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { useState, type ReactNode } from "react";

import { Button } from "@/ui/Button";
import { Checkbox, Switch } from "@/ui/Checkbox";
import { FilterSelector, NumberField, ProgressBar, SearchField } from "@/ui/Controls";
import { DeviceGlyph, type DeviceKind } from "@/ui/DeviceGlyph";
import { ConfirmDialog } from "@/ui/Dialog";
import { Drawer } from "@/ui/Drawer";
import { EmptyState, Facts, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ComboBox, ListBoxItem, Select } from "@/ui/Select";
import { SequenceRail, SequenceRailStrip, type RailStep } from "@/ui/SequenceRail";
import { StateTag } from "@/ui/StateTag";
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
