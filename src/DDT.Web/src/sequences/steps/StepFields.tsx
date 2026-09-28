// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import type { SequenceStep } from "../sequences";
import { PauseFields, SetVariableFields } from "./FlowFields";
import type { KindFieldsProps } from "./kindFields";
import { WriteCloudInitSeedFields, WriteRawImageFields } from "./RawImageFields";
import { RebootFields, RunScriptFields } from "./ScriptFields";
import {
  ApplyImageFields,
  InjectDriversFields,
  JoinDomainFields,
  PartitionFields,
  WriteUnattendFields,
} from "./WindowsFields";

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
    case "writeRawImage":
      return <WriteRawImageFields step={step} {...rest} />;
    case "writeCloudInitSeed":
      return <WriteCloudInitSeedFields step={step} {...rest} />;
    case "setVariable":
      return <SetVariableFields step={step} {...rest} />;
    case "pause":
      return <PauseFields step={step} {...rest} />;
    // A container's own fields are the inspector's.
    case "group":
    case "if":
    case "repeat":
      return null;
  }
}
