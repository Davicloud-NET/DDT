// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconDots } from "@tabler/icons-react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Button as AriaButton, MenuTrigger } from "react-aria-components";

import { currentUserQuery, type CurrentUser } from "@/auth/auth";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { liveListOptions } from "@/live/freshness";
import { useLiveMarks } from "@/live/useLiveMarks";
import { useLiveStatus } from "@/live/useLiveStatus";
import { removeTokensOf } from "@/tokens/tokens";
import { Button } from "@/ui/Button";
import { SearchField } from "@/ui/Controls";
import { ConfirmDialog } from "@/ui/Dialog";
import { EmptyState, Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Menu, MenuItem } from "@/ui/Menu";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { Table, TableBody, TableCell, TableColumn, TableHeader, TableRow } from "@/ui/Table";

import { DirectoryPanel } from "./DirectoryPanel";
import { AddUserDialog, ChangeUserDialog, PasswordDialog } from "./UserDialogs";
import {
  deleteUser,
  patchUser,
  removeUsers,
  resetPassword,
  resetTwoFactor,
  setUserDisabled,
  upsertUser,
  usersQuery,
  type UserView,
} from "./users";
import {
  isLockedOut,
  matchesUser,
  roleLabel,
  roleOrigin,
  shownName,
  sourceLabel,
  userDeletionConsequence,
} from "./userView";

type Confirmation = "disable" | "reset-password" | "reset-two-factor" | "delete";

// Administration > Users and roles: every account DDT knows, with what it may do and where that comes from, and the
// directory's group map. Only administrators reach it; the server refuses everyone else.
export function UsersPage() {
  const me = useQuery(currentUserQuery).data ?? null;

  if (me === null) {
    return null;
  }

  if (!me.roles.includes("Administrator")) {
    return (
      <Page>
        <PageHeader title={<Trans>Users and roles</Trans>} />
        <Notice>
          <Trans>
            Only administrators manage accounts. Your own account is on the Account and security
            page in the account menu.
          </Trans>
        </Notice>
      </Page>
    );
  }

  return <UserAdministration me={me} />;
}

function UserAdministration({ me }: { me: CurrentUser }) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const live = useLiveStatus();
  const users = useQuery({ ...usersQuery, ...liveListOptions(live) });
  const now = useNow(30_000);
  // An account that appears enters; one whose role, sign-in or state changes flashes, as after an action here.
  const mark = useLiveMarks({
    queryKey: usersQuery.queryKey,
    items: (list) => list,
    id: (user) => user.id,
    signature: (user) =>
      [
        user.role,
        user.disabled,
        user.lockedOutUntil,
        user.mustChangePassword,
        user.twoFactorEnabled,
      ].join("|"),
    tone: () => "idle",
  });
  const [query, setQuery] = useState("");
  const [adding, setAdding] = useState(false);
  const [changing, setChanging] = useState<UserView | null>(null);
  const [confirming, setConfirming] = useState<{ kind: Confirmation; user: UserView } | null>(null);
  const [password, setPassword] = useState<{
    name: string;
    password: string;
    reset: boolean;
  } | null>(null);

  // Every action patches the list with the server's answer; the hub brings the same change to other pages.
  const act = useMutation({
    mutationFn: async ({ kind, user }: { kind: Confirmation; user: UserView }) => {
      switch (kind) {
        case "disable":
          upsertUser(queryClient, await setUserDisabled(user.id, true));
          break;
        case "reset-two-factor":
          upsertUser(queryClient, await resetTwoFactor(user.id));
          break;
        case "reset-password": {
          const reset = await resetPassword(user.id);

          // The answer is only the password; the reset also ends a lockout and asks for a new password.
          patchUser(queryClient, user.id, { mustChangePassword: true, lockedOutUntil: null });
          setPassword({ name: shownName(user), password: reset.password, reset: true });
          break;
        }
        case "delete":
          await deleteUser(user.id);
          removeUsers(queryClient, [user.id]);
          removeTokensOf(queryClient, [user.id]);
          break;
      }
    },
    onSuccess: () => {
      setConfirming(null);
    },
  });

  const enable = useMutation({
    mutationFn: (user: UserView) => setUserDisabled(user.id, false),
    onSuccess: (saved) => {
      upsertUser(queryClient, saved);
    },
  });

  const all = users.data ?? [];
  const needle = query.trim().toLowerCase();
  const shown = needle === "" ? all : all.filter((user) => matchesUser(user, needle));

  function confirm(kind: Confirmation, user: UserView) {
    act.reset();
    setConfirming({ kind, user });
  }

  return (
    <Page>
      <PageHeader title={<Trans>Users and roles</Trans>}>
        <div className="flex-1" />
        {all.length > 0 ? (
          <SearchField
            label={t`Find an account`}
            placeholder={t`Name, user name or email`}
            value={query}
            onChange={setQuery}
          />
        ) : null}
        <Button
          variant="primary"
          onPress={() => {
            setAdding(true);
          }}
        >
          <Trans>Add account</Trans>
        </Button>
      </PageHeader>

      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          An administrator changes everything, including the accounts here; an operator approves
          machines and starts deployments; a viewer only looks. Directory and single sign-on
          accounts appear after their first sign-in.
        </Trans>
      </p>

      {users.isError ? (
        <Notice tone="fail">
          <Trans>The accounts could not be loaded.</Trans>
        </Notice>
      ) : null}
      {enable.isError ? <Notice tone="fail">{enable.error.message}</Notice> : null}

      <Panel title={<Trans>Accounts</Trans>} flush>
        {users.isPending ? (
          <div className="flex flex-col gap-3 p-4">
            <Skeleton className="h-6 w-1/2" />
            <Skeleton className="h-6 w-2/3" />
          </div>
        ) : shown.length === 0 ? (
          <EmptyState title={<Trans>No account matches</Trans>} />
        ) : (
          <Table aria-label={t`Accounts`} className="min-w-[1000px] table-fixed">
            <TableHeader>
              <TableColumn id="account" isRowHeader className="w-[22%] pl-4">
                <Trans>Account</Trans>
              </TableColumn>
              <TableColumn id="source" className="w-40">
                <Trans>Type</Trans>
              </TableColumn>
              <TableColumn id="role">
                <Trans>Role</Trans>
              </TableColumn>
              <TableColumn id="twoFactor" className="w-28">
                <Trans>Second factor</Trans>
              </TableColumn>
              <TableColumn id="state" className="w-44">
                <Trans>State</Trans>
              </TableColumn>
              <TableColumn id="signIn" className="w-32">
                <Trans>Last sign-in</Trans>
              </TableColumn>
              <TableColumn id="actions" className="w-12 pr-4">
                <span className="sr-only">
                  <Trans>Actions</Trans>
                </span>
              </TableColumn>
            </TableHeader>
            <TableBody items={shown} dependencies={[now, me.id, enable.isPending, mark]}>
              {(user) => (
                <TableRow id={user.id} textValue={user.userName} className={mark(user.id)}>
                  <TableCell className="pl-4">
                    <span className="flex min-w-0 flex-col">
                      <span className="flex min-w-0 items-baseline gap-2">
                        <span className="truncate type-label text-ink">{shownName(user)}</span>
                        {user.id === me.id ? (
                          <span className="shrink-0 type-small text-muted">
                            <Trans>you</Trans>
                          </span>
                        ) : null}
                      </span>
                      <AccountLine user={user} />
                    </span>
                  </TableCell>
                  <TableCell className="type-small">
                    <SourceCell user={user} />
                  </TableCell>
                  <TableCell className="type-small">
                    <span className="flex min-w-0 flex-col">
                      <span className={user.role === null ? "text-attention-text" : "text-ink"}>
                        {user.role === null ? <Trans>No role</Trans> : roleLabel(user.role)}
                      </span>
                      <span className="truncate text-muted">{roleOrigin(user)}</span>
                    </span>
                  </TableCell>
                  <TableCell className="type-small">
                    {user.twoFactorEnabled ? (
                      <span className="text-ink">
                        <Trans>On</Trans>
                      </span>
                    ) : (
                      <span className="text-muted">
                        <Trans>Off</Trans>
                      </span>
                    )}
                  </TableCell>
                  <TableCell>
                    <StateCell user={user} now={now} />
                  </TableCell>
                  <TableCell className="type-small whitespace-nowrap text-muted">
                    {user.lastSignInUtc === null ? (
                      <Trans>Never</Trans>
                    ) : (
                      <span title={fullTime(user.lastSignInUtc)}>
                        {relativeTime(user.lastSignInUtc, now)}
                      </span>
                    )}
                  </TableCell>
                  <TableCell className="pr-4">
                    <UserMenu
                      user={user}
                      isSelf={user.id === me.id}
                      onAction={(action) => {
                        if (action === "change") {
                          setChanging(user);
                        } else if (action === "enable") {
                          enable.mutate(user);
                        } else {
                          confirm(action, user);
                        }
                      }}
                    />
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        )}
      </Panel>

      <DirectoryPanel />

      {adding ? (
        <AddUserDialog
          onClose={() => {
            setAdding(false);
          }}
          onCreated={(created) => {
            setAdding(false);
            setPassword({
              name: shownName(created.user),
              password: created.password,
              reset: false,
            });
          }}
        />
      ) : null}

      {changing !== null ? (
        <ChangeUserDialog
          user={changing}
          isSelf={changing.id === me.id}
          onClose={() => {
            setChanging(null);
          }}
        />
      ) : null}

      <PasswordDialog
        shown={password}
        onClose={() => {
          setPassword(null);
        }}
      />

      <ConfirmDialog
        isOpen={confirming !== null}
        onOpenChange={(open) => {
          if (!open) {
            setConfirming(null);
          }
        }}
        title={confirming === null ? "" : <ConfirmTitle {...confirming} />}
        confirmLabel={confirming === null ? "" : <ConfirmLabel kind={confirming.kind} />}
        danger={confirming?.kind === "delete" || confirming?.kind === "disable"}
        isBusy={act.isPending}
        error={act.isError ? act.error.message : undefined}
        onConfirm={() => {
          if (confirming !== null) {
            act.mutate(confirming);
          }
        }}
      >
        {confirming === null ? null : <ConfirmBody {...confirming} />}
      </ConfirmDialog>
    </Page>
  );
}

// Under the shown name: the user name, when the name is not already it, and the email address.
function AccountLine({ user }: { user: UserView }) {
  const named = shownName(user) !== user.userName;

  if (!named && user.email === null) {
    return null;
  }

  return (
    <span className="truncate type-small text-muted">
      {named ? <span className="type-data text-[12.5px]">{user.userName}</span> : null}
      {named && user.email !== null ? " · " : null}
      {user.email}
    </span>
  );
}

function SourceCell({ user }: { user: UserView }) {
  const provider = user.externalProvider;

  return (
    <span className="flex min-w-0 flex-col">
      <span className="text-ink">{sourceLabel(user.source)}</span>
      {provider !== null ? (
        <span className="truncate text-muted">
          {user.source === "External" ? provider : <Trans>Linked to {provider}</Trans>}
        </span>
      ) : null}
    </span>
  );
}

function StateCell({ user, now }: { user: UserView; now: number }) {
  const { t } = useLingui();
  const tags = [];

  if (user.disabled) {
    tags.push(
      <StateTag key="disabled" tone="retired">
        <Trans>Disabled</Trans>
      </StateTag>,
    );
  }

  if (user.lockedOutUntil !== null && isLockedOut(user, now)) {
    const until = fullTime(user.lockedOutUntil);

    tags.push(
      <span key="locked" title={t`Locked until ${until}`}>
        <StateTag tone="attention">
          <Trans>Locked</Trans>
        </StateTag>
      </span>,
    );
  }

  if (user.mustChangePassword) {
    tags.push(
      <StateTag key="password" tone="attention">
        <Trans>New password due</Trans>
      </StateTag>,
    );
  }

  if (tags.length === 0) {
    return (
      <span className="type-small text-muted">
        <Trans>Active</Trans>
      </span>
    );
  }

  return <span className="flex flex-wrap gap-1.5">{tags}</span>;
}

type UserAction = "change" | "enable" | Confirmation;

// What an administrator may do to an account. The server refuses to disable, delete or reset the one asking, so those
// are not offered on the own row; that account's password and second factor are on its Account page.
function UserMenu({
  user,
  isSelf,
  onAction,
}: {
  user: UserView;
  isSelf: boolean;
  onAction: (action: UserAction) => void;
}) {
  const { t } = useLingui();
  const name = user.userName;

  return (
    <MenuTrigger>
      <AriaButton
        aria-label={t`Actions for ${name}`}
        className="flex size-7.5 cursor-pointer items-center justify-center rounded-key text-muted key-motion outline-none hover:bg-hover pressed:bg-key-quiet-pressed hover:text-ink focus-visible:outline-2 focus-visible:outline-focus"
      >
        <IconDots size={18} stroke={2} />
      </AriaButton>
      <Menu
        aria-label={t`Actions for ${name}`}
        onAction={(key) => {
          onAction(String(key) as UserAction);
        }}
      >
        <MenuItem id="change">
          <Trans>Change name, email or role</Trans>
        </MenuItem>
        {!isSelf && user.source === "Local" ? (
          <MenuItem id="reset-password">
            <Trans>Reset password</Trans>
          </MenuItem>
        ) : null}
        {!isSelf && user.twoFactorEnabled ? (
          <MenuItem id="reset-two-factor">
            <Trans>Reset second factor</Trans>
          </MenuItem>
        ) : null}
        {isSelf ? null : user.disabled ? (
          <MenuItem id="enable">
            <Trans>Enable</Trans>
          </MenuItem>
        ) : (
          <MenuItem id="disable">
            <Trans>Disable</Trans>
          </MenuItem>
        )}
        {!isSelf ? (
          <MenuItem id="delete" className="text-fail-text">
            <Trans>Delete</Trans>
          </MenuItem>
        ) : null}
      </Menu>
    </MenuTrigger>
  );
}

function ConfirmTitle({ kind, user }: { kind: Confirmation; user: UserView }) {
  const name = shownName(user);

  switch (kind) {
    case "disable":
      return <Trans>Disable {name}?</Trans>;
    case "reset-password":
      return <Trans>Give {name} a new password?</Trans>;
    case "reset-two-factor":
      return <Trans>Turn off the second factor of {name}?</Trans>;
    case "delete":
      return <Trans>Delete {name}?</Trans>;
  }
}

function ConfirmLabel({ kind }: { kind: Confirmation }) {
  switch (kind) {
    case "disable":
      return <Trans>Disable account</Trans>;
    case "reset-password":
      return <Trans>Make a new password</Trans>;
    case "reset-two-factor":
      return <Trans>Turn off second factor</Trans>;
    case "delete":
      return <Trans>Delete account</Trans>;
  }
}

function ConfirmBody({ kind, user }: { kind: Confirmation; user: UserView }) {
  const name = shownName(user);

  switch (kind) {
    case "disable":
      return (
        <p>
          <Trans>
            {name} is signed out within a minute and cannot sign in, and its API tokens stop
            working, until an administrator enables it again. Nothing is deleted.
          </Trans>
        </p>
      );
    case "reset-password":
      return (
        <p>
          <Trans>
            The current password stops working at once, and {name} is signed out within a minute.
            DDT shows the new password once; {name} replaces it at the next sign-in.
          </Trans>
        </p>
      );
    case "reset-two-factor":
      return (
        <p>
          <Trans>
            For an account that lost its authenticator. {name} is signed out within a minute and
            signs in with the password alone until it sets up an authenticator again on its Account
            page.
          </Trans>
        </p>
      );
    case "delete":
      return <p>{userDeletionConsequence(user)}</p>;
  }
}
