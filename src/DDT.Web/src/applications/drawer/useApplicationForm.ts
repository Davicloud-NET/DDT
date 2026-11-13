// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import {
  applicationEditOf,
  applicationRequestOf,
  isShownFor,
  type ApplicationEdit,
} from "../applicationEdit";
import {
  createApplication,
  saveApplication,
  upsertApplication,
  type ApplicationView,
} from "../applications";
import { useEntryForm, type EntryForm } from "./useEntryForm";

export type ApplicationForm = EntryForm<ApplicationView, ApplicationEdit>;

function send(base: ApplicationView | null, revision: number, edit: ApplicationEdit) {
  const request = applicationRequestOf(revision, edit);

  return base === null ? createApplication(request) : saveApplication(base.id, request);
}

export function useApplicationForm(options: {
  application: ApplicationView | null;
  seed: ApplicationEdit;
  onClose: () => void;
}): ApplicationForm {
  return useEntryForm({
    item: options.application,
    seed: options.seed,
    editOf: applicationEditOf,
    save: send,
    put: upsertApplication,
    isShown: (edit) => isShownFor(edit.kind),
    onClose: options.onClose,
  });
}
