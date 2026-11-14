// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { Dialog } from "@/ui/Dialog";
import { ListSkeleton } from "@/ui/ListSkeleton";
import { Notice } from "@/ui/Notice";
import { SearchField } from "@/ui/SearchField";
import { TextField } from "@/ui/TextField";

import { LanguageChoices } from "./LanguageChoices";
import { languagePackageName } from "./languageNames";
import { useLanguageIsoForm } from "./useLanguageIsoForm";

// The languages of a Languages and Optional Features ISO, to tick. Each becomes a Windows package with its language
// pack, language features and fonts.
export function LanguageIsoDialog({ path, onClose }: { path: string | null; onClose: () => void }) {
  const { t: translate } = useLingui();
  const form = useLanguageIsoForm(path, onClose);
  const { view, start } = form;
  const example = languagePackageName("de-DE", form.end);

  return (
    <Dialog
      isOpen={path !== null}
      onOpenChange={(open) => {
        if (!open) {
          form.close();
        }
      }}
      title={<Trans>Import languages</Trans>}
      width="lg"
      isBusy={start.isPending}
      footer={
        <>
          <Button variant="secondary" onPress={form.close}>
            <Trans>Cancel</Trans>
          </Button>
          <Button
            variant="primary"
            isDisabled={start.isPending || form.chosen.size === 0}
            onPress={() => {
              start.mutate();
            }}
          >
            <Trans>Import</Trans>
          </Button>
        </>
      }
    >
      {form.iso.isError ? <Notice tone="fail">{form.iso.error.message}</Notice> : null}
      {view === undefined ? (
        form.iso.isError ? null : (
          <ListSkeleton padded={false} />
        )
      ) : (
        <div className="flex flex-col gap-4">
          <p className="type-data text-muted">{view.path}</p>
          {view.languages.length > 8 ? (
            <SearchField
              label={translate`Find a language`}
              placeholder={translate`Name or tag`}
              value={form.query}
              onChange={form.setQuery}
              className="w-full"
            />
          ) : null}
          <LanguageChoices languages={form.shown} chosen={form.chosen} onToggle={form.toggle} />
          <TextField
            label={<Trans>End of the names</Trans>}
            hint={<Trans>For example {example}.</Trans>}
            value={form.end}
            onChange={form.setSuffix}
          />
        </div>
      )}
      {start.isError ? <Notice tone="fail">{start.error.message}</Notice> : null}
    </Dialog>
  );
}
