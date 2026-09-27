# The web UI

How the web UI in `src/DDT.Web` is built, for anyone who adds or changes a page. The look itself, Switchgear, is
described in `src/DDT.Design/README.md`.

## Stack

- React with TypeScript, built by Vite.
- TanStack Router for the pages and TanStack Query for everything read from the server.
- React Aria Components for every control that takes input: they bring keyboard handling, focus management and
  screen reader semantics. Two components, the table and the drawer, started from Untitled UI's free components and
  carry a line that says so; the rest are DDT's own.
- Tailwind CSS v4, with the theme generated from `src/DDT.Design/tokens.json` (`npm run tokens`).
- Lingui for translations, with PO catalogs under `src/locales`. English is the source, German the first
  translation; `pseudo` stretches every text for layout checks.
- Tabler icons, and the Archivo and Martian Mono fonts from Fontsource.
- The SignalR client for the live connection.

Every package that ships in the bundle is listed with its licence in `THIRD-PARTY-NOTICES.md`, and
`npm run licences` writes their licence texts to `licenses/web`.

## Where things are

| Folder | What is in it |
|---|---|
| `src/ui` | The component library: buttons, fields, selects, tables, dialogs, the drawer, menus, tabs, notices, state tags, the sequence rail, filter controls, the QR code. `/design` shows them all in a development build. |
| `src/app` | The shell (top bar, category rows, user menu, command palette, connection banner), the router, the navigation model and the theme. |
| `src/live` | The live connection: hub events, machine watches, the live status and how lists stay fresh. |
| `src/lib` | The API client with its CSRF handling, formatting of sizes, durations and times, autosave. |
| `src/i18n`, `src/locales` | Choosing and loading a language, and the catalogs. |
| One folder per subject | `machines`, `runs`, `log`, `sequences`, `rules`, `images`, `packages`, `uploads`, `account`: the page, its components, and the module that reads and writes the server's data for it. |

## Pages

The top bar holds five categories, each with its own row of pages (`src/app/navigation.ts`). A new feature adds a
page to a category, never a tab to the bar, and a setting sits on the page of the thing it configures.

A page is built from the same parts everywhere: `Page` for the gutters, `PageHeader` with the title and whatever
belongs beside it (filters, search, the main action), `Panel` for each surface, `Table` for lists, `EmptyState`
when a list is empty, `Skeleton` while it loads, `Notice` for a problem the page cannot solve by itself. Actions
that change something open a `Dialog`; actions that erase something use `ConfirmDialog`, with `typedWord` where a
wrong click would erase a disk. People who may only look see no action controls at all.

Tables with truncating cells need `table-fixed`. On a phone, where a table would scroll sideways, a page shows its
items as a list instead (see the machine list and `useMediaQuery`).

## Live data

The pages never reload after an action, and never read a whole list again because something changed.

- Every action is an API call whose answer is the changed thing. The page puts that answer into the TanStack Query
  cache with `setQueryData` and is done.
- Changes made elsewhere arrive through the hub (`/hubs/live`). Each event carries what changed, and
  `src/live/liveConnection.ts` patches the cache with it: `machineChanged` carries the machine, `machinesRemoved`
  the IDs that are gone, and so on. A new event follows that pattern; an event that only says "something
  changed" is not added.
- While the live connection is up, a list the hub keeps current is not read again on focus, on a new page or when a
  panel opens (`liveListOptions` in `src/live/freshness.ts`). While it is down, the lists are read every few
  seconds, and the banner says so.
- A page that shows one machine watches it, and receives its step changes and new log lines as well.
- React Aria collections render a row again only when its item changes. Anything else a row shows, such as the
  clock or the signed-in person's permissions, goes into the collection's `dependencies`.

## Translations

Every text a person reads goes through Lingui: `t`, `plural` and `msg` from `@lingui/core/macro`, `Trans` and
`useLingui` from `@lingui/react/macro`.

- Put values into named variables before a macro uses them: ``const name = step.name; t`Remove ${name}` ``. A
  member or a call inside a macro becomes a numbered argument such as `{0}`, which tells a translator nothing, and
  a test refuses it.
- One sentence is one message. Do not build a sentence from fragments that are translated one by one.
- After changing texts, run `npm run i18n` and translate the new German messages in `src/locales/de/messages.po`.
  A test fails while a German message is missing or has lost one of its arguments.
- Tests run in English.

German texts use the formal "Sie" and Microsoft's terms: Gerät, Tasksequenz, Bereitstellung, Image, Treiber,
Datenträger, Ausführung for a run, Freigabe for an approval.

### What the server says

The server's refusals and the texts in its data, such as a sequence's problems or why a machine gets its sequence,
come with a stable code and the values the text names, beside the English.
`src/DDT.Contracts/Messages/ServerMessages.cs` lists every code with its English as an ICU message, which the server
formats for the English it sends.

- A refusal's problem details carry `code` and `args` beside `title`; a validation problem carries `errorCodes`,
  the codes of its `errors`, field by field and in the same order.
- A text in the data has sibling fields, such as `message`, `code` and `args` on a sequence problem, or
  `explanation`, `explanationCode` and `explanationArgs` on a machine's resolution.
- A value is a string, a number, or a message of its own, `{ code, args }`, for a sentence or a name within the
  sentence.

`apiErrorFrom` says a refusal's title and field errors in the person's language, so a page reads `error.message`
and `problem.errors` as they are. A text in the data goes through `serverText(code, args, english)`, or the helper
beside its type, such as `findingText` or `resolutionText`. A code this build does not know leaves the server's
English.

The web's catalog, `src/lib/serverMessages.ts`, is written from the server's: a server test writes
`scripts/server-messages.json` when the list changed, `npm run messages` writes the catalog from it, and tests on
both sides fail until both are current. Each code is its message's context, so its German is its own even where
the web says the same English elsewhere. After adding or changing a server message, run the server's tests, then
`npm run messages` and `npm run i18n`, and translate the new German messages.

Server logs, audit details and what the agent says stay English, since they are searched and pasted into issues.

## Writing

Texts are plain and specific, in sentence case. A button says what happens ("Delete rule", not "OK"), the result
uses the same words, and an error says what went wrong and what to do about it, without apologising. A
confirmation says what the action leaves behind. Capitals appear only on state tags.

## Checks

From `src/DDT.Web`:

```
npx prettier --write src
npx tsc -b
npx eslint src
npx vitest run
```

Warnings count as errors. Every source file starts with the three-line licence header, which a .NET test checks.
