// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Facts } from "@/ui/Facts";
import { ResultBox } from "@/ui/ResultBox";
import { StateTag } from "@/ui/StateTag";

import { settingsText } from "../settings";
import type { LdapTestResult } from "../signIn";

import { RoleAtSignIn } from "./RoleAtSignIn";
import { TestedGroups } from "./TestedGroups";

// A directory test's answer. map is the tested one, which decides the role.
export function TestResult({
  result,
  map,
  proven,
}: {
  result: LdapTestResult;
  map: Record<string, string>;
  proven: boolean;
}) {
  const { t } = useLingui();

  const yesNo = (value: boolean, yes: string, no: string) => (
    <StateTag tone={value ? "ok" : "fail"}>{value ? yes : no}</StateTag>
  );

  return (
    <ResultBox>
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
                  value: <RoleAtSignIn role={result.role} decides={Object.keys(map).length > 0} />,
                },
                {
                  label: <Trans>Groups</Trans>,
                  value: <TestedGroups groups={result.groups} map={map} />,
                },
              ]
            : []),
        ]}
      />
      <p className="type-small text-ink">{settingsText(result)}</p>
      {proven ? (
        <p className="type-small text-ok-text">
          <Trans>
            Your sign-in with these values keeps you an administrator. Save them within 5 minutes.
          </Trans>
        </p>
      ) : null}
    </ResultBox>
  );
}
