// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";
import type { ReactNode } from "react";

import { relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Button } from "@/ui/Button";
import { Notice } from "@/ui/Notice";
import { Panel } from "@/ui/Panel";

import type { SettingsForm } from "../useSettingsForm";

import { ApplyStates } from "./ApplyStates";
import { ReauthDialog } from "./ReauthDialog";
import { SectionErrors } from "./SectionErrors";
import { WarningsDialog } from "./WarningsDialog";

// A section of settings as a panel, with a bar that saves or discards it and says who saved last. A section that
// rebuilds a subsystem also shows how far each host has applied it.
export function SettingsSection<T>({
  form,
  title,
  description,
  children,
  canChange,
}: {
  form: SettingsForm<T>;
  title: ReactNode;
  description?: ReactNode;
  children: ReactNode;
  canChange: boolean;
}) {
  const view = form.view;

  return (
    <Panel title={title}>
      {description ? <p className="max-w-[80ch] text-ink-2">{description}</p> : null}

      {form.query.isError ? (
        <Notice tone="fail">
          <Trans>These settings could not be loaded.</Trans>
        </Notice>
      ) : null}

      {form.changedElsewhere ? (
        <ShowTheirs form={form}>
          <Trans>Someone else saved these settings while you were changing them.</Trans>
        </ShowTheirs>
      ) : null}

      <div className="flex flex-col gap-4">{children}</div>

      {form.refusal?.kind === "stale" ? (
        <ShowTheirs form={form}>{form.refusal.message}</ShowTheirs>
      ) : null}
      {form.refusal?.kind === "other" ? <Notice tone="fail">{form.refusal.message}</Notice> : null}
      <SectionErrors form={form} />

      {view?.apply !== null && view?.apply !== undefined && view.apply.length > 0 ? (
        <ApplyStates states={view.apply} version={view.version} />
      ) : null}

      <div className="-mx-4 -mb-4 flex flex-wrap items-center gap-3 border-t border-line-soft px-4 py-3">
        <SavedNote form={form} />
        {canChange ? (
          <>
            <Button isDisabled={!form.dirty || form.saving} onPress={form.discard}>
              <Trans>Discard</Trans>
            </Button>
            <Button
              variant="primary"
              isDisabled={!form.dirty || form.saving}
              onPress={() => {
                form.save();
              }}
            >
              <Trans>Save</Trans>
            </Button>
          </>
        ) : null}
      </div>

      <WarningsDialog form={form} />
      <ReauthDialog
        isOpen={form.needsReauth}
        onAccepted={form.retryAfterReauth}
        onCancel={form.cancelReauth}
      />
    </Panel>
  );
}

// Another administrator saved the section meanwhile. The user can drop their draft and show that save instead.
function ShowTheirs<T>({ form, children }: { form: SettingsForm<T>; children: ReactNode }) {
  return (
    <Notice tone="attention">
      <span className="flex flex-wrap items-center gap-3">
        {children}
        <Button size="sm" onPress={form.discard}>
          <Trans>Show theirs</Trans>
        </Button>
      </span>
    </Notice>
  );
}

function SavedNote<T>({ form }: { form: SettingsForm<T> }) {
  const now = useNow(30_000);
  const view = form.view;
  const savedBy = view?.updatedBy ?? null;
  const savedWhen =
    view?.updatedUtc === null || view?.updatedUtc === undefined
      ? null
      : relativeTime(view.updatedUtc, now);

  return (
    <span className="flex-1 type-small text-muted">
      {savedWhen === null ? (
        <Trans>Never changed on this page.</Trans>
      ) : savedBy === null ? (
        <Trans>Saved {savedWhen}.</Trans>
      ) : (
        <Trans>
          Saved {savedWhen} by {savedBy}.
        </Trans>
      )}
    </span>
  );
}
