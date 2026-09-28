# Code style

The build already nags about formatting: `.editorconfig`, the .NET analyzers, ESLint and Prettier
all run with warnings as errors. This page is about the rest, the things a tool can't catch (or
can't catch yet). It applies to everyone who writes code for DDT, humans and AI alike. It applies
twice over to AI, because models love writing essays.

When this page and the code around you disagree, this page wins, and the code around you is due
a tidy-up.

## The short version

1. **Comments are short and say why.** One or two lines, about what the code can't say itself.
2. **One type per file**, and the file is named after it. One component per file on the web.
3. **Small things.** Small classes, short methods, few dependencies. If it does two jobs, it's two
   types.
4. **Names say what things are.** No abbreviations, no `Manager`, no `Helper`.
5. **Follow the platform's conventions.** .NET's for C#, React's for the web. When in doubt, do
   what well-kept code nearby does.
6. **Tests describe behaviour**, in sentences.

## Comments

Write a comment when the code can't speak for itself:

- **Why** something is done this way, especially when the obvious way is wrong.
- **A constraint from outside**: firmware, Windows PE, Windows setup, an RFC, a quirk of a
  library. Link the source (an RFC section, a KB article, an issue) when there is one.
- **A trade-off** someone would otherwise "fix" and break.

And keep it short:

- **One or two lines.** Three or four for a truly tricky why. Anything longer belongs in the
  documentation, in the commit message, or is a hint that the code wants restructuring.
- **Don't narrate the code.** `// Loops over the disks` above a loop over the disks helps no one.
  Neither does a comment that repeats a method's name or lists its parameters.
- **No history.** "Since then", "used to" and "was changed because" belong in the commit
  message, where `git blame` finds them.
- **Use the proper term.** "The DHCP offer" beats "what the server answers first". The people
  reading this are developers.
- **Don't point at places that move**, like a README section or a line number. Link an issue or a
  stable document instead.
- **A type's comment says what it's for, in one line.** Skip it when the name already says so.
- **`TODO` only with an issue:** `// TODO(#123): drop once firmware X is gone`.
- **Plain `//` comments.** Nobody generates API docs from DDT, so skip `///` boilerplate that only
  repeats the member's name.

Good ones, from DDT itself:

```csharp
// The agent runs in x64 WinPE and starts bcdboot from the applied image, which fails for any other image.
public const string DeployableArchitecture = "x64";

// 2 GB more keeps the downloads and the applied image from filling the disk to the last byte.
private const long SpareBytes = 2048L * 1024 * 1024;
```

And one that needs the treatment. `SequenceRunner` opens with 16 lines of prose:

```csharp
// Runs a task sequence in Windows PE. A fresh run is checked first, so nothing is erased for a run that cannot
// succeed; a run found on the disk after a restart goes on where it was, or is handed over again when the installed
// Windows was to go on with it. The engine runs the steps, and the Windows PE phase ends in one of three ways: ...
// (13 more lines)
```

Two lines say what matters, and the rest goes to the documentation:

```csharp
// Runs a task sequence in Windows PE and resumes a run found on disk after a restart.
// Never throws: every failure is reported, because a crashed agent is replaced by the boot image's.
```

## Files and types

**C#**

- **One top-level type per file**, named after the type. That covers classes, records, structs,
  interfaces, enums, delegates and exceptions. `Result<T>` lives in `Result.cs`. When a generic
  and a non-generic type share a name, the generic one gets `Result{T}.cs`.
- **The folder is the namespace.** File-scoped namespaces and usings outside of them; the build
  enforces those two.
- **Nested types** are fine when they're private, small and only make sense inside their parent.
- **Partial classes are for generators and designers**: `JsonSerializerContext`, `[LoggerMessage]`,
  Avalonia's code-behind. They are not a way to hide a big class across several files. The one
  exception is a data catalogue like `ServerMessages`, which may keep one part per area in
  `ServerMessages.Area.cs`.

**TypeScript**

- **One component per file**, named after it: `AssignDialog.tsx` holds `AssignDialog`.
- **A private helper component** can stay in its file while it's a handful of lines and nothing
  else uses it. Once it grows past about 30 lines, or another file wants it, it moves out.
- **A compound component** may share one file, named after its root. That means parts that only
  work nested inside the root, like `Table`, `TableHeader` and `TableRow`. Components that merely
  look alike are not a compound.
- **One hook per file**, called `useThing.ts`. Plain modules are `camelCase.ts`. Everything lives
  in its feature's folder (`machines/`, `settings/`, ...).
- **Named exports only.** No default exports.
- **Tests sit next to the code**: `AssignDialog.test.tsx`.

## Size

These are smoke alarms, not laws. Past the second number, split it before you add to it.

| | Aim for | Time to split |
|---|---|---|
| C# class | 300 lines | 400 |
| C# method | 40 lines | 60 |
| Constructor dependencies | 5 | 7 |
| Method parameters | 4 | 6, use a record (or `[AsParameters]` in a minimal API handler) |
| Nesting depth | 3 | 4 |
| TypeScript file | 300 lines | 400 |
| Component or function | 80 lines | 120 |
| Test class or test file | 500 lines | 700 |

The dependency limit is for services. A record or another data type has as many members as its
data has.

**Split by responsibility, not by line count.** Some signs a class does two jobs:

- its one-line description needs an "and";
- some fields are only used by some methods;
- it takes dependencies that only one method needs;
- comments like `// --- Downloads ---` separate its sections.

Pull the job out into a type whose name says what it does. `Helper`, `Utils` and `Manager` are
not names, they're drawers everything ends up in.

**Flat data may run long.** A message catalogue, a lookup table or a binary format's constants can
be longer than 400 lines when they're just data. Split them by area once finding things gets hard.
The same goes for a cohesive codec, like the FAT reader. Rather than chopping it into pieces, take
out what it duplicates.

## Naming

- **The .NET conventions, most of them enforced:**
  - `PascalCase` for types and members;
  - `camelCase` for locals and parameters;
  - `_camelCase` for private fields, `s_camelCase` for private static ones;
  - `I` for interfaces, `T` for type parameters;
  - the `Async` suffix on async methods.
- **Whole words.** Only abbreviations everyone in the field knows: `Id`, `Url`, `Http`, `Dhcp`,
  `Tftp`, `Pxe`, `Wim`, `Bcd`, `WindowsPE`.
- **Booleans read as a question**: `IsApproved`, `HasDisk`, `CanPick`.
- **American spelling in code** (`Authorize`, `Color`, `Initialize`), the way the frameworks spell
  it. The documentation writes British English.
- **TypeScript:**
  - `camelCase` for functions and variables;
  - `PascalCase` for components and types;
  - `useThing` for hooks;
  - `UPPER_SNAKE_CASE` for module constants.

## C#

- **Nullable is on.** Don't use `!` to silence it. There are none today; let's keep it that way.
- **`sealed` by default**, unless a class is designed to be inherited.
- **Primary constructors** for dependencies.
- **Check arguments at public entry points** with `ArgumentNullException.ThrowIfNull` and friends.
- **Records for data**, like the contracts. Options classes use `set`, because the configuration
  binding generator skips `init` without a warning.
- **Async all the way.** Never `.Result` or `.Wait()`. The `CancellationToken` comes last and is
  passed on. `ConfigureAwait(false)` goes on every awaited call except in code that needs the UI
  thread (the console's view models). `await using` goes without it.
- **Time comes from `TimeProvider`**, never from `DateTime.Now` or `DateTimeOffset.UtcNow`, so the
  tests can move the clock.
- **Logging** uses source-generated `[LoggerMessage]` methods, each with its own event id.
  Passwords, tokens and secrets never go into a log line.
- **Catch what you can handle.** `catch (Exception)` only belongs at a boundary that reports the
  failure, such as the agent's loop, a hosted service or an endpoint filter, and it gets a
  one-line comment saying so.
- **NativeAOT-friendly.** No reflection-based JSON in the agent or the console: use
  source-generated `JsonSerializerContext`s. Trimming warnings are errors.
- **Thin endpoints.** A minimal API handler binds the request, checks access, calls a service and
  maps the answer. If it needs a dozen dependencies, it takes an `[AsParameters]` record, or its
  logic moves into a service.
- **LINQ where it reads better**, a loop where that's clearer or the code is hot.

## TypeScript and React

- **Strict TypeScript.** No `any`, no `!`.
- **Function components** with named exports. Props get a `type ThingProps` once there are more
  than a couple.
- **Server state lives in TanStack Query**, and pages stay live: an action patches the cache with
  the server's answer, and other people's changes arrive through the hub. See
  [web-ui.md](web-ui.md#live-data).
- **Controls come from `src/ui`**, which wraps React Aria. Don't hand-roll a button, dialog or
  select.
- **Every text goes through Lingui** (`t` or `<Trans>`), in English and German.
- **Colours, spacing and motion come from the design tokens**, never hard-coded values.
- **Components mostly render.** Logic lives in hooks and plain modules, where it's easy to test
  without a DOM.

## Tests

- **A test's name is a sentence** about behaviour: `TheAssignDialogLearnsTheServersTime`, or
  `it("shows the legal notices and the version the server runs")`.
- **One test class per class or feature** (`ThingTests`). Past 500 lines, split it by behaviour:
  `SequenceRunnerResumeTests`, `SequenceRunnerPreflightTests`.
- **One behaviour per test**, in arrange, act, assert order.
- **No sleeping.** Move a fake clock (`TimeProvider`, fake timers) instead of waiting for a real
  one. When a test has to wait for something outside the process, wait for the condition, never
  for a fixed time.
- **Fakes get their own files too.** One type per file applies to tests as well.
- **Real beats made-up.** A capture from real firmware beats a hand-made packet fixture.

## PowerShell

- **Scripts** start with `[CmdletBinding()]`, `Set-StrictMode -Version Latest` and
  `$ErrorActionPreference = 'Stop'`, and have comment-based help.
- **Functions** use approved verbs (`Get-`, `New-`, `Invoke-`, ...) and do one thing each.
- **A script that grows past a few hundred lines** moves its functions into a module next to it,
  and the script only reads its parameters and calls them.

## What the tools check for you

You don't have to remember any of this, because the build fails when it's wrong:

- **.NET:** formatting, naming, `var` usage, braces, file-scoped namespaces and the licence header,
  all from `.editorconfig`, with warnings as errors.
- **One type per file and method length:** the Meziantou analyzers MA0048 and MA0051. Every other
  Meziantou rule is off.
- **Class size, dependencies, parameters and comment length:** `CodeShapeTests`, because no
  analyzer checks those.
- **Web:** ESLint's strict type-checked rules, its size limits (`max-lines`,
  `max-lines-per-function`, `max-depth`, `max-params`), and Prettier for formatting. A test checks
  one component per file.
- **Tests:** the licence header in every other language, stale generated files, and missing
  translations.

[CONTRIBUTING.md](../CONTRIBUTING.md) has the full list of checks.

## When a rule gets in the way

Readability wins. Break the rule on purpose, leave a one-line comment saying why, and mention it
in the pull request.
