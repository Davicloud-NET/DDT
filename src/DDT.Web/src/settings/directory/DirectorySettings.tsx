// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import type { CurrentUser } from "@/auth/auth";
import { Notice } from "@/ui/Notice";
import { Skeleton } from "@/ui/Skeleton";

import { SettingsGroup } from "../parts/SettingsGroup";
import { SettingsSection } from "../parts/SettingsSection";
import { SettingSwitch } from "../parts/SettingSwitch";
import type { LdapSettings } from "../signIn";
import { useSettingsForm } from "../useSettingsForm";

import { BindAccountGroup } from "./BindAccountGroup";
import { DirectoryTest } from "./DirectoryTest";
import { GroupsAndRolesGroup } from "./GroupsAndRolesGroup";
import { ProofNotice } from "./ProofNotice";
import { ServerGroup } from "./ServerGroup";
import { useDirectoryGroupNames } from "./useDirectoryGroupNames";
import { useDirectoryProof } from "./useDirectoryProof";
import { UsersGroup } from "./UsersGroup";

// The section ldap: people sign in with their directory user name and password, and the group map decides their
// role.
export function DirectorySettings({ me }: { me: CurrentUser }) {
  const proof = useDirectoryProof();
  const form = useSettingsForm<LdapSettings>("ldap", proof.header);
  const view = form.view;
  const values = form.values;
  const names = useDirectoryGroupNames(view?.values ?? null);

  if (view === null || values === null) {
    return form.query.isError ? (
      <Notice tone="fail" title={<Trans>The directory settings could not be loaded.</Trans>}>
        {form.query.error.message}
      </Notice>
    ) : (
      <Skeleton className="h-64 w-full" />
    );
  }

  const status = proof.check(me.source === "Directory", view.values, values, form.secrets);

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
      <ServerGroup form={form} values={values} />
      <BindAccountGroup form={form} />
      <UsersGroup form={form} />
      <GroupsAndRolesGroup form={form} stored={view.values} values={values} names={names} />
      <SettingsGroup title={<Trans>Test</Trans>}>
        <DirectoryTest form={form} me={me} proof={status.fresh} onProof={proof.setProof} />
      </SettingsGroup>
      {status.needsProof && status.fresh === null ? (
        <ProofNotice stale={status.proofForThese} />
      ) : null}
    </SettingsSection>
  );
}
