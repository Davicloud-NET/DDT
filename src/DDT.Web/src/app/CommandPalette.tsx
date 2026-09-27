// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { Trans, useLingui } from "@lingui/react/macro";
import { IconSearch } from "@tabler/icons-react";
import { useQuery } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { useEffect, useState, type ReactNode } from "react";
import {
  Button as AriaButton,
  Autocomplete,
  Dialog as AriaDialog,
  Header,
  Input,
  Menu,
  MenuItem,
  MenuSection,
  Modal as AriaModal,
  ModalOverlay,
  SearchField,
  useFilter,
} from "react-aria-components";

import { imagesQuery } from "@/images/images";
import { formatMac, machinesQuery } from "@/machines/machines";
import { displayName, hardwareLine } from "@/machines/machineView";
import { sequencesQuery } from "@/sequences/sequences";
import { cx } from "@/ui/cx";

import { categories } from "./navigation";

interface Entry {
  id: string;
  to: string;
  // Params and search for a page with a parameter, such as a machine's.
  params?: Record<string, string>;
  label: string;
  detail?: string;
  // Words that find the entry besides its label, such as a machine's MAC addresses.
  keywords?: string;
}

// Ctrl K (Cmd K on a Mac) opens a search over every page, machine, task sequence and image, to jump to it. What it
// lists comes from the lists the pages already hold, read when the palette first opens.
export function CommandPalette() {
  const { t } = useLingui();
  const [isOpen, setOpen] = useState(false);

  useEffect(() => {
    const open = (event: KeyboardEvent) => {
      if (event.key.toLowerCase() === "k" && (event.ctrlKey || event.metaKey) && !event.altKey) {
        event.preventDefault();
        setOpen((current) => !current);
      }
    };

    window.addEventListener("keydown", open);

    return () => {
      window.removeEventListener("keydown", open);
    };
  }, []);

  return (
    <>
      <AriaButton
        onPress={() => {
          setOpen(true);
        }}
        aria-keyshortcuts="Control+K"
        className="mr-3 flex h-8 w-56 cursor-pointer items-center gap-2 self-center rounded-key bg-frame-hover px-2.5 text-frame-muted motion-colors outline-none hover:text-frame-text focus-visible:outline-2 focus-visible:outline-focus max-md:w-auto"
      >
        <IconSearch aria-hidden="true" size={16} stroke={2} />
        <span className="flex-1 text-left type-small max-md:sr-only">
          <Trans>Go to…</Trans>
        </span>
        <kbd className="rounded-tag border border-frame-line px-1.5 font-sans type-small max-md:hidden">
          Ctrl K
        </kbd>
      </AriaButton>
      <ModalOverlay
        isOpen={isOpen}
        onOpenChange={setOpen}
        isDismissable
        className="fixed inset-0 z-50 flex items-start justify-center bg-backdrop px-4 pt-[14vh] entering:animate-overlay-in exiting:animate-overlay-out"
      >
        <AriaModal className="w-full max-w-150 overflow-hidden rounded-overlay bg-raised shadow-overlay outline-none entering:animate-pop-in exiting:animate-pop-out">
          <AriaDialog
            aria-label={t`Go to a page, machine, sequence or image`}
            className="outline-none"
          >
            <Palette
              onDone={() => {
                setOpen(false);
              }}
            />
          </AriaDialog>
        </AriaModal>
      </ModalOverlay>
    </>
  );
}

function Palette({ onDone }: { onDone: () => void }) {
  const { i18n, t } = useLingui();
  const navigate = useNavigate();
  const { contains } = useFilter({ sensitivity: "base" });
  const machines = useQuery(machinesQuery).data ?? [];
  const sequences = useQuery(sequencesQuery).data ?? [];
  const images = useQuery(imagesQuery).data ?? [];

  const pages: Entry[] = categories.flatMap((category) =>
    category.pages.map((page) => ({
      id: `page:${page.to}`,
      to: page.to,
      label: i18n._(page.label),
      detail: i18n._(category.label),
    })),
  );
  const machineEntries: Entry[] = machines.map((machine) => ({
    id: `machine:${machine.id}`,
    to: "/machines/$machineId",
    params: { machineId: machine.id },
    label: displayName(machine),
    detail: hardwareLine(machine),
    keywords: [
      machine.serialNumber,
      machine.lastSeenAddress,
      ...machine.macAddresses.flatMap((mac) => [mac, formatMac(mac)]),
    ]
      .filter((value): value is string => value !== null)
      .join(" "),
  }));
  const sequenceEntries: Entry[] = sequences.map((sequence) => ({
    id: `sequence:${sequence.id}`,
    to: "/deployment/sequences/$sequenceId",
    params: { sequenceId: sequence.id },
    label: sequence.name,
  }));
  const imageEntries: Entry[] = images.map((image) => ({
    id: `image:${image.id}`,
    to: "/library/images",
    label: image.name,
    ...(image.originalFileName === null ? {} : { detail: image.originalFileName }),
  }));
  const all = [...pages, ...machineEntries, ...sequenceEntries, ...imageEntries];

  return (
    <Autocomplete
      filter={(text, input, node) => {
        const entry = all.find((candidate) => candidate.id === node.key);

        return (
          contains(text, input) ||
          (entry?.keywords !== undefined &&
            contains(entry.keywords.replace(/[-:]/g, ""), input.replace(/[-:]/g, "")))
        );
      }}
    >
      <SearchField
        aria-label={t`Search`}
        autoFocus
        className="flex items-center gap-2.5 border-b border-line-soft px-4"
      >
        <IconSearch aria-hidden="true" size={18} stroke={2} className="text-muted" />
        <Input
          placeholder={t`Go to a page, machine, sequence or image`}
          className="h-13 flex-1 bg-transparent type-body text-ink outline-none placeholder:text-placeholder [&::-webkit-search-cancel-button]:hidden"
        />
      </SearchField>
      <Menu
        aria-label={t`Results`}
        onAction={(key) => {
          const entry = all.find((candidate) => candidate.id === key);

          if (entry !== undefined) {
            onDone();
            void navigate(
              entry.params === undefined
                ? { to: entry.to }
                : { to: entry.to, params: entry.params },
            );
          }
        }}
        renderEmptyState={() => (
          <p className="px-4 py-6 type-small text-muted">
            <Trans>Nothing matches.</Trans>
          </p>
        )}
        className="max-h-[55vh] overflow-y-auto p-1.5 outline-none"
      >
        <Section title={<Trans>Pages</Trans>} entries={pages} />
        <Section title={<Trans>Machines</Trans>} entries={machineEntries} />
        <Section title={<Trans>Task sequences</Trans>} entries={sequenceEntries} />
        <Section title={<Trans>OS images</Trans>} entries={imageEntries} />
      </Menu>
    </Autocomplete>
  );
}

function Section({ title, entries }: { title: ReactNode; entries: Entry[] }) {
  if (entries.length === 0) {
    return null;
  }

  return (
    <MenuSection className="pb-1">
      <Header className="px-2.5 pt-2 pb-1 type-small text-muted">{title}</Header>
      {entries.map((entry) => (
        <MenuItem
          key={entry.id}
          id={entry.id}
          textValue={entry.label}
          className={({ isFocused }) =>
            cx(
              "flex cursor-pointer items-baseline gap-3 rounded-key px-2.5 py-2 motion-highlight outline-none",
              isFocused && "bg-selected",
            )
          }
        >
          <span className="truncate type-label text-ink">{entry.label}</span>
          {entry.detail !== undefined ? (
            <span className="truncate type-small text-muted">{entry.detail}</span>
          ) : null}
        </MenuItem>
      ))}
    </MenuSection>
  );
}
