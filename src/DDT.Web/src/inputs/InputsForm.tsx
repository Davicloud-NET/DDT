// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { prefillText, type ResolvedValue } from "@/values/values";

import { AccountField } from "./fields/AccountField";
import { ChoiceField } from "./fields/ChoiceField";
import type { InputFieldProps } from "./fields/inputField";
import { MultiChoiceField } from "./fields/MultiChoiceField";
import { TextInputField } from "./fields/TextInputField";
import { YesNoField } from "./fields/YesNoField";
import { errorFor, prefilledBy, type AnswerDraft, type AskedInput } from "./inputs";
import type { Answers } from "./useAnswers";

const emptyDraft: AnswerDraft = { value: "", values: [], userName: "", password: "" };

// The fields for a sequence's inputs, by kind. A field says where its first value came from and what the page or the
// server refused. A password is never shown again.
export function InputsForm({
  inputs,
  answers,
  defaults = [],
  errors = {},
}: {
  inputs: readonly AskedInput[];
  answers: Answers;
  defaults?: readonly ResolvedValue[];
  // The server's refusals by input name.
  errors?: Record<string, string>;
}) {
  return (
    <div className="flex flex-col gap-4">
      {inputs.map((input) => {
        const prefilled = prefilledBy(input, defaults);
        const hint = [input.help, prefilled === null ? null : prefillText(prefilled)]
          .filter((part): part is string => part !== null && part !== "")
          .join(" ");

        return (
          <InputField
            key={input.name}
            input={input}
            draft={answers.drafts[input.name] ?? emptyDraft}
            hint={hint === "" ? null : hint}
            error={errorFor(answers.missing, input.name) ?? errorFor(errors, input.name)}
            onChange={(draft) => {
              answers.edit(input.name, draft);
            }}
          />
        );
      })}
    </div>
  );
}

function InputField(props: InputFieldProps) {
  switch (props.input.kind) {
    case "Text":
      return <TextInputField {...props} />;
    case "Choice":
      return <ChoiceField {...props} />;
    case "MultiChoice":
      return <MultiChoiceField {...props} />;
    case "YesNo":
      return <YesNoField {...props} />;
    case "Account":
      return <AccountField {...props} />;
  }
}
