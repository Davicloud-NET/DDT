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
| `src/ui` | The component library: buttons, fields, selects, tables, dialogs, the drawer, menus, tabs, notices, state tags, the sequence rail, filter controls, the QR code, the secret shown once, and the flow's canvas, wires, node card and minimap. `/design` shows them all in a development build. |
| `src/app` | The shell (top bar, category rows, user menu, command palette, connection banner), the router, the navigation model and the theme. |
| `src/live` | The live connection: hub events, machine watches, the live status and how lists stay fresh. |
| `src/lib` | The API client with its CSRF handling, formatting of sizes, durations and times, autosave. |
| `src/i18n`, `src/locales` | Choosing and loading a language, and the catalogs. |
| One folder per subject | `machines`, `runs`, `log`, `sequences`, `rules`, `roles` (machine roles), `accounts` (the accounts steps use), `images`, `packages`, `uploads`, `account`, `users`, `tokens`: the page, its components, and the module that reads and writes the server's data for it. `inputs` asks a sequence's inputs on the web, and `machines/facts.ts` names what a machine reports, for the machine's page and for conditions. |
| `src/conditions`, `src/values` | What several pages share: the condition builder and its subjects, used by the flow builder and the rules; and the values of runs, rules and machine roles, with where a run's values came from and the editor of the values a rule or a machine role sets. |

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

## The flow builder

The Task sequences page edits a sequence as a flow. The page is `src/sequences/builder`, over pure modules in
`src/sequences/flow` and the canvas parts in `src/ui`:

| Where | What |
|---|---|
| `sequences/flow/flowTree.ts` | The tree as the server's `SequenceTree` walks it, in pre-order: where each node sits, the slots where nodes can go, and the version a document needs. |
| `sequences/flow/flowEdits.ts` | The reducer, grown from the rail editor's `SequenceEdit`: insert at a slot, move, remove, wrap in a group, an IF or a Repeat, unwrap, update, the sequence's variables and inputs, and renaming a name everywhere. |
| `sequences/flow/conditionTree.ts` | Edits of a node's condition trees, its when, an IF's test and a Repeat's until, by path. |
| `sequences/flow/references.ts` | Where a sequence names its variables and inputs, for "Used by" and "Rename everywhere". |
| `sequences/flow/templates.ts` | The server's `ValueTemplate`, mirrored for completion and the preview on a sample machine. |
| `sequences/flow/flowLayout.ts` | The layout, see below. |
| `sequences/flow/flowKeyboard.ts` | The keys of the canvas, the labels a screen reader reads, and the clipboard's format. |
| `sequences/flow/history.ts` | Undo and redo. |
| `sequences/builder` | `FlowBuilder` (the page), `FlowCanvas` (cards, slots, the node's menu, drag and drop), `FlowOutline`, `Palette`, `AddNodeMenu`, the `Inspector` with its tabs Node, Variables, Problems and Sequence, `TemplateField` and `AccountSetting`. |
| `src/conditions` | `ConditionBuilder`, which the rules share: nested all, any and none groups, the subjects in sections (the machine, the run, rules and machine roles, the sequence), the operators that fit each kind of value, and the sentence a condition reads as. |
| `src/ui` | `FlowViewport` and `viewTransform.ts` (pan, wheel, Ctrl+wheel and pinch zoom, fit), `FlowWires` (SVG wires, arrows, ports and join dots under the cards), `FlowNode` (the card, with the rail's module along its top edge, and the frame of a container) and `Minimap`. |

**The layout.** `flowLayout.ts` works out every box, frame, port, join, wire, arrow and slot from the tree alone, top
to bottom, in pixels of the flow at 100 %. A series stacks its nodes on one axis. An IF is its card with the Then and
Else ports on its bottom edge, the branches side by side, and curves from each port to its branch and back to a join
dot. A group or a Repeat is a frame around its header card and its body, and a Repeat keeps a lane on the left for
its wire back. Node sizes are fixed per kind, so nothing is measured, and the same tree always gives the same layout;
nothing is placed by hand or saved. A collapsed container is one card with a strip of its steps, remembered per
browser and sequence under `ddt.flow.collapsed.<id>` in local storage. The renderer only draws what the layout gives
it. Cards change their colour with `motion-colors`; nothing slides when the tree changes.

**No graph library.** A library such as React Flow is built around nodes placed freely and wires drawn by hand, which
a tree laid out by rule never uses, and its own pointer and keyboard handling would fight React Aria's drag and drop
and arrow keys that follow the flow. The tree is series-parallel, so a recursive layout is exact. So the canvas is
DDT's own: no package added, nothing to stub in jsdom, and since the renderer only consumes the layout, a library
could still take its place later.

**Editing.** Every change goes through the reducer, so autosave, the conflict handling and `EditorLock` stay as the
rail editor had them. The "+" on a slot opens the add menu. A node or a palette item is dragged with React Aria's
`useDrag` onto a slot's `useDrop`, as `application/x-ddt-flow-node` or `application/x-ddt-flow-kind`; the outline
moves rows with `useDragAndDrop` on a React Aria `Tree`. The keyboard moves a node with Alt and the arrows, with cut
and paste, or from its menu, rather than by a keyboard drag.

**The keyboard.** `flowKeyboard.ts` turns keys into commands from the tree alone. The canvas is one Tab stop with a
roving focus: only the selected node, or else the first, has `tabIndex` 0. Up and Down follow the flow, Left and
Right cross to the other branch of the nearest IF, Home and End go to the first and the last node, Escape to the
container around, Enter to the inspector; Delete removes, Alt+Up and Alt+Down move within a list, Shift+F10 or the
menu key open the node's menu. Ctrl+C, X, V and D go through the system clipboard as `{ddtFlow: 1, nodes}`, with an
in-page copy where the clipboard is refused, and a paste gives the nodes new ids. Ctrl+Z and Ctrl+Y belong to the
page.

**History.** `history.ts` keeps whole copies of the document, 100 deep, and typing into one field with less than a
second between keys is one step. An undo or a redo is saved at once like any other change, and both stacks are
dropped when the page takes in another administrator's save. Removing a node shows "Removed X." with Undo for 10
seconds; the toast and the history are two ways to the same undo.

**Shares belong to steps.** A share is connected while its step runs, so only a leaf has shares: the inspector offers
"Network shares" for a step and never for a group, an IF or a Repeat, and the server refuses shares on a container
(`sequence.sharesOnlyOnSteps`) and hands a container no account. A new kind of container keeps it so.

**Loading and tests.** The builder's route is loaded when it is opened (`lazyRouteComponent` in `app/router.tsx`),
and a machine's page loads `RunFlow` the same way; both import the same layout and canvas modules. The pure modules
have unit tests, the layout's among them against overlaps, children outside their frames and for 300 nodes in under
5 ms. `src/test/fixtures` holds sequences and template cases that server tests write, so the web's mirror of the
contracts and of `ValueTemplate` fails a test when the server's changes.

## A run's flow

A machine's page draws the run's own copy of the sequence with the same layout and canvas (`src/runs/RunFlow.tsx`).
`runs/runPath.ts` works out the path from that tree and the steps the server keeps of the run, the latest visit of
each node: a node in a branch an IF did not take, or inside a container that was skipped, is not taken; everything
else is done, running, paused, waiting or still ahead, and an IF that has not decided keeps both branches ahead.
`FlowWires` draws the taken wires in ink and the others dashed, and a node not taken has a dashed outline and muted
text rather than a lower opacity, so it keeps its contrast. `runs/decisions.ts` says why a run went where
it went from what the agent recorded when it decided, each test with whether it held and the value it met, such as
"Took Then: Model contains Latitude holds for "Latitude 7450"", never from what the machine reports now. `RunSteps`
lists the path's nodes with their place in the tree and that line. The canvas follows the node the run is at until
any move it did not make itself, and "Follow the run" takes it up again while the run goes on.
`machines/RunWaiting.tsx` is the notice of a run that waits at a Pause or for answers, with its keys for operators,
and `src/inputs` asks a sequence's inputs there, in the assign dialog and in the approval.

## Motion

What moves, and how, is in `src/DDT.Design/README.md`; the values are the `motion` tokens, which the theme carries
as `--duration-*`, `--ease-*` and `--motion-distance`. A page rarely animates anything itself: the components in
`src/ui` do.

- The controls in `src/ui` already answer hover and press. A control made by hand takes a utility from
  `src/styles/app.css`: `motion-colors` for a colour change such as a hover, `key-motion` and a `pressed:` colour for
  a key, `motion-highlight` for a highlight that follows the keyboard through a list, `motion-fill` for a bar whose
  width is its value. Not `transition-colors`, which fades the focus ring in, nor `transition-all`.
- Overlays on React Aria (dialogs, the drawer, menus, popovers, tooltips) enter with an `entering:` animation such
  as `animate-pop-in` and leave with the matching `exiting:` one, `animate-pop-out`; React Aria keeps them mounted
  until the exit has run. A popover comes from the side of its trigger, by its placement. Toasts leave through
  `LeavingToastQueue` in `src/ui/toasts.ts`, since React Aria's toasts have no exit of their own.
- The shell fades a page in when the route changes (`useReplay` in `src/ui/motion.ts`), and a tab panel fades in
  when another tab is chosen. Pages do not slide.
- A list the hub keeps current points out what changed with `useLiveMarks` from `src/live/useLiveMarks.ts`: it takes
  the query key the list shows, how to find the items in its data, an item's id, the signature of what a change
  should point out (usually the state) and the state's tone. Its result gives the classes for an item's row, and goes
  into a React Aria collection's `dependencies`. Only data that arrives by `setQueryData` is marked, so a first load,
  a reconnect, polling and older pages never flash.
- With "reduce motion", `app.css` turns off every animation and every transition but those of colour; the flash
  stays, as it changes nothing but a colour. Code that times motion itself takes its durations from
  `src/ui/motion.ts`, which a test holds to the tokens.
- jsdom has no Web Animations, so `src/test/setup.ts` gives every element an empty `getAnimations`: entrances and
  exits end at once in tests. A test that needs an exit to run stubs it, as `src/ui/motion.test.tsx` does.

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
  `explanation`, `explanationCode` and `explanationArgs` on a machine's resolution. The settings API's problems,
  warnings, apply states and test results carry `text`, the message as `{ code, args }`, beside `message`, because a
  warning's `code` is its confirmation code.
- A value is a string, a number, or a message of its own, `{ code, args }`, for a sentence or a name within the
  sentence.

`apiErrorFrom` says a refusal's title and field errors in the person's language, so a page reads `error.message`
and `problem.errors` as they are. A text in the data goes through `serverText(code, args, english)`, or the helper
beside its type, such as `findingText`, `resolutionText` or `settingsText`. A code this build does not know leaves the server's
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
