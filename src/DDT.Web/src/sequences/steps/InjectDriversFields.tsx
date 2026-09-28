// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural, t } from "@lingui/core/macro";
import { Trans } from "@lingui/react/macro";
import { Link } from "@tanstack/react-router";

import { FlagSetting } from "../fields/FlagSetting";
import type { InjectDriversStep } from "../sequences";
import type { KindFieldsProps } from "./kindFields";
import { STEP_LINK_CLASS } from "./stepLinkClass";

export function InjectDriversFields({
  step,
  findings,
  catalog,
  onChange,
}: KindFieldsProps<InjectDriversStep>) {
  const count = catalog.packages.filter((item) => item.kind === "Drivers").length;

  return (
    <>
      <p className="text-ink-2 sm:col-span-2">
        <Trans>
          Adds the driver packages whose targets match the machine's model to the applied image. The
          packages are chosen when the sequence is assigned.
        </Trans>
      </p>
      <p className="type-small text-ink-2 sm:col-span-2">
        {count === 0
          ? t`No driver package is in the library yet.`
          : plural(count, {
              one: "# driver package is in the library, for the models its targets name.",
              other: "# driver packages are in the library, each for the models its targets name.",
            })}{" "}
        <Link to="/library/drivers" className={STEP_LINK_CLASS}>
          <Trans>Driver packages and their targets are under Drivers.</Trans>
        </Link>
      </p>
      <FlagSetting
        label={<Trans>Fail when no driver package matches the model</Trans>}
        field="requireMatch"
        findings={findings}
        className="sm:col-span-2"
        hint={<Trans>Otherwise the step is done without adding drivers.</Trans>}
        value={step.requireMatch}
        onChange={(requireMatch) => {
          onChange({ requireMatch });
        }}
      />
    </>
  );
}
