// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { ApplyImageFields } from "./ApplyImageFields";
import { InjectDriversFields } from "./InjectDriversFields";
import { JoinDomainFields } from "./JoinDomainFields";
import type { KindFieldsProps } from "./kindFields";
import { PartitionFields } from "./PartitionFields";
import { RebootFields } from "./RebootFields";
import { RunScriptFields } from "./RunScriptFields";
import { WriteUnattendFields } from "./WriteUnattendFields";

// The fields of the step's own kind.
export function StepFields({ step, ...rest }: KindFieldsProps<SequenceStep>) {
  switch (step.kind) {
    case "partition":
      return <PartitionFields step={step} {...rest} />;
    case "applyImage":
      return <ApplyImageFields step={step} {...rest} />;
    case "injectDrivers":
      return <InjectDriversFields step={step} {...rest} />;
    case "writeUnattend":
      return <WriteUnattendFields step={step} {...rest} />;
    case "joinDomain":
      return <JoinDomainFields step={step} {...rest} />;
    case "runScript":
      return <RunScriptFields step={step} {...rest} />;
    case "reboot":
      return <RebootFields step={step} {...rest} />;
  }
}
