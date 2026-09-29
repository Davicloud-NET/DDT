// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { FlagSetting } from "../../fields/FlagSetting";
import { TextSetting } from "../../fields/TextSetting";
import { orNull, type InputPartProps } from "./declarationFields";

// Where the account an Account input asks for may be used.
export function AccountInputFields({ input, at, findings, update }: InputPartProps) {
  const destination = input.account ?? { domain: null, hosts: [], runAs: false };

  return (
    <>
      <TextSetting
        label={<Trans>Domain it joins</Trans>}
        field={at("account.domain")}
        findings={findings}
        hint={<Trans>For a Join the domain step. Empty where it joins none.</Trans>}
        mono
        value={destination.domain ?? ""}
        onChange={(text) => {
          update({ account: { ...destination, domain: orNull(text) } });
        }}
      />
      <TextSetting
        label={<Trans>Share hosts it may connect to</Trans>}
        field={at("account.hosts")}
        findings={findings}
        hint={<Trans>Host names separated by commas, such as fs01.corp.example.</Trans>}
        mono
        value={destination.hosts.join(", ")}
        onChange={(text) => {
          update({
            account: {
              ...destination,
              hosts: text
                .split(",")
                .map((host) => host.trim())
                .filter((host) => host !== ""),
            },
          });
        }}
      />
      <FlagSetting
        label={<Trans>Scripts may run as it</Trans>}
        field={at("account.runAs")}
        findings={findings}
        value={destination.runAs}
        onChange={(runAs) => {
          update({ account: { ...destination, runAs } }, true);
        }}
      />
    </>
  );
}
