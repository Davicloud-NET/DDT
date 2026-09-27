// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { Link, useNavigate, useSearch } from "@tanstack/react-router";

import { currentUserQuery } from "@/auth/auth";
import { fullTime, relativeTime } from "@/lib/relativeTime";
import { useNow } from "@/lib/useNow";
import { Page, PageHeader, Panel, Skeleton } from "@/ui/Layout";
import { Notice } from "@/ui/Notice";
import { StateTag } from "@/ui/StateTag";
import { Tab, TabList, TabPanel, Tabs } from "@/ui/Tabs";

import { AgentPanel, ConsolePanel } from "./AgentPanel";
import { CertificatePanel } from "./CertificatePanel";
import { ConfigurationOrigin } from "./ConfigurationOrigin";
import { LoggingPanel } from "./LoggingPanel";
import { ProxiesPanel } from "./ProxiesPanel";
import { serverSearch, type ServerTab } from "./serverSearch";
import { settingsOverviewQuery, type SettingsOverview } from "./settings";
import { ApplyStates } from "./SettingsParts";

type ServerSetting = SettingsOverview["server"][number];
type SectionSummary = SettingsOverview["sections"][number];

const linkClass = "text-ink underline underline-offset-3 hover:text-run-text";

// Administration > Server: what is left of the server's own settings once the others moved to the pages of what they
// configure. The overview shows what configuration alone decides and how every settings section stands; the other
// tabs edit the certificate, the proxies, the agent machines netboot with, and the log levels. Only administrators
// reach it; the server refuses everyone else.
export function ServerPage() {
  const me = useQuery(currentUserQuery).data ?? null;

  if (me === null) {
    return null;
  }

  if (!me.roles.includes("Administrator")) {
    return (
      <Page>
        <PageHeader title={<Trans>Server</Trans>} />
        <Notice>
          <Trans>Only administrators see and change the server's settings.</Trans>
        </Notice>
      </Page>
    );
  }

  return <ServerSettings />;
}

function ServerSettings() {
  const { t } = useLingui();
  const search = serverSearch({ ...useSearch({ from: "/shell/admin/server" }) });
  const navigate = useNavigate({ from: "/admin/server" });
  const overview = useQuery(settingsOverviewQuery);
  const tab: ServerTab = search.tab ?? "overview";

  return (
    <Page className="max-w-[72rem]">
      <PageHeader title={<Trans>Server</Trans>} />

      {overview.data?.keyRingReadable === false ? (
        <Notice tone="fail" title={<Trans>This server saves no settings</Trans>}>
          <Trans>
            This process cannot read the key ring the stored secrets were encrypted with, so it
            refuses every save. Every DDT process on one database has to share the key ring in
            DDT:StorePath/keys.
          </Trans>
        </Notice>
      ) : null}

      <Tabs
        selectedKey={tab}
        onSelectionChange={(key) => {
          const next = serverSearch({ tab: key });

          void navigate({ search: next, replace: true });
        }}
      >
        <TabList aria-label={t`Server settings`}>
          <Tab id="overview">
            <Trans>Overview</Trans>
          </Tab>
          <Tab id="certificate">
            <Trans>Certificate</Trans>
          </Tab>
          <Tab id="proxies">
            <Trans>Proxies</Trans>
          </Tab>
          <Tab id="agent">
            <Trans>Agent</Trans>
          </Tab>
          <Tab id="logging">
            <Trans>Logging</Trans>
          </Tab>
        </TabList>
        <TabPanel id="overview" className="flex flex-col gap-4">
          {overview.isError ? (
            <Notice tone="fail">
              <Trans>The server's settings could not be read.</Trans>
            </Notice>
          ) : overview.data === undefined ? (
            <Skeleton className="h-64 w-full" />
          ) : (
            <>
              <Sections sections={overview.data.sections} />
              <Configuration settings={overview.data.server} />
            </>
          )}
        </TabPanel>
        <TabPanel id="certificate" className="flex flex-col gap-4">
          <CertificatePanel />
        </TabPanel>
        <TabPanel id="proxies" className="flex flex-col gap-4">
          <ProxiesPanel />
        </TabPanel>
        <TabPanel id="agent" className="flex flex-col gap-4">
          <AgentPanel />
          <ConsolePanel />
        </TabPanel>
        <TabPanel id="logging" className="flex flex-col gap-4">
          <LoggingPanel />
        </TabPanel>
      </Tabs>
    </Page>
  );
}

// Every settings section, with how it stands and a link to the page that edits it.
function Sections({ sections }: { sections: SectionSummary[] }) {
  const { t } = useLingui();
  const now = useNow(30_000);

  return (
    <Panel title={<Trans>Settings sections</Trans>} flush>
      <p className="max-w-[80ch] px-4 pt-3 text-ink-2">
        <Trans>
          Each section is saved on the page of what it configures, and applies without a restart:
          some at their next use, others by rebuilding their part of the running server.
        </Trans>
      </p>
      <ul aria-label={t`Settings sections`} className="flex flex-col">
        {sections.map((summary) => (
          <SectionRow key={summary.section} summary={summary} now={now} />
        ))}
      </ul>
    </Panel>
  );
}

function SectionRow({ summary, now }: { summary: SectionSummary; now: number }) {
  const locked = summary.lockedCount;
  const problems = summary.problemCount;
  const version = summary.version;
  const savedBy = summary.updatedBy;
  const savedWhen = summary.updatedUtc === null ? null : relativeTime(summary.updatedUtc, now);

  return (
    <li className="flex flex-col gap-1.5 border-b border-line-soft px-4 py-3 last:border-b-0">
      <span className="flex flex-wrap items-center gap-x-3 gap-y-1.5">
        <span className="flex-1 type-label">
          <SectionLink section={summary.section} />
        </span>
        {locked > 0 ? (
          <StateTag tone="idle">
            {plural(locked, { one: "# set in configuration", other: "# set in configuration" })}
          </StateTag>
        ) : null}
        {problems > 0 ? (
          <StateTag tone="fail">
            {plural(problems, { one: "# problem", other: "# problems" })}
          </StateTag>
        ) : null}
      </span>
      <span className="flex flex-wrap gap-x-3 gap-y-0.5 type-small text-muted">
        <span>
          {summary.kind === "Live" ? (
            <Trans>Applies at its next use</Trans>
          ) : (
            <Trans>Rebuilds its part of the server on save</Trans>
          )}
        </span>
        <span>
          <Trans>Version {version}</Trans>
        </span>
        <span {...(summary.updatedUtc === null ? {} : { title: fullTime(summary.updatedUtc) })}>
          {savedWhen === null ? (
            <Trans>Never changed on a page</Trans>
          ) : savedBy === null ? (
            <Trans>Saved {savedWhen}</Trans>
          ) : (
            <Trans>
              Saved {savedWhen} by {savedBy}
            </Trans>
          )}
        </span>
      </span>
      {summary.apply !== null && summary.apply.length > 0 ? (
        <ApplyStates states={summary.apply} version={version} />
      ) : null}
    </li>
  );
}

// The page that edits a section. The certificate section holds the server names.
function SectionLink({ section }: { section: string }) {
  switch (section) {
    case "deployment":
      return (
        <Link to="/deployment/defaults" className={linkClass}>
          <Trans>Deployment defaults</Trans>
        </Link>
      );
    case "machines":
      return (
        <Link to="/machines/approval" className={linkClass}>
          <Trans>Approval and zero touch</Trans>
        </Link>
      );
    case "pxe":
      return (
        <Link to="/boot/network" className={linkClass}>
          <Trans>Network boot</Trans>
        </Link>
      );
    case "ldap":
      return (
        <Link to="/admin/sign-in" className={linkClass}>
          <Trans>Directory sign-in (LDAP)</Trans>
        </Link>
      );
    case "oidc":
      return (
        <Link to="/admin/sign-in" className={linkClass}>
          <Trans>Single sign-on (OpenID Connect)</Trans>
        </Link>
      );
    case "certificate":
      return (
        <Link to="/admin/server" search={{ tab: "certificate" }} className={linkClass}>
          <Trans>Server names</Trans>
        </Link>
      );
    case "proxies":
      return (
        <Link to="/admin/server" search={{ tab: "proxies" }} className={linkClass}>
          <Trans>Proxies</Trans>
        </Link>
      );
    case "logging":
      return (
        <Link to="/admin/server" search={{ tab: "logging" }} className={linkClass}>
          <Trans>Logging</Trans>
        </Link>
      );
    default:
      return <span className="type-data text-ink">{section}</span>;
  }
}

// What configuration alone decides, read-only: the server needs it before it can serve the page.
function Configuration({ settings }: { settings: ServerSetting[] }) {
  const { t } = useLingui();

  return (
    <Panel title={<Trans>Set in configuration</Trans>}>
      <p className="max-w-[80ch] text-ink-2">
        <Trans>
          The server needs these before it can serve this page, so they stay in its configuration:
          environment variables, appsettings.json or the command line. It reads them as it starts,
          so a change there takes a restart of DDT. Values that could hold a password are never
          shown.
        </Trans>
      </p>
      <ul aria-label={t`Configuration values`} className="flex flex-col">
        {settings.map((setting) => (
          <li
            key={setting.key}
            className="grid gap-x-4 gap-y-0.5 border-t border-line-soft py-2 first:border-t-0 md:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]"
          >
            <span className="type-data break-all text-ink">{setting.key}</span>
            <span className="flex flex-wrap items-baseline gap-x-2">
              <ConfiguredValue setting={setting} />
            </span>
          </li>
        ))}
      </ul>
    </Panel>
  );
}

function ConfiguredValue({ setting }: { setting: ServerSetting }) {
  const hasOrigin = setting.isSet ? setting.source !== null : setting.value !== null;

  return (
    <>
      {setting.value !== null ? (
        <span className="type-data break-all text-ink">{setting.value}</span>
      ) : setting.isSet ? (
        <span className="text-ink">
          {setting.secret ? <Trans>Set, not shown</Trans> : <Trans>Set</Trans>}
        </span>
      ) : (
        <span className="text-muted">
          <Trans>Not set</Trans>
        </span>
      )}
      {hasOrigin ? (
        <span className="type-small text-muted">
          <ConfigurationOrigin setting={setting} />
        </span>
      ) : null}
    </>
  );
}
