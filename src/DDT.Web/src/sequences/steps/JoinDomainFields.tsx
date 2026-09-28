// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { useContext } from "react";

import { AccountSetting } from "../builder/AccountSetting";
import { TemplateField } from "../builder/TemplateField";
import { EditorLock } from "../editorLock";
import type { JoinDomainStep } from "../sequences";
import { DomainJoinCheck } from "./DomainJoinCheck";
import { orNull, type KindFieldsProps } from "./kindFields";

export function JoinDomainFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<JoinDomainStep>) {
  const locked = useContext(EditorLock);
  const own = (step.account ?? null) !== null;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        {own ? (
          <Trans>
            Joins the domain of the account chosen below, in Windows after the hand-over. The
            machine needs a computer name.
          </Trans>
        ) : catalog.domainConfigured === false ? (
          <Trans>No domain is set on the Deployment defaults page, so this step cannot run.</Trans>
        ) : (
          <Trans>
            Joins the domain set on the Deployment defaults page, in Windows after the hand-over.
            The machine needs a computer name.
          </Trans>
        )}
      </p>
      <AccountSetting
        label={<Trans>Join with</Trans>}
        field="account"
        findings={findings}
        className="sm:col-span-2"
        use="join"
        noneLabel={t`The join account on the Deployment defaults page`}
        hint={<Trans>The account also names the domain the machine joins.</Trans>}
        value={step.account ?? null}
        onChange={(account) => {
          onChange({ account }, true);
        }}
      />
      <TemplateField
        label={<Trans>Organizational unit</Trans>}
        field="organizationalUnit"
        findings={findings}
        className="sm:col-span-2"
        hint={
          <Trans>
            Such as OU=Workstations,DC=example,DC=com. Empty takes the default from the Deployment
            defaults page.
          </Trans>
        }
        placeholder={t`The default`}
        howTo={false}
        value={step.organizationalUnit ?? ""}
        onChange={(text) => {
          onChange({ organizationalUnit: orNull(text) });
        }}
      />
      {catalog.domainConfigured === true && !locked && !own ? (
        <DomainJoinCheck
          organizationalUnit={step.organizationalUnit ?? null}
          className="sm:col-span-2"
        />
      ) : null}
    </>
  );
}
