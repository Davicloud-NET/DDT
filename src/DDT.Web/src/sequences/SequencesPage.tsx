// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans } from "@lingui/react/macro";

import { Button } from "@/ui/Button";
import { EmptyState } from "@/ui/EmptyState";
import { Notice } from "@/ui/Notice";
import { Page } from "@/ui/Page";

import { DeleteSequenceDialog } from "./DeleteSequenceDialog";
import { LoadingRows } from "./list/LoadingRows";
import { NoSequences } from "./list/NoSequences";
import { SequencesHeader } from "./list/SequencesHeader";
import { SequenceTable } from "./list/SequenceTable";
import { useSequencesPage } from "./list/useSequencesPage";
import { NewSequenceDialog } from "./NewSequenceDialog";
import { activeRunsOf, rulesChoosing } from "./sequenceList";

// Every task sequence, with what it does to a machine, whether it can run, and what uses it. Administrators create
// and delete sequences here. Everyone else can only look.
export function SequencesPage() {
  const page = useSequencesPage();
  const { all, ruleList, machineList, deleting, isAdministrator } = page;
  const create = () => {
    page.setCreating(true);
  };

  return (
    <Page>
      <SequencesHeader
        hasSequences={all.length > 0}
        matching={page.matching}
        filter={page.filter}
        query={page.query}
        isAdministrator={isAdministrator}
        onSearch={page.setSearch}
        onCreate={create}
      />

      <p className="max-w-[75ch] text-ink-2">
        <Trans>
          A task sequence is the list of steps a machine runs, such as partitioning the disk,
          applying an image and running scripts. One with problems is kept as a draft and cannot run
          until they are fixed.
        </Trans>
      </p>

      {page.sequences.isError ? (
        <Notice tone="fail">
          <Trans>The task sequence list could not be loaded.</Trans>
        </Notice>
      ) : null}

      <section className="overflow-hidden rounded-panel bg-panel shadow-panel">
        {page.sequences.isPending ? (
          <LoadingRows />
        ) : all.length === 0 ? (
          <NoSequences isAdministrator={isAdministrator} onCreate={create} />
        ) : page.shown.length === 0 ? (
          <EmptyState
            title={<Trans>No task sequence matches</Trans>}
            action={
              <Button
                onPress={() => {
                  page.setSearch({ state: "all", q: "" });
                }}
              >
                <Trans>Show all task sequences</Trans>
              </Button>
            }
          />
        ) : (
          <SequenceTable
            shown={page.shown}
            now={page.now}
            ruleList={ruleList}
            machineList={machineList}
            isAdministrator={isAdministrator}
            onDelete={page.setDeletingId}
          />
        )}
      </section>

      {page.creating ? (
        <NewSequenceDialog
          taken={all.map((sequence) => sequence.name)}
          onClose={() => {
            page.setCreating(false);
          }}
        />
      ) : null}

      {deleting !== null ? (
        <DeleteSequenceDialog
          sequence={deleting}
          rules={rulesChoosing(ruleList, deleting.id)}
          activeRuns={activeRunsOf(machineList, deleting.id)}
          onClose={() => {
            page.setDeletingId(null);
          }}
        />
      ) : null}
    </Page>
  );
}
