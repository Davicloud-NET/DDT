// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { answerErrors, type AskedInput } from "@/inputs/inputs";
import { InputsForm } from "@/inputs/InputsForm";
import type { Answers } from "@/inputs/useAnswers";
import type { ApiError } from "@/lib/api";
import { Notice } from "@/ui/Notice";
import type { ResolvedValue } from "@/values/values";

interface AssignAnswersProps {
  sequenceName: string;
  inputs: readonly AskedInput[];
  answers: Answers;
  defaults: readonly ResolvedValue[];
  // The server's refusal of the assignment, which may name answers.
  refused: ApiError | null;
  // The chosen sequence's inputs could not be read.
  documentFailed: boolean;
}

// What the chosen sequence asks on the web before it runs.
export function AssignAnswers({
  sequenceName,
  inputs,
  answers,
  defaults,
  refused,
  documentFailed,
}: AssignAnswersProps) {
  const { t: translate } = useLingui();

  return (
    <>
      {inputs.length > 0 ? (
        <section
          aria-label={translate`Answers for ${sequenceName}`}
          className="flex flex-col gap-3"
        >
          <h3 className="type-label text-ink">
            <Trans>What {sequenceName} asks before it runs</Trans>
          </h3>
          <InputsForm
            inputs={inputs}
            answers={answers}
            defaults={defaults}
            errors={answerErrors(refused)}
          />
        </section>
      ) : null}
      {documentFailed ? (
        <Notice tone="fail">
          <Trans>
            What {sequenceName} asks before it runs could not be loaded, so it is assigned without
            answers.
          </Trans>
        </Notice>
      ) : null}
    </>
  );
}
