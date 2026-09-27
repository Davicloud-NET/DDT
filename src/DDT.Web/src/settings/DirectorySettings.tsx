// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useMutation, useQuery } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";

import type { CurrentUser } from "@/auth/auth";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { NumberField, SearchField } from "@/ui/Controls";
import { Facts, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { TextField } from "@/ui/TextField";
import { directoryQuery, findGroups } from "@/users/users";
import { roleLabel } from "@/users/userView";

import { RoleMapEditor } from "./RoleMapEditor";
import type { SecretAction } from "./settings";
import {
  LockNote,
  SettingSecret,
  SettingSelect,
  SettingSwitch,
  SettingText,
  SettingsGroup,
  SettingsSection,
} from "./SettingsParts";
import {
  connectionChanged,
  DIRECTORY_PROOF_HEADER,
  DIRECTORY_PROOF_LIFETIME_MS,
  entryOf,
  needsDirectoryProof,
  proofFits,
  secondsOf,
  spanOf,
  testLdap,
  type DirectoryProof,
  type LdapSettings,
  type LdapTestResult,
} from "./signIn";
import { useSettingsForm, type SettingsForm } from "./useSettingsForm";

// How long typing rests before the directory is asked, so a group name is not searched letter by letter.
const SEARCH_DELAY_MS = 300;

// Braces in a message are its arguments, so the user filter's placeholder goes into the texts as a value.
const USER_TOKEN = "{0}";

// The section ldap: people sign in with their directory user name and password, and the group map decides their
// role. A directory administrator's save that changes who can sign in carries the proof of a test of their own
// sign-in with exactly these values, which the server asks for so that nobody locks themselves out.
export function DirectorySettings({ me }: { me: CurrentUser }) {
  const { t } = useLingui();
  const [proof, setProof] = useState<DirectoryProof | null>(null);
  const form = useSettingsForm<LdapSettings>("ldap", () =>
    proof !== null && proof.expires > Date.now() ? { [DIRECTORY_PROOF_HEADER]: proof.token } : {},
  );
  const now = useNow(10_000);

  const view = form.view;
  const values = form.values;

  // The names the directory has for the groups of the saved map.
  const stored = view?.values ?? null;
  const storedReady =
    stored !== null &&
    stored.enabled &&
    (stored.host ?? "").trim() !== "" &&
    (stored.baseDn ?? "").trim() !== "";
  const directory = useQuery({
    ...directoryQuery,
    enabled: storedReady && Object.keys(stored.groupRoleMap).length > 0,
  });
  const [found, setFound] = useState<Map<string, string | null>>(new Map());

  if (view === null || values === null) {
    return form.query.isError ? (
      <Notice tone="fail" title={<Trans>The directory settings could not be loaded.</Trans>}>
        {form.query.error.message}
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  const mapLock = form.lockOf("groupRoleMap");
  const map = values.groupRoleMap;

  const directoryNames = new Map<string, string | null>(
    directory.data !== undefined &&
      directory.data.enabled &&
      (directory.data.host ?? "") !== "" &&
      (directory.data.baseDn ?? "") !== ""
      ? directory.data.groupRoleMap.map((entry) => [entry.group.toLowerCase(), entry.name])
      : [],
  );

  const describe = (key: string): ReactNode => {
    const lower = key.trim().toLowerCase();

    if (found.has(lower) || !directoryNames.has(lower)) {
      const name = found.get(lower) ?? null;

      return name === null ? null : <span className="type-label text-ink">{name}</span>;
    }

    const name = directoryNames.get(lower) ?? null;

    return name === null ? (
      <span className="type-label text-attention-text">
        <Trans>Not found in the directory</Trans>
      </span>
    ) : (
      <span className="type-label text-ink">{name}</span>
    );
  };

  const needsProof = needsDirectoryProof(
    me.source === "Directory",
    view.values,
    values,
    form.secrets,
  );
  const proofForThese = proof !== null && proofFits(proof, values, form.secrets);

  return (
    <SettingsSection
      form={form}
      canChange
      title={<Trans>Directory</Trans>}
      description={
        <Trans>
          People sign in with the user name and password of their directory account, such as one of
          Active Directory. DDT checks the password with the directory at each sign-in and takes
          over the name, the email address and, through the group map, the role. Changes apply at
          the next sign-in.
        </Trans>
      }
    >
      <SettingSwitch
        form={form}
        field="enabled"
        canChange
        label={<Trans>Let people sign in with their directory account</Trans>}
      />

      <SettingsGroup title={<Trans>Server</Trans>}>
        <div className="grid gap-4 md:grid-cols-2">
          <SettingText
            form={form}
            field="host"
            canChange
            label={<Trans>Directory server</Trans>}
            hint={<Trans>A domain controller or the domain's name.</Trans>}
            placeholder="dc1.corp.example"
            mono
          />
          <PortField form={form} />
          <SettingSelect
            form={form}
            field="transport"
            canChange
            label={<Trans>Encryption</Trans>}
            options={[
              {
                id: "Ldaps",
                label: t`LDAPS`,
                description: <Trans>Encrypted from the first byte.</Trans>,
              },
              {
                id: "StartTls",
                label: t`StartTLS`,
                description: <Trans>Starts plain and encrypts before anything is sent.</Trans>,
              },
              {
                id: "UnencryptedDangerous",
                label: t`Unencrypted (dangerous)`,
                description: <Trans>Passwords cross the network in clear text.</Trans>,
              },
            ]}
          />
          <TimeoutField form={form} />
        </div>
        {values.transport === "UnencryptedDangerous" ? (
          <Notice tone="fail" title={<Trans>Passwords cross the network in clear text</Trans>}>
            <Trans>
              The bind password and every password typed at sign-in can be read by anyone on the way
              to the directory. Use this only on a test network; a save asks you to confirm it.
            </Trans>
          </Notice>
        ) : null}
      </SettingsGroup>

      <SettingsGroup title={<Trans>Bind account</Trans>}>
        <p className="type-small text-ink-2">
          <Trans>
            DDT reads the directory with this account: it finds the user who signs in, and their
            groups. A saved password goes only to the server it was entered for, so after a change
            of the server, port or encryption, enter it again.
          </Trans>
        </p>
        <div className="grid gap-4 md:grid-cols-2">
          <SettingText
            form={form}
            field="bindDn"
            canChange
            label={<Trans>Account</Trans>}
            placeholder="CN=ddt-bind,OU=Service accounts,DC=corp,DC=example"
            mono
          />
          <SettingSecret
            form={form}
            field="bindPassword"
            canChange
            label={<Trans>Password</Trans>}
          />
        </div>
      </SettingsGroup>

      <SettingsGroup title={<Trans>Users</Trans>}>
        <div className="grid gap-4 md:grid-cols-2">
          <SettingText
            form={form}
            field="baseDn"
            canChange
            label={<Trans>Search base</Trans>}
            hint={<Trans>Where DDT looks for users and groups.</Trans>}
            placeholder="DC=corp,DC=example"
            mono
          />
          <SettingText
            form={form}
            field="userFilter"
            canChange
            label={<Trans>User filter</Trans>}
            hint={
              <Trans>
                Finds the user who signs in. It must contain {USER_TOKEN}, which DDT replaces with
                the user name typed at sign-in.
              </Trans>
            }
            placeholder="(&(objectClass=user)(sAMAccountName={0}))"
            mono
          />
          <SettingText
            form={form}
            field="immutableIdAttribute"
            canChange
            label={<Trans>Attribute that identifies an account</Trans>}
            hint={
              <Trans>
                DDT keys each directory account on it. With another attribute, every account is
                taken for a new one at its next sign-in.
              </Trans>
            }
            placeholder="objectGUID"
            mono
          />
          <SettingText
            form={form}
            field="displayNameAttribute"
            canChange
            label={<Trans>Name attribute</Trans>}
            placeholder="displayName"
            mono
          />
          <SettingText
            form={form}
            field="emailAttribute"
            canChange
            label={<Trans>Email attribute</Trans>}
            placeholder="mail"
            mono
          />
        </div>
      </SettingsGroup>

      <SettingsGroup title={<Trans>Groups and roles</Trans>}>
        <p className="max-w-[80ch] type-small text-ink-2">
          <Trans>
            At each sign-in, a directory account gets the highest role its groups below give it, and
            an account in none of them cannot sign in. While no group is listed, administrators
            choose the role of each directory account on the Users and roles page.
          </Trans>
        </p>
        <SettingSwitch
          form={form}
          field="resolveNestedGroups"
          canChange
          label={<Trans>Read the account's groups, nested groups included</Trans>}
          hint={<Trans>The group map needs it: while it is off, DDT reads no groups at all.</Trans>}
        />
        <RoleMapEditor
          label={t`Groups and the roles they give`}
          map={map}
          onChange={(next) => {
            form.change("groupRoleMap", next);
          }}
          canChange={mapLock === null}
          errorsOf={(key) => form.fieldErrors(`groupRoleMap[${key}]`)}
          describe={describe}
          empty={<Trans>No group is listed, so groups decide no role.</Trans>}
          addLabel={<Trans>Add a group by its distinguished name</Trans>}
          addHint={<Trans>A group added here gives Viewer until you choose its role.</Trans>}
          addPlaceholder="CN=DDT Admins,OU=Groups,DC=corp,DC=example"
          addAction={<Trans>Add group</Trans>}
          duplicate={t`This group is listed already.`}
        />
        {mapLock !== null ? <LockNote lock={mapLock} /> : null}
        {form.fieldErrors("groupRoleMap").length > 0 ? (
          <span className="type-small text-fail-text">
            {form.fieldErrors("groupRoleMap").join(" ")}
          </span>
        ) : null}
        {mapLock === null ? (
          <GroupFinder
            ready={storedReady}
            unsaved={connectionChanged(view.values, values, form.secrets)}
            map={map}
            onAdd={(group, name) => {
              setFound((current) => new Map(current).set(group.toLowerCase(), name));
              form.change("groupRoleMap", { ...map, [group]: "Viewer" });
            }}
          />
        ) : null}
      </SettingsGroup>

      <SettingsGroup title={<Trans>Test</Trans>}>
        <DirectoryTest
          form={form}
          me={me}
          proof={proofForThese && proof.expires > now ? proof : null}
          onProof={setProof}
        />
      </SettingsGroup>

      {needsProof && !(proofForThese && proof.expires > now) ? (
        <Notice tone="attention">
          {proofForThese ? (
            <Trans>
              Your test of these values is more than 5 minutes old. Test your sign-in again before
              you save.
            </Trans>
          ) : (
            <Trans>
              You sign in through the directory, and these changes decide whether you still can.
              Test your own sign-in with them above before you save; the save is accepted within 5
              minutes of a test that kept you an administrator.
            </Trans>
          )}
        </Notice>
      ) : null}
    </SettingsSection>
  );
}

// A port reads better without the grouping a number field adds, as in 3269 for the global catalog.
function PortField({ form }: { form: SettingsForm<LdapSettings> }) {
  const port = form.values?.port;
  const errors = form.fieldErrors("port");
  const lock = form.lockOf("port");

  return (
    <div className="flex flex-col gap-1">
      <NumberField
        label={<Trans>Port</Trans>}
        hint={<Trans>LDAPS uses 636, StartTLS and unencrypted 389.</Trans>}
        minValue={1}
        maxValue={65535}
        formatOptions={{ useGrouping: false }}
        value={typeof port === "number" ? port : Number.NaN}
        onChange={(next) => {
          if (!Number.isNaN(next)) {
            form.change("port", next);
          }
        }}
        isReadOnly={lock !== null}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
        className="max-w-60"
      />
      {lock === null ? null : <LockNote lock={lock} />}
    </div>
  );
}

// The timeout is a duration on the server, and whole seconds here.
function TimeoutField({ form }: { form: SettingsForm<LdapSettings> }) {
  const seconds = secondsOf(form.values?.timeout ?? null);
  const errors = form.fieldErrors("timeout");
  const lock = form.lockOf("timeout");

  return (
    <div className="flex flex-col gap-1">
      <NumberField
        label={<Trans>Timeout in seconds</Trans>}
        hint={<Trans>How long DDT waits for each answer of the directory.</Trans>}
        minValue={1}
        formatOptions={{ useGrouping: false }}
        value={seconds ?? Number.NaN}
        onChange={(next) => {
          if (!Number.isNaN(next)) {
            form.change("timeout", spanOf(next));
          }
        }}
        isReadOnly={lock !== null}
        isInvalid={errors.length > 0}
        errorMessage={errors.join(" ")}
        className="max-w-60"
      />
      {lock === null ? null : <LockNote lock={lock} />}
    </div>
  );
}

// Groups found by name, to add to the map. The server searches the directory as the section is saved.
function GroupFinder({
  ready,
  unsaved,
  map,
  onAdd,
}: {
  ready: boolean;
  unsaved: boolean;
  map: Record<string, string>;
  onAdd: (group: string, name: string | null) => void;
}) {
  const { t } = useLingui();
  const [query, setQuery] = useState("");
  const [asked, setAsked] = useState("");

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setAsked(query.trim());
    }, SEARCH_DELAY_MS);

    return () => {
      window.clearTimeout(timer);
    };
  }, [query]);

  const groups = useQuery({
    queryKey: ["directory", "groups", asked],
    queryFn: () => findGroups(asked),
    enabled: ready && asked !== "",
    retry: false,
    staleTime: 60_000,
  });

  if (!ready) {
    return (
      <p className="type-small text-muted">
        <Trans>
          Once directory sign-in is on and its server and search base are saved, you can find groups
          by name here. Until then, add a group by its distinguished name.
        </Trans>
      </p>
    );
  }

  return (
    <section className="flex flex-col gap-3">
      <h4 className="type-label text-ink">
        <Trans>Find a group</Trans>
      </h4>
      <SearchField
        label={t`Find a directory group`}
        placeholder={t`Group name`}
        value={query}
        onChange={setQuery}
        className="w-full max-w-120"
      />
      {unsaved ? (
        <p className="type-small text-attention-text">
          <Trans>
            The search asks the directory as it is saved, not with the changes above that are not
            saved yet.
          </Trans>
        </p>
      ) : null}
      {asked === "" ? null : groups.isPending ? (
        <Skeleton className="h-5 w-2/3" />
      ) : groups.isError ? (
        <Notice tone="fail">{groups.error.message}</Notice>
      ) : groups.data.length === 0 ? (
        <p className="type-small text-muted">
          <Trans>No group matches.</Trans>
        </p>
      ) : (
        <ul
          aria-label={t`Directory groups found`}
          className="flex max-h-96 flex-col divide-y divide-line-soft overflow-auto rounded-key bg-well"
        >
          {groups.data.map((group) => {
            const dn = group.distinguishedName;
            const shown = group.name ?? dn;
            const role = entryOf(map, dn);
            const label = role === undefined ? null : roleLabel(role);

            return (
              <li key={dn} className="flex items-center gap-3 px-3 py-2">
                <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                  <span className="truncate type-label text-ink">{shown}</span>
                  <span className="type-data text-[12.5px] break-all text-muted select-all">
                    {dn}
                  </span>
                  {group.description !== null && group.description !== "" ? (
                    <span className="type-small text-ink-2">{group.description}</span>
                  ) : null}
                </span>
                {label !== null ? (
                  <span className="shrink-0 type-small text-ink-2">
                    <Trans>Gives {label}</Trans>
                  </span>
                ) : (
                  <Button
                    size="sm"
                    aria-label={t`Add ${shown} to the map`}
                    onPress={() => {
                      onAdd(dn, group.name);
                    }}
                  >
                    <Trans>Add</Trans>
                  </Button>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}

// Tries the values in the form before they are saved: the bind, and with a user name and password, that user's
// sign-in without a session. A test of one's own sign-in that keeps one an administrator hands back the proof a
// directory administrator's save needs.
function DirectoryTest({
  form,
  me,
  proof,
  onProof,
}: {
  form: SettingsForm<LdapSettings>;
  me: CurrentUser;
  proof: DirectoryProof | null;
  onProof: (proof: DirectoryProof) => void;
}) {
  const [userName, setUserName] = useState(me.source === "Directory" ? me.userName : "");
  const [password, setPassword] = useState("");

  const test = useMutation({
    mutationFn: (tested: { values: LdapSettings; secrets: Record<string, SecretAction> }) =>
      testLdap({
        ...tested,
        userName: userName.trim() === "" ? null : userName.trim(),
        password: password === "" ? null : password,
      }),
    onSuccess: (result, tested) => {
      setPassword("");

      if (result.proof !== null) {
        onProof({
          token: result.proof,
          values: tested.values,
          bindPassword: tested.secrets.bindPassword ?? { action: "Keep" },
          expires: Date.now() + DIRECTORY_PROOF_LIFETIME_MS,
        });
      }
    },
  });

  return (
    <div className="flex flex-col gap-3">
      <p className="max-w-[80ch] type-small text-ink-2">
        <Trans>
          Tries the values above as they are, before you save them. With a user name and password,
          DDT also signs that user in, without starting a session, and shows their groups and the
          role they would get. A wrong password counts towards the account's lockout.
        </Trans>
      </p>
      <form
        className="flex flex-wrap items-start gap-3"
        onSubmit={(event) => {
          event.preventDefault();

          if (form.values !== null) {
            test.mutate({ values: form.values, secrets: form.secrets });
          }
        }}
      >
        <TextField
          label={<Trans>User name, optional</Trans>}
          autoComplete="off"
          spellCheck="false"
          value={userName}
          onChange={setUserName}
          className="w-60"
        />
        <TextField
          label={<Trans>Password, optional</Trans>}
          type="password"
          autoComplete="off"
          value={password}
          onChange={setPassword}
          className="w-60"
        />
        <Button type="submit" isDisabled={test.isPending} className="mt-6.5">
          <Trans>Test the directory</Trans>
        </Button>
      </form>
      {test.isError ? <Notice tone="fail">{test.error.message}</Notice> : null}
      {test.isSuccess ? (
        <TestResult
          result={test.data}
          map={test.variables.values.groupRoleMap}
          proven={proof !== null && proof.token === test.data.proof}
        />
      ) : null}
    </div>
  );
}

function TestResult({
  result,
  map,
  proven,
}: {
  result: LdapTestResult;
  map: Record<string, string>;
  proven: boolean;
}) {
  const { t } = useLingui();
  const decides = Object.keys(map).length > 0;
  const count = result.groups.length;
  const role = result.role === null ? null : roleLabel(result.role);

  const yesNo = (value: boolean, yes: string, no: string) => (
    <StateTag tone={value ? "ok" : "fail"}>{value ? yes : no}</StateTag>
  );

  return (
    <div className="flex flex-col gap-3 rounded-key bg-well p-3.5">
      <Facts
        items={[
          {
            label: <Trans>Bind account</Trans>,
            value: yesNo(result.bound, t`Signed in`, t`Failed`),
          },
          ...(result.userFound === null
            ? []
            : [
                {
                  label: <Trans>User</Trans>,
                  value: yesNo(result.userFound, t`Found`, t`Not found`),
                },
              ]),
          ...(result.passwordAccepted === null
            ? []
            : [
                {
                  label: <Trans>Password</Trans>,
                  value: yesNo(result.passwordAccepted, t`Accepted`, t`Refused`),
                },
              ]),
          ...(result.userFound === true
            ? [
                {
                  label: <Trans>Role at sign-in</Trans>,
                  value:
                    role !== null ? (
                      <span className="type-label">{role}</span>
                    ) : decides ? (
                      <span className="text-attention-text">
                        <Trans>None, so the sign-in is refused</Trans>
                      </span>
                    ) : (
                      <span className="text-muted">
                        <Trans>Chosen on the Users and roles page</Trans>
                      </span>
                    ),
                },
                {
                  label: <Trans>Groups</Trans>,
                  value:
                    count === 0 ? (
                      <span className="text-muted">
                        <Trans>None</Trans>
                      </span>
                    ) : (
                      <details>
                        <summary className="cursor-pointer text-ink-2">
                          {plural(count, { one: "# group", other: "# groups" })}
                        </summary>
                        <ul className="mt-1 flex flex-col gap-0.5">
                          {result.groups.map((group) => (
                            <GroupLine key={group} group={group} role={entryOf(map, group)} />
                          ))}
                        </ul>
                      </details>
                    ),
                },
              ]
            : []),
        ]}
      />
      <p className="type-small text-ink">{result.message}</p>
      {proven ? (
        <p className="type-small text-ok-text">
          <Trans>
            Your sign-in with these values keeps you an administrator. Save them within 5 minutes.
          </Trans>
        </p>
      ) : null}
    </div>
  );
}

function GroupLine({ group, role }: { group: string; role: string | undefined }) {
  const label = role === undefined ? null : roleLabel(role);

  return (
    <li className="type-data text-[12.5px] break-all text-muted">
      {group}
      {label === null ? null : (
        <span className="ml-2 font-sans type-small text-ink">
          <Trans>gives {label}</Trans>
        </span>
      )}
    </li>
  );
}
