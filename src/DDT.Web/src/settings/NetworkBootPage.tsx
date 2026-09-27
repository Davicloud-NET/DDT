// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery, useQueryClient, type UseQueryResult } from "@tanstack/react-query";
import { useId, useState } from "react";

import { currentUserQuery } from "@/auth/auth";
import { liveListOptions } from "@/live/freshness";
import { useLiveStatus } from "@/live/useLiveStatus";
import { Button } from "@/ui/Button";
import { Checkbox } from "@/ui/Checkbox";
import { ConfirmDialog } from "@/ui/Dialog";
import { Facts, Page, PageHeader, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { ComboBox, ListBoxItem, Select } from "@/ui/Select";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";

import {
  architectureDescription,
  architecturesInOrder,
  bootManagers,
  bootUrl,
  canonicalArchitecture,
  defaultBootManager,
  hostOf,
  isListed,
  methodFor,
  otherEntries,
  pxeConfigurationQuery,
  pxeInterfacesQuery,
  rescanPxe,
  withEntries,
  withInterface,
  type BootTargetSettings,
  type PxeConfiguration,
  type PxeHostInterfaces,
  type PxeSettings,
} from "./networkBoot";
import { putSection } from "./settings";
import {
  LockNote,
  SettingLines,
  SettingNumber,
  SettingSwitch,
  SettingText,
  SettingsGroup,
  SettingsSection,
} from "./SettingsParts";
import { useSettingsForm, valueAt, type SettingsForm } from "./useSettingsForm";

type PxeForm = SettingsForm<PxeSettings>;

// The fields the page shows outside the boot targets, whose problems land on them; any other problem is listed at the
// top of the section.
const placedFields = [
  "interfaces",
  "enableProxyDhcp",
  "enableTftp",
  "tftpSinglePort",
  "tftpMaxWindowSize",
  "maxConcurrentTftpTransfers",
  "authorisedRelayAgents",
];

// Boot > Network boot: which interfaces DDT answers netboot on, how ProxyDHCP and TFTP behave, and which boot file
// each client architecture is sent. The pxe section runs code on every machine that netboots, so only administrators
// read it; the server refuses everyone else.
export function NetworkBootPage() {
  const me = useQuery(currentUserQuery).data ?? null;

  if (me === null) {
    return null;
  }

  if (!me.roles.includes("Administrator")) {
    return (
      <Page>
        <PageHeader title={<Trans>Network boot</Trans>} />
        <Notice>
          <Trans>
            Only administrators see and change network boot: which interfaces DDT answers on and
            what machines load.
          </Trans>
        </Notice>
      </Page>
    );
  }

  return <NetworkBootSettings />;
}

function NetworkBootSettings() {
  const form = useSettingsForm<PxeSettings>("pxe");
  const live = useLiveStatus();
  const hosts = useQuery({ ...pxeInterfacesQuery, ...liveListOptions(live) });
  const configuration = useQuery(pxeConfigurationQuery).data ?? null;
  const view = form.view;
  const values = form.values;

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Network boot</Trans>} />
      {form.query.isError ? (
        <Notice tone="fail">
          <Trans>The network boot settings could not be loaded.</Trans>
        </Notice>
      ) : view === null || values === null ? (
        <Skeleton className="h-64 w-full" />
      ) : (
        <SettingsSection
          form={form}
          canChange
          title={<Trans>Answering machines that netboot</Trans>}
          description={
            <Trans>
              DDT answers netboot only on the interfaces listed here. ProxyDHCP tells each machine
              which boot file to load, and TFTP or HTTP serves it from the boot directory.
            </Trans>
          }
        >
          <SectionProblems form={form} />
          <InterfacesGroup form={form} values={values} hosts={hosts} />
          <TransfersGroup form={form} />
          <BootTargetsGroup
            form={form}
            values={values}
            hosts={hosts.data ?? []}
            configuration={configuration}
          />
          <ConfigurationGroup configuration={configuration} />
          {form.dirty ? (
            <Notice tone="attention">
              <Trans>
                Saving restarts the network boot listeners on every host that runs them, which ends
                the TFTP transfers in progress. Those machines start their download again.
              </Trans>
            </Notice>
          ) : null}
        </SettingsSection>
      )}
    </Page>
  );
}

// While the stored section has problems, no host serves anything. Most problems belong to a field and show there;
// the others are listed here.
function SectionProblems({ form }: { form: PxeForm }) {
  const problems = form.view?.problems ?? [];
  const elsewhere = problems.filter(
    (problem) => !placedFields.includes(problem.field) && !problem.field.startsWith("bootTargets"),
  );

  if (problems.length === 0) {
    return null;
  }

  return (
    <Notice tone="fail">
      <span className="flex flex-col gap-1.5">
        <span>
          <Trans>
            These settings have problems, so no host serves network boot until they are fixed.
          </Trans>{" "}
          {elsewhere.length < problems.length ? (
            <Trans>The fields marked below say what.</Trans>
          ) : null}
        </span>
        {elsewhere.length > 0 ? (
          <ul className="flex list-disc flex-col gap-1 pl-5">
            {elsewhere.map((problem, index) => (
              <li key={index}>{problem.message}</li>
            ))}
          </ul>
        ) : null}
      </span>
    </Notice>
  );
}

function InterfacesGroup({
  form,
  values,
  hosts,
}: {
  form: PxeForm;
  values: PxeSettings;
  hosts: UseQueryResult<PxeHostInterfaces[]>;
}) {
  const { t } = useLingui();
  const [typed, setTyped] = useState("");
  const entries = values.interfaces;
  const lock = form.lockOf("interfaces");
  const editable = lock === null;
  const errors = form.fieldErrors("interfaces");
  const warnings = (form.view?.warnings ?? []).filter((warning) => warning.field === "interfaces");
  const reported = hosts.data ?? [];
  const others = otherEntries(entries, reported);

  const set = (next: string[]) => {
    form.change("interfaces", next);
  };

  return (
    <SettingsGroup title={<Trans>Interfaces</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          A host may have a network adapter on a segment whose DHCP belongs to someone else, so DDT
          answers on no interface until it is listed. List the interfaces machines reach DDT on, and
          never a VPN tunnel.
        </Trans>
      </p>
      {entries.length === 0 ? (
        <Notice tone="attention">
          <Trans>
            No interface is listed, so DDT answers no machine that netboots. Choose the interfaces
            below, or type one.
          </Trans>
        </Notice>
      ) : null}
      {lock === null ? null : <LockNote lock={lock} />}

      {hosts.isError ? (
        <Notice tone="fail">
          <Trans>The interfaces the hosts found could not be read.</Trans>
        </Notice>
      ) : hosts.isPending ? (
        <Skeleton className="h-16 w-full" />
      ) : reported.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>
            No host that runs network boot has reported its interfaces yet. A host reports them when
            it applies these settings without problems.
          </Trans>
        </p>
      ) : (
        <div className="grid gap-4 md:grid-cols-2">
          {reported.map((host) => {
            const name = host.host;

            return (
              <div key={name} className="flex flex-col gap-1.5">
                <span className="type-label text-ink">
                  <Trans>On {name}</Trans>
                </span>
                {host.interfaces.length === 0 ? (
                  <p className="type-small text-muted">
                    <Trans>This host found no interface with an IPv4 address.</Trans>
                  </p>
                ) : (
                  <ul aria-label={t`Interfaces on ${name}`} className="flex flex-col">
                    {host.interfaces.map((candidate) => (
                      <li
                        key={candidate.name}
                        className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line-soft py-1.5 last:border-b-0"
                      >
                        <Checkbox
                          isSelected={isListed(entries, candidate)}
                          onChange={(serve) => {
                            set(withInterface(entries, candidate, serve));
                          }}
                          isReadOnly={!editable}
                        >
                          <span className="type-data">{candidate.name}</span>
                        </Checkbox>
                        <span className="min-w-0 flex-1 type-data break-words text-muted">
                          {candidate.addresses.join(", ")}
                        </span>
                        {candidate.served ? (
                          <StateTag tone="ok">
                            <Trans>Serving</Trans>
                          </StateTag>
                        ) : null}
                      </li>
                    ))}
                  </ul>
                )}
              </div>
            );
          })}
        </div>
      )}

      {others.length > 0 ? (
        <div className="flex flex-col gap-1.5">
          <span className="type-label text-ink">
            <Trans>Other entries</Trans>
          </span>
          <ul aria-label={t`Other entries`} className="flex flex-col">
            {others.map((entry) => (
              <li
                key={entry}
                className="flex items-center gap-3 border-b border-line-soft py-1.5 last:border-b-0"
              >
                <span className="min-w-0 flex-1 type-data break-words text-ink">{entry}</span>
                {editable ? (
                  <Button
                    size="sm"
                    variant="quiet"
                    aria-label={t`Remove ${entry}`}
                    onPress={() => {
                      set(entries.filter((listed) => listed !== entry));
                    }}
                  >
                    <Trans>Remove</Trans>
                  </Button>
                ) : null}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      {editable ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            set(withEntries(entries, typed));
            setTyped("");
          }}
          className="flex flex-col gap-1.5"
        >
          <span className="flex flex-wrap items-end gap-2">
            <TextField
              label={<Trans>Another interface name or address</Trans>}
              mono
              value={typed}
              onChange={setTyped}
              className="max-w-80 flex-1"
            />
            <Button type="submit" isDisabled={typed.trim() === ""}>
              <Trans>Add</Trans>
            </Button>
          </span>
          <span className="type-small text-muted">
            <Trans>
              For an adapter no host reported, such as one still to come. An IPv4 address picks the
              interface that has it.
            </Trans>
          </span>
        </form>
      ) : null}

      {errors.length > 0 ? (
        <span className="type-small text-fail-text">{errors.join(" ")}</span>
      ) : null}
      {warnings.length > 0 ? (
        <ul className="flex flex-col gap-1">
          {warnings.map((warning, index) => (
            <li key={index} className="type-small text-attention-text">
              {warning.message}
            </li>
          ))}
        </ul>
      ) : null}

      <Rescan form={form} />
    </SettingsGroup>
  );
}

// Every host scans its interfaces and applies the section again, for an adapter that appeared or an address that
// changed. That restarts the listeners, so it asks first; the answer is the section with the new apply states.
function Rescan({ form }: { form: PxeForm }) {
  const queryClient = useQueryClient();
  const [asking, setAsking] = useState(false);
  const rescan = useMutation({
    mutationFn: rescanPxe,
    onSuccess: (view) => {
      putSection(queryClient, view);
      setAsking(false);
    },
  });

  return (
    <div className="flex flex-wrap items-center gap-3">
      <Button
        size="sm"
        isDisabled={form.dirty || rescan.isPending}
        onPress={() => {
          rescan.reset();
          setAsking(true);
        }}
      >
        <Trans>Scan interfaces again</Trans>
      </Button>
      <span className="type-small text-muted">
        {form.dirty ? (
          <Trans>Save or discard your changes first.</Trans>
        ) : (
          <Trans>For a network adapter that was added, or an address that changed.</Trans>
        )}
      </span>
      <ConfirmDialog
        isOpen={asking}
        onOpenChange={setAsking}
        title={<Trans>Scan the interfaces again?</Trans>}
        confirmLabel={<Trans>Scan again</Trans>}
        onConfirm={() => {
          rescan.mutate();
        }}
        isBusy={rescan.isPending}
        error={rescan.error?.message}
      >
        <p>
          <Trans>
            Every host that runs network boot looks for its network adapters again and restarts its
            listeners with these settings. TFTP transfers in progress end, and those machines start
            their download again.
          </Trans>
        </p>
      </ConfirmDialog>
    </div>
  );
}

function TransfersGroup({ form }: { form: PxeForm }) {
  return (
    <SettingsGroup title={<Trans>ProxyDHCP and TFTP</Trans>}>
      <SettingSwitch
        form={form}
        field="enableProxyDhcp"
        canChange
        label={<Trans>Answer as ProxyDHCP</Trans>}
        hint={
          <Trans>
            DDT tells a netbooting machine its boot file, while the site's own DHCP server keeps
            handing out the addresses. Turn it off where every site's DHCP server names DDT and the
            boot file itself; DDT then listens on neither UDP 67 nor 4011.
          </Trans>
        }
      />
      <SettingSwitch
        form={form}
        field="enableTftp"
        canChange
        label={<Trans>Serve boot files over TFTP</Trans>}
        hint={
          <Trans>
            Turn it off only when another TFTP server serves them. Every TFTP boot target then needs
            that server's address.
          </Trans>
        }
      />
      <SettingSwitch
        form={form}
        field="tftpSinglePort"
        canChange
        label={<Trans>Answer TFTP from port 69</Trans>}
        hint={
          <Trans>
            Turn it on when the log shows a read request arriving but the machine never receives
            data: a stateful firewall is dropping replies from a fresh port. Replies then leave by
            the route back to the machine, which has to go through a served interface.
          </Trans>
        }
      />
      <div className="flex flex-wrap gap-4">
        <SettingNumber
          form={form}
          field="tftpMaxWindowSize"
          canChange
          label={<Trans>Largest TFTP window</Trans>}
          hint={
            <Trans>
              The most blocks DDT sends before the machine acknowledges them, whatever the boot
              image asks for; 1 to 64. 16 was reliable in tests; lower it to 8 or 4 where a link
              loses packets.
            </Trans>
          }
          minValue={1}
          maxValue={64}
        />
        <SettingNumber
          form={form}
          field="maxConcurrentTftpTransfers"
          canChange
          label={<Trans>TFTP transfers at once</Trans>}
          hint={<Trans>A machine over the limit waits until a transfer ends.</Trans>}
          minValue={1}
        />
      </div>
      <SettingLines
        form={form}
        field="authorisedRelayAgents"
        canChange
        label={<Trans>Authorized relay agents</Trans>}
        hint={
          <Trans>
            One IPv4 address per line. DDT answers a request a DHCP relay forwards from another site
            only when that relay is listed here.
          </Trans>
        }
      />
    </SettingsGroup>
  );
}

// The problems of a boot target's field, which the server names bootTargets[X64Uefi].bootFile. A key typed in another
// case is named as stored by some checks and by its member name by others, so both are looked up.
function targetErrors(form: PxeForm, key: string, field: string): string[] {
  const canonical = canonicalArchitecture(key);
  const suffix = field === "" ? "" : `.${field}`;
  const names = [`bootTargets[${key}]${suffix}`];

  if (canonical !== null && canonical !== key) {
    names.push(`bootTargets[${canonical}]${suffix}`);
  }

  return names.flatMap((name) => form.fieldErrors(name));
}

// The form as the shared fields of a boot target see it: bootTargets.X64Uefi.serverAddress reads and changes the
// value, its problems come under the server's name for it, and the lock of the whole collection shows once, above the
// targets, instead of under every field.
function targetForm(form: PxeForm): PxeForm {
  return {
    ...form,
    fieldErrors: (field: string) => {
      const [, key = "", ...rest] = field.split(".");

      return field.startsWith("bootTargets.")
        ? targetErrors(form, key, rest.join("."))
        : form.fieldErrors(field);
    },
    lockOf: () => null,
  };
}

function BootTargetsGroup({
  form,
  values,
  hosts,
  configuration,
}: {
  form: PxeForm;
  values: PxeSettings;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}) {
  const { i18n } = useLingui();
  const targets = values.bootTargets;
  const keys = Object.keys(targets).sort((a, b) => order(a) - order(b) || a.localeCompare(b, "en"));
  const lock = form.lockOf("bootTargets");
  const editable = lock === null;
  const used = new Set(keys.map((key) => key.toLowerCase()));
  const available = architecturesInOrder.filter((name) => !used.has(name.toLowerCase()));
  const [chosen, setChosen] = useState<string | null>(null);
  const adding = chosen !== null && available.includes(chosen) ? chosen : (available[0] ?? null);
  const errors = form.fieldErrors("bootTargets");

  const add = () => {
    if (adding === null) {
      return;
    }

    const method = methodFor(adding);
    const port = configuration?.httpBootPort ?? null;
    const target: BootTargetSettings = {
      method,
      // Only the x64 boot managers are in the boot image layout.
      bootFile: adding.startsWith("X64")
        ? method === "Http"
          ? port === null
            ? null
            : bootUrl(window.location.hostname, port, defaultBootManager)
          : defaultBootManager
        : null,
      serverAddress: null,
      serverHostName: null,
      advertiseBootServerDiscovery: false,
    };

    form.change("bootTargets", { ...targets, [adding]: target });
    setChosen(null);
  };

  return (
    <SettingsGroup title={<Trans>Boot targets</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          ProxyDHCP sends a machine the boot file of the architecture its firmware reports, and
          answers no architecture without a boot target. A site whose own DHCP server names DDT and
          the boot file needs none.
        </Trans>
      </p>
      {lock === null ? null : <LockNote lock={lock} />}
      {errors.length > 0 ? (
        <span className="type-small text-fail-text">{errors.join(" ")}</span>
      ) : null}

      {keys.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>No boot target yet, so ProxyDHCP answers no machine.</Trans>
        </p>
      ) : (
        keys.map((key) => (
          <BootTarget
            key={key}
            form={form}
            targetKey={key}
            target={targets[key] ?? null}
            editable={editable}
            hosts={hosts}
            configuration={configuration}
          />
        ))
      )}

      {editable && adding !== null ? (
        <div className="flex flex-wrap items-end gap-2">
          <Select
            label={<Trans>Architecture</Trans>}
            value={adding}
            onChange={(key) => {
              setChosen(key === null ? null : String(key));
            }}
            className="w-80 max-w-full"
          >
            {available.map((name) => {
              const description = architectureDescription(name);

              return (
                <ListBoxItem
                  key={name}
                  id={name}
                  textValue={name}
                  {...(description === null ? {} : { description: i18n._(description) })}
                >
                  {name}
                </ListBoxItem>
              );
            })}
          </Select>
          <Button onPress={add}>
            <Trans>Add boot target</Trans>
          </Button>
        </div>
      ) : null}
    </SettingsGroup>
  );
}

function order(key: string): number {
  const index = architecturesInOrder.indexOf(canonicalArchitecture(key) ?? "");

  return index < 0 ? architecturesInOrder.length : index;
}

function BootTarget({
  form,
  targetKey,
  target,
  editable,
  hosts,
  configuration,
}: {
  form: PxeForm;
  targetKey: string;
  target: BootTargetSettings | null;
  editable: boolean;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}) {
  const { i18n, t } = useLingui();
  const headingId = useId();
  const fields = targetForm(form);
  const key = targetKey;
  const canonical = canonicalArchitecture(key);
  const description = architectureDescription(key);
  const method = canonical === null ? null : methodFor(canonical);
  const stored = target?.method ?? null;
  const wrongMethod = method !== null && stored?.toLowerCase() !== method.toLowerCase();
  const keyErrors = targetErrors(form, key, "");
  const methodErrors = targetErrors(form, key, "method");
  const path = (field: string) => `bootTargets.${key}.${field}`;

  return (
    <div
      role="group"
      aria-labelledby={headingId}
      className="flex flex-col gap-3 rounded-key p-3.5 shadow-[inset_0_0_0_1px_var(--color-line-soft)]"
    >
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h4 id={headingId} className="type-data text-ink">
          {key}
        </h4>
        {description === null ? null : (
          <span className="type-small text-muted">{i18n._(description)}</span>
        )}
        <span className="flex-1" />
        {editable ? (
          <Button
            size="sm"
            variant="quiet"
            aria-label={t`Remove the boot target for ${key}`}
            onPress={() => {
              const rest = Object.fromEntries(
                Object.entries(form.values?.bootTargets ?? {}).filter(([other]) => other !== key),
              );

              form.change("bootTargets", rest);
            }}
          >
            <Trans>Remove</Trans>
          </Button>
        ) : null}
      </div>

      {keyErrors.length > 0 ? (
        <span className="type-small text-fail-text">{keyErrors.join(" ")}</span>
      ) : null}

      {method === null ? null : (
        <div className="flex flex-wrap items-center gap-3">
          <span className="type-small text-ink-2">
            {method === "Http" ? (
              <Trans>Its firmware loads the boot file over HTTP, from a URL.</Trans>
            ) : (
              <Trans>Its firmware loads the boot file over TFTP.</Trans>
            )}
          </span>
          {wrongMethod ? (
            <>
              <span className="type-small text-fail-text">
                {methodErrors.length > 0 ? (
                  methodErrors.join(" ")
                ) : (
                  <Trans>The stored method does not suit this architecture.</Trans>
                )}
              </span>
              {editable ? (
                <Button
                  size="sm"
                  onPress={() => {
                    form.change(path("method"), method);
                  }}
                >
                  {method === "Http" ? <Trans>Use HTTP</Trans> : <Trans>Use TFTP</Trans>}
                </Button>
              ) : null}
            </>
          ) : null}
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-2">
        <BootFile
          form={fields}
          field={path("bootFile")}
          http={method === "Http"}
          editable={editable}
          hosts={hosts}
          configuration={configuration}
        />
        <SettingText
          form={fields}
          field={path("serverAddress")}
          canChange={editable}
          label={<Trans>Server address</Trans>}
          hint={
            method === "Http" ? (
              <Trans>
                The IPv4 address the answer names as the boot server; the URL decides where the boot
                file comes from. Left empty, DDT's address on the interface the request arrived on.
              </Trans>
            ) : (
              <Trans>
                The IPv4 address machines load the boot file from. Left empty, DDT's address on the
                interface the request arrived on, which suits a host serving several networks.
              </Trans>
            )
          }
          mono
        />
        <SettingText
          form={fields}
          field={path("serverHostName")}
          canChange={editable}
          label={<Trans>Server host name</Trans>}
          hint={
            <Trans>
              The boot server's name in the answer, in ASCII and at most 63 characters. Left empty,
              its address.
            </Trans>
          }
          mono
        />
      </div>
      <SettingSwitch
        form={fields}
        field={path("advertiseBootServerDiscovery")}
        canChange={editable}
        label={<Trans>Advertise boot server discovery</Trans>}
        hint={
          <Trans>
            Only ever useful for BIOS machines: UEFI firmware can refuse an answer that carries it.
          </Trans>
        }
      />
    </div>
  );
}

// The boot file, with the two boot managers of the boot image layout to pick from and any other path or URL typed.
// For HTTP, the URLs are built from a server name, the boot port and /boot/.
function BootFile({
  form,
  field,
  http,
  editable,
  hosts,
  configuration,
}: {
  form: PxeForm;
  field: string;
  http: boolean;
  editable: boolean;
  hosts: PxeHostInterfaces[];
  configuration: PxeConfiguration | null;
}) {
  const { t } = useLingui();
  const stored = valueAt(form.values, field);
  const value = typeof stored === "string" ? stored : "";
  const errors = form.fieldErrors(field);
  const port = configuration?.httpBootPort ?? null;
  const here = window.location.hostname;
  const [server, setServer] = useState(() => hostOf(value) ?? here);
  const name = server.trim() === "" ? here : server.trim();
  const offered = bootManagers.flatMap((manager) => {
    const file = http ? (port === null ? null : bootUrl(name, port, manager.path)) : manager.path;
    const description =
      manager.authority === "2011"
        ? t`Microsoft 2011 CA. The default, which most machines trust.`
        : t`Windows UEFI CA 2023.`;

    return file === null ? [] : [{ file, description }];
  });
  // The addresses of the interfaces the hosts serve, which a machine reaches without a name to look up.
  const addresses = [
    ...new Set(
      hosts.flatMap((host) =>
        host.interfaces
          .filter((candidate) => candidate.served)
          .flatMap((candidate) => candidate.addresses),
      ),
    ),
  ];
  const portText = port === null ? "" : String(port);

  return (
    <>
      {http ? (
        <ComboBox
          label={<Trans>Server name for the URL</Trans>}
          hint={
            <Trans>
              The name or IPv4 address machines reach DDT by. The boot file offers URLs with it.
            </Trans>
          }
          mono
          allowsCustomValue
          inputValue={server}
          onInputChange={setServer}
          isReadOnly={!editable}
        >
          {[...new Set([here, ...addresses])].map((option) => (
            <ListBoxItem key={option} id={option} textValue={option}>
              {option}
            </ListBoxItem>
          ))}
        </ComboBox>
      ) : null}
      <ComboBox
        label={<Trans>Boot file</Trans>}
        hint={
          http ? (
            port === null ? (
              <Trans>
                An http URL on DDT's boot port, below /boot/, where it serves the boot directory.
                Machines that no longer trust the Microsoft 2011 CA need bootmgfw_ex.efi, signed by
                the 2023 one.
              </Trans>
            ) : (
              <Trans>
                An http URL on port {portText}, below /boot/, where DDT serves the boot directory.
                Machines that no longer trust the Microsoft 2011 CA need bootmgfw_ex.efi, signed by
                the 2023 one.
              </Trans>
            )
          ) : (
            <Trans>
              A path in the boot directory. Machines that no longer trust the Microsoft 2011 CA need
              x64/bootmgfw_ex.efi, signed by the 2023 one.
            </Trans>
          )
        }
        mono
        allowsCustomValue
        inputValue={value}
        onInputChange={(next) => {
          form.change(field, next.trim() === "" ? null : next);
        }}
        isReadOnly={!editable}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
      >
        {offered.map((option) => (
          <ListBoxItem
            key={option.file}
            id={option.file}
            textValue={option.file}
            description={option.description}
          >
            {option.file}
          </ListBoxItem>
        ))}
      </ComboBox>
    </>
  );
}

// HttpBootPort and BootDirectory are read before the server starts, so they stay in configuration.
function ConfigurationGroup({ configuration }: { configuration: PxeConfiguration | null }) {
  const { t } = useLingui();
  const unknown = t`Not known`;

  return (
    <SettingsGroup title={<Trans>Set in configuration</Trans>}>
      <p className="type-small text-ink-2">
        <Trans>
          These stay in configuration, as DDT:Pxe:HttpBootPort and DDT:Pxe:BootDirectory: a port
          that is taken would stop the server, and everything in the boot directory is served to
          anyone who asks. A relative boot directory is inside DDT:StorePath.
        </Trans>
      </p>
      <Facts
        items={[
          {
            label: <Trans>HTTP boot port</Trans>,
            value:
              configuration?.httpBootPort === null || configuration?.httpBootPort === undefined
                ? unknown
                : String(configuration.httpBootPort),
            mono: true,
          },
          {
            label: <Trans>Boot directory</Trans>,
            value: configuration?.bootDirectory ?? unknown,
            mono: true,
          },
        ]}
      />
    </SettingsGroup>
  );
}
