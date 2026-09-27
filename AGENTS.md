# Agent instructions

## Language

Issues, pull requests and commit messages are written in **English** — title and
body. This is the collaboration surface, and keeping it English is what lets
someone who does not speak German read the backlog and work here.

German terms of art stay German: Atemschutz, Stammdaten, Einsatzkraft, Trupp,
Funkrufname, Einsatzdaten, Messreihe. They are what the app says on screen and
what a Kommandant says out loud, so translating them makes the text describe a
product that does not exist. `Atemschutz: If the Truppnummer already exists, the
application crashes` is exactly right. Quoted UI strings, wording proposed for a
screen, and log output are reproduced verbatim — never translated.

A pull request title becomes the squash-merge commit subject, so this is one
rule in both places; see [Git commits](#git-commits).

What is English is the workbench, not the product:

| English | German |
|---|---|
| issues, pull requests, commit messages | the UI and every string in it |
| `CHANGELOG.md`, the code and its comments | the German half of `README.md` |
| `CONTRIBUTING.md`, `AGENTS.md`, `SECURITY.md` | the GitHub release body |
| the bug report and feature request forms | the issue chooser, `docs/datenschutz-und-sicherheit.md`, the website |

**Discussions are deliberately exempt — German and English are both fine there.**
A Kommandant arriving from the README or the issue chooser is routed to
Discussions in German, which is what
[`.github/ISSUE_TEMPLATE/config.yml`](.github/ISSUE_TEMPLATE/config.yml) is for.
Feedback from an Übung is worth more than the language it arrives in. Turning a
German discussion into an English issue is a maintainer's job, not a hurdle put
in front of the person who found the bug.

## Pull requests

Every PR that touches the UI must include screenshots. The flow is:

1. Render the affected view(s) to PNG using the headless Skia harness in
   `tests/LageBuch.Acceptance.Tests` (see `WorkspaceRenderHelper` +
   `Window.CaptureRenderedFrame()` — `UseHeadlessDrawing = false` so embedded
   fonts rasterize).
2. Attach the PNGs directly with `gh`'s `--attach` flag — no manual pasting
   needed. It uploads to `github.com/user-attachments/assets` (the same
   pipeline the web UI's drag-and-drop uses) and rewrites any
   `![alt](./path.png)` reference in the body to point at the uploaded asset:
   - New PR: `gh pr create --attach './before.png#Before' --attach './after.png#After'`
   - Existing PR body: `gh pr edit <number> --attach './screenshot.png#Alt text'`
   - PR comment: `gh pr comment <number> --attach './screenshot.png#Alt text'`

For UI changes, provide a before/after pair. The render harness files are
diagnostic-only and should not be committed unless they double as a real test.

The README's screenshots and demo GIF come from one such test,
`DemoFlowRenderTests` — regenerate them with `make screenshots` and
`make demo-gif` whenever a view they show changes; `make samples` refreshes
the fictional `docs/samples/uebung.fwincident` after a schema change.

## Schema migrations

A feature branch that adds a migration owns version N only until `main` takes
N. Before merging, rebase and **renumber** the branch's migrations above
`main`'s current `Migrations.CurrentVersion` — two branches shipping different
"V18"s is how a file ends up stamped with a version whose migrations it never
received.

For the same reason, never run a branch build against real Einsatzdateien: it
stamps its own `schema_version` onto them, and the released app then skips
every migration numbered below it. `SchemaGuard` repairs missing columns after
the fact, but it cannot restore data a foreign build dropped, and it
deliberately does not touch tables.

Any migration that adds a column must also declare it in
`SchemaGuard.ExpectedColumns`
(`src/LageBuch.Persistence/Sqlite/SchemaGuard.cs`);
`SchemaReconciliationTests` fails the build otherwise.

## Sync protocol version

Two devices are allowed to sync when their **wire contracts** overlap, never
when their app versions match. `SyncProtocol.ProtocolVersion` and
`SyncProtocol.MinimumProtocolVersion` in `src/LageBuch.Sync/SyncProtocol.cs` are
that contract's own version and the oldest one this build still speaks; the app
version travels alongside them only so a refusal can name the build a human has
to update. Never gate on it — a fleet whose Android half waits on Play review is
the normal case, not a fault.

Any change to what crosses the wire — the `SyncCommand` `[JsonDerivedType]`
allowlist, `IncidentSnapshot`, the `/masterdata` payload — falls into one of two
cases, and picking the right one is the whole mechanism. There is deliberately
no per-feature capability negotiation:

- **A peer at the floor can ignore it.** A new optional command field with a
  defaulted constructor parameter (as `AddForceUnitCommand`'s `OfficerCount = 0`
  does), a new snapshot property an older client drops. Raise
  `ProtocolVersion` alone; old peers keep connecting.
- **A peer at the floor cannot handle it.** A new command a client may send, a
  renamed or removed field, a changed enum contract. Raise
  `MinimumProtocolVersion` to match — and check
  `LegacyProtocolVersion`, which encodes the claim that the pre-handshake v0.6.1
  contract is protocol 1.

The client does the two-sided overlap test, because it is the end that sees both
ranges. The host's own gate is one-sided on purpose: it refuses a peer below its
floor and lets a peer claiming something newer through, since that peer has
already read `/version` and chosen to speak down.

## Git commits

All commits must be:
- Semantic / Conventional Commits: subject starts with `feat:`, `fix:`,
  `docs:`, `style:`, `refactor:`, `perf:`, `test:`, `build:`, `ci:`, `chore:`
  (optionally scoped, e.g. `fix(backgroundjob):`)
- DCO signed-off: always pass `-s` to `git commit`
- Cryptographically signed, SSH or GPG: always pass `-S`, or set
  `commit.gpgsign = true`
- Written in English, subject and body — a squash-merge makes the pull request
  title the commit subject, so it is one rule; see [Language](#language)

The last two are project requirements, not local conventions — `main`'s branch
protection rejects a pull request carrying an unsigned commit, and the DCO
check fails one missing a `Signed-off-by` trailer. Neither can be merged past.
[`CONTRIBUTING.md`](CONTRIBUTING.md#signing-your-commits) has the setup and the
recovery recipe for a branch already pushed.

Signing in the sandbox: SSH signing (`gpg.format = ssh`) needs the SSH key,
which the command sandbox blocks — inside the sandbox `git commit` silently
produces an unsigned commit despite `commit.gpgsign = true`, and you find out
only when the merge is blocked. Run signing commits with the sandbox disabled
and verify before pushing with:

```bash
git cat-file commit HEAD | grep gpgsig
```

That check covers SSH signatures too: git stores them in the same `gpgsig`
header, so the absence of that header means the commit is unsigned whichever
format you use.

## Git push

NEVER push directly to `master` or `main`. Always use a feature branch and
open a PR. Absolute rule, no exceptions.

## GitHub Actions workflows

- Pin third-party actions to a full 40-char commit SHA, never a movable tag
  (`uses: owner/action@<sha> # vX.Y.Z`). First-party internal reusable
  workflows may follow repo convention.
- Always pin to the latest release: resolve with
  `gh api repos/<owner>/<action>/releases/latest --jq .tag_name`, then
  `gh api repos/<owner>/<action>/git/refs/tags/<tag> --jq .object.sha`.
- Every repo needs `.github/dependabot.yml` covering the `github-actions`
  ecosystem; add it if missing.

## Avalonia docs MCP

`.mcp.json` configures Avalonia's free, hosted
[Build MCP](https://docs.avaloniaui.net/tools/ai-tools/build-mcp) as
`avalonia-docs`. Claude Code asks once to approve it, as it does for every
project-scoped server. Use it — `lookup_avalonia_api`,
`search_avalonia_docs`, `get_avalonia_expert_rules` — before writing XAML or
reaching for a control API: this app is on Avalonia 12, much of what a model
remembers is Avalonia 11, and a deprecated member only fails a full rebuild
(see [Static analysis](#static-analysis)).

Avalonia's DevTools and Parcel MCP servers are deliberately not configured:
they need a paid Avalonia Plus licence, which a contributor cannot be expected
to have. For looking at a rendered view, the headless harness under
[Pull requests](#pull-requests) does the job.

## Architecture

These sections hold the standards you would otherwise have to ask for with an
"act as a senior .NET architect / security engineer" prompt. They are the
defaults for every change. Where the codebase already solves a problem one way,
use that way. Consistency beats a locally cleverer idea.

Dependencies point one way, and nothing may invert them:

- `Domain` references nothing.
- `Persistence`, `Documents` and `Sync` reference only `Domain`.
- `AppLogic` holds the view models and has **no Avalonia reference**, which is
  what makes it testable without a UI.
- `App.Shared` holds the views.
- `App` (desktop) and `App.Android` are thin hosts.

A project reference that breaks this order needs a reason in the PR.

- **View models** live in `src/LageBuch.AppLogic/ViewModels/`.
  - Write them as `sealed partial` classes over CommunityToolkit.Mvvm's
    `ObservableObject`, using `[ObservableProperty]` and `[RelayCommand]`.
  - They never touch `Dispatcher.UIThread`. Marshal through `IUiDispatcher`,
    and drive timers through `ITicker`.
- **No DI container.** Wiring is by hand, through constructor injection:
  - the desktop app in `CompositionRoot.cs` and `Program.cs`;
  - Android in `MainActivity.cs`.
  
  An optional dependency is a nullable constructor parameter with a default,
  e.g. `uiDispatcher ?? new ImmediateUiDispatcher()`. Do not introduce
  `ServiceCollection`.
- **Time is injected.**
  - Domain code takes an `IClock`; newer code takes a `TimeProvider`.
  - Never read `DateTime.Now` or `DateTimeOffset.Now` directly. `SystemClock`
    is the one place that does.
- **Types:**
  - Non-view classes are `sealed`.
  - Domain values are `sealed record`s.
  - Persisted state comes back through a `Rehydrate` factory, not public
    setters.
- **Smallest change that fits.**
  - Extend the existing pattern before inventing an abstraction.
  - Add an interface only when there is a second implementation, and a test
    double counts as one.
  - No speculative generality, and no drive-by refactors in a feature PR.

## C# and .NET

The analyzers already enforce formatting and naming. The rules below are about
what they cannot see.

- **Nullability is real.**
  - Never add `#nullable disable`.
  - Never use `!` to silence a warning. Fix the flow, or annotate it with
    `[MaybeNullWhen]` or `[NotNullWhen]`.
  - Public entry points start with `ArgumentNullException.ThrowIfNull`.
- **Failure has three shapes here.** Pick one of them; do not bring in a
  `Result<T>` library.
  - `bool TryX(..., out T)` with `[MaybeNullWhen(false)]`.
  - A nullable return whose `null` is documented.
  - A typed exception, such as `CertificateChangedException`.
- **Failures surface, they are not logged.** There is no logging framework.
  - A failure becomes a bound German status or error property on the view
    model, as `ExportStatus` and `PersistenceError` do.
  - A `catch` that ends in neither that nor a rethrow is a bug.
- **Async:**
  - No `async void` outside event handlers.
  - No `.Result` or `.Wait()`. The one audited exception is the Exit hook in
    `Program.cs`.
  - Write deliberate fire-and-forget as `_ = Task`, and only where the failure
    surfaces somewhere else.
  - No `ConfigureAwait(false)`: CA2007 is off because continuations must stay
    on the UI context.
  - New I/O or network APIs take `CancellationToken cancellationToken =
    default` and pass it on.
- **Culture:**
  - Anything the user reads is formatted through the fixed `de-DE` culture in
    `src/LageBuch.Domain/Formatting.cs`.
  - Anything persisted or sent over the wire uses `InvariantCulture`.
  - Keys and identifiers compare with `StringComparison.Ordinal`.
- **Dispose deterministically.** Use `using` or `await using` for every
  `IDisposable` you own.
- **Packages:**
  - Versions live in `Directory.Packages.props` (central package management).
    Never put a `Version` on a `PackageReference` in a `.csproj`.
  - Android and `LageBuch.Acceptance.Tests` keep their own nested props file.
  - A new dependency must earn its place in the PR description: it is
    maintained, it is licence-compatible, and its transitive tree is small.

## Avalonia UI

Use the `avalonia-docs` MCP described above before reaching for an API from
memory. The rules below are the ones this app has learned the hard way.

- **Compiled bindings are on by default.**
  - Every `.axaml` declares `x:DataType`.
  - `x:CompileBindings="False"` needs a comment saying why.
- **Code-behind is view-only.** It may handle focus and forward events to
  commands. No state and no decisions live in `.axaml.cs`; anything a test
  should cover belongs in the view model.
- **UI text is German and inline.** There are no `.resx` files.
  - The person documenting is the *Lagebuchführer*, never "Bediener". Code
    names stay `Operator`.
  - Keep wording short and imperative. It is read in a command vehicle during
    an operation.
- **Accessibility:** every icon-only or otherwise text-less control gets an
  `AutomationProperties.Name`. `AutomationPropertiesTests` holds the line.
- **Every new top-level `Window`** calls `WindowsTitleBar.ApplyDarkMode`.
  Windows 10 gets no dark title bar otherwise.
- **Layout traps that have shipped before:**
  - **Content-sized columns over virtualized lists.** A column with
    `MaxWidth` and `HorizontalAlignment="Center"` that contains a
    virtualizing list changes width while the user scrolls. Use `Stretch`
    instead.
  - **`Transparent` gradient stops.** A stop at `Transparent` fades through
    grey. Fade to a zero-alpha stop of the same hue, as `SignalFadedColor`
    in `Theme/Tokens.axaml` does.
  - **Ragged `Auto` columns.** Row `Grid`s inside an `ItemsControl` do not
    share `Auto` widths. Use `Grid.IsSharedSizeScope` with a
    `SharedSizeGroup`.
- **Views are shared with Android.** Anything in `App.Shared` must also work
  on a phone. A desktop-only API goes behind a service interface
  implemented in `App` and `App.Android`, the way the file dialogs are.

## Security

**Threat model.** Three kinds of input arrive from outside:

- a sync peer on a shared LAN;
- an `.fwincident` file someone handed over;
- a Stammdaten JSON import.

Treat all three as hostile. The data inside them is personal data about people
at an Einsatz, so confidentiality is a requirement, not a nice-to-have.

- **SQL.**
  - Values are always bound as `$name` parameters.
  - An identifier may be interpolated only from a compile-time schema
    constant, and the site carries a `CA2100` justification that says so.
  - Nothing that came from input ever reaches the SQL text.
- **Paths.** Beyond the `Path.Join` and sanitiser rules under
  [Static analysis](#static-analysis), a path built from outside input must be
  resolved with `Path.GetFullPath` and checked to start with the root plus a
  separator. `AttachmentTempPaths` shows the pattern.
- **Size.** Every input that crosses the trust boundary has a cap, enforced on
  both ends. An attachment, for example, is checked against
  `IncidentFile.MaxSizeBytes` in both the view model and the host.
- **Deserialization.**
  - Use System.Text.Json only, and `SyncJson.Options` for anything on the
    wire.
  - Polymorphism goes through a closed `[JsonDerivedType]` allowlist, as on
    `SyncCommand`.
  - Never use `BinaryFormatter`, Newtonsoft `TypeNameHandling`, or
    `Type.GetType` on input.
- **Sync transport.**
  - Certificates are pinned on first use through `ITrustStore`. Never add a
    certificate callback that accepts anything, not even temporarily and not
    even in a debug build.
  - PIN attempts stay rate-limited by `PinRateLimiter`.
  - Anything secret comes from `RandomNumberGenerator`, never `Random`.
- **Launching things.**
  - A URL is validated before it is opened (`HttpUrlValidator`, reached via
    `UrlLauncher`), and only its schemes are allowlisted.
  - A process started with `UseShellExecute = false` quotes its argument.
  - Nothing from input is ever concatenated into a command line.
- **No hand-rolled crypto.** Use BCL primitives only. Compare secret material
  with `CryptographicOperations.FixedTimeEquals`. The sync PIN is the
  documented exception; see the comment on `IncidentHost.PinMatches`.
- **Personal data stays out of diagnostics.**
  - No names, phone numbers or incident content in trace output.
  - Test fixtures and screenshots use fictional data (`AnonymizedExampleData`,
    `docs/samples/`).
  - Real Einsatzdateien are never committed.

## Tests

- **Tooling:**
  - xUnit with plain `Assert`, and handwritten test doubles. There is no
    mocking library and no FluentAssertions; do not add either.
  - Headless UI tests use `[AvaloniaFact]` in `LageBuch.Acceptance.Tests`.
- **Naming:** a test's name is a sentence in snake_case stating the behaviour,
  e.g. `Join_prompt_focuses_the_host_field_not_the_name_field`.
- **Bug fixes:** write the failing test first.
  - A security fix gets a test that replays the attack and shows it fail: the
    traversal name, the oversized body, the wrong PIN.
- **Time and waiting:** control time with `FakeTimeProvider` or a fixed
  `IClock`. A real `Task.Delay` or sleep in a test is a flake waiting to
  happen.

## Before you call it done

Before saying a task is finished, and again before opening a pull request,
review your whole diff through each of the following lenses. This review is the
standing replacement for the persona prompts.

**Architect**
- Does every project reference still point the way
  [Architecture](#architecture) says?
- Did I reuse the existing pattern, or invent a parallel one?
- Is every new type, interface and parameter needed by this change, not by an
  imagined future one?
- Is the diff as small as the change allows?

**Security**
- Does new input cross a trust boundary? If so, is it validated, size-capped,
  parameterized and path-contained?
- Can a failure surface to the user without leaking personal data or crashing
  the app mid-Einsatz?
- Did I weaken pinning, rate limiting or a sanitiser, even temporarily?

**UI** (when any `.axaml` changed)
- Does the view declare `x:DataType`, and do text-less controls carry
  automation names?
- Is the wording German and uses *Lagebuchführer*?
- Does it still work on Android?
- Is there a before/after screenshot pair ready for the PR?

**Evidence**
- Run a clean build with `make clean ci`. An incremental build hides XAML
  deprecations.
- When a test fails, search the log for `Test Run Failed`. The tail of the log
  says `0 Error(s)` even when a test assembly failed.
- "Done" means you saw the output, not that you expect it to pass.

## Static analysis

Two tools police this code and they overlap: the build runs the .NET analyzers
at `AnalysisMode=All` plus StyleCop with `TreatWarningsAsErrors`, and CodeQL
runs the `security-and-quality` suite on every pull request. The build sees
`[SuppressMessage]` attributes and `.editorconfig` severities; CodeQL does not.
A decision recorded only in code therefore comes back as a CodeQL alert unless
it is recorded in `.github/codeql/codeql-config.yml` too.

Write code that trips neither:

- **Join paths with `Path.Join`, never `Path.Combine`.** `Path.Combine` returns
  its later argument alone once that argument turns out to be rooted, silently
  discarding the directory you meant to stay inside; `Path.Join` always
  concatenates. This is defence in depth, not a substitute for sanitising: a
  name that crosses the trust boundary — a sync peer's file name, a content
  provider's `DISPLAY_NAME`, anything read back out of an incident file — still
  goes through `IncidentFile.SanitizeFileName` or `SafeFileName.Sanitize` first.
- **Give every `catch (Exception)` a per-member `[SuppressMessage("Design",
  "CA1031", Justification = "...")]`** that says where the failure surfaces —
  a bound error string, a documented `null` contract, an event. The build
  rejects the catch without one; the justification is also what makes CodeQL's
  duplicate finding defensible instead of an open alert nobody can explain.
- **Leave no dead locals.** `IDE0059` is a warning here and warnings are
  errors. Use `_` for a value you do not need, including inside a tuple
  deconstruction or an `out var` call.
- **Never compare `double` or `float` with `==` or `!=`** (`cs/equality-on-floats`).
  This bites hardest in UI tests, where it is tempting to find a control by a
  layout value — `OfType<StackPanel>().First(p => p.MaxWidth == 720)`. Find
  controls by `x:Name` instead, adding the name to the `.axaml` if it has none;
  that is what `OpenErrorBanner` and `RecentFilesHint` are for. Where a number
  really is the subject, assert on it with a tolerance — xUnit's
  `Assert.Equal(expected, actual, precision: 0)`, or `Math.Abs(a - b) <= 1.0` —
  and never with `Assert.Equal(0, someDouble)`, which is an exact comparison
  wearing a method call.
- **Never call an API the toolchain marks obsolete.** When the warning names
  the replacement, use it — `TextBox.Watermark` → `PlaceholderText`. On the C#
  side `TreatWarningsAsErrors` already fails the build (CS0612/0618/0619), but
  Avalonia's XAML compiler logs through MSBuild and slipped past it, so
  `Directory.Build.props` escalates `AVLN5001` separately. Note that a XAML
  deprecation only surfaces on a **full** rebuild — an incremental build skips
  the XAML compile, which is how a deprecated `Watermark` once shipped
  unnoticed. If a deprecation ever genuinely has to stand, suppress that one
  occurrence with a written justification; never widen `NoWarn`.
  A `PART_` name in a control template is not an obsolete member —
  `TextBox /template/ TextBlock#PART_Watermark` stays exactly as it is.

When a new CodeQL alert appears, never leave it open and never dismiss it
without a written reason:

1. **Fix it in code** if it is real. This is the default.
2. **If the query duplicates a rule the build already governs** — a CA or SA
   rule the repository has deliberately configured — exclude it by id in
   `.github/codeql/codeql-config.yml`, with a comment naming that rule and why
   the build's handling is the finer-grained one. Never exclude a query
   carrying a security severity.
3. **Only for a site-specific false positive**, dismiss that single alert with
   a comment explaining it. Prefer 1 and 2: a dismissal lives outside the
   repository, so no reviewer and no future reader ever sees the reasoning.

A `code_scanning` ruleset rule blocks a pull request that introduces a new
alert, so this is enforced rather than advisory.

Note that `paths` and `paths-ignore` do **not** work in the CodeQL config here:
GitHub restricts them to interpreted languages or `build-mode: none`, and this
is C# under `build-mode: manual`. Scope by rule id, or by the build target in
`codeql.yml`.

## Repository hygiene

Never commit `docs/superpowers/` — local tooling artifacts only. If tracked,
remove them and add to `.gitignore`.

## Changelog entries

Any PR touching `src/` must add **exactly one** changelog entry, as its own
file in `changelog.d/`, named `+<slug>.<type>.md`, where `<type>` is one of
`added`, `changed`, `deprecated`, `removed`, `fixed` or `security`. The file
holds the entry text as plain prose, with no leading `- `; reference issues
inline (`(#302)`). CI enforces that an entry exists — the `changelog entry` job
runs `towncrier check` — so do it in the same PR, not as a follow-up.

The entry is **English** and **one sentence**. `CHANGELOG.md` is one language
throughout, the way `README.md` is German for ELW crews and `SECURITY.md` is
English for security researchers; German terms of art stay German — Atemschutz,
Stammdaten, Einsatzdaten, Messreihe — because that is what the app says on
screen. The same job warns when an entry looks German, but only warns: it
cannot tell a Fachbegriff from a German sentence.

**Never write a pull request link into the fragment.** The rendered bullet
carries one — `- [#334](…/pull/334) - The PDF's Aufgaben section marks …` — but
it is recovered from git by `scripts/changelog-prlinks.py` when the release is
cut, because the number does not exist while the PR is open. An *issue*
reference inline is a different thing and stays in the prose.

One entry, not one per commit. A pull request is one change as far as the
release notes are concerned, and the entry describes what it does for the
reader, never the layers it was built in: a feature split across domain,
persistence and UI commits is still a single entry. A PR that also carries an
unrelated fix folds that into the same entry rather than adding a second file —
if the two really do not belong together, they are two pull requests.

Never edit `CHANGELOG.md` directly. It is assembled from `changelog.d/` by
`towncrier build` when a release is cut. Editing it from a feature branch is
what used to make every other open PR conflict, which is the whole reason for
the split. The leading `+` in the filename is required: it keeps filenames
unique so two PRs citing the same ticket cannot collide.

## The website

The user-facing website lives in a separate repository,
[`CodeForFire/codeforfire.github.io`](https://github.com/CodeForFire/codeforfire.github.io)
(Astro Starlight, served at <https://codeforfire.github.io/>). Two things
follow for work in *this* repository.

**Some of its content is built from here.** Its build copies
`docs/datenschutz-und-sicherheit.md`, `docs/logo/`, `docs/demo/einsatz-flow.gif`
and `docs/screenshots/*.png` straight out of this repo. Renaming or moving any
of those breaks the site build — deliberately, so the page is never silently
lost. If you move one, open a pull request there in the same breath. Content
changes need nothing: the site rebuilds nightly.

**Three things are maintained in both places and must be updated together**,
in the same pull request pair:

| Here | There |
|---|---|
| README's *Warum Lagebuch?* claims | `src/content/docs/index.mdx` |
| README's *Lagebuch im Vergleich* table | `src/content/docs/vergleich.md` |
| README's *Installation* and *Downloads prüfen* | `src/content/docs/download.md` |

A price, a platform or a competitor claim that changes in one and not the other
is the failure mode to avoid: the comparison table cites sources and is the
first thing a Kommandant checks.

## Master-data example file

`docs/master-data.example.json` is documented (README.md) as the full
Stammdaten schema reference. Whenever `MasterDataSet` or `MasterDataJson`
in `src/LageBuch.Persistence/MasterData/MasterDataSet.cs` gains, loses, or
renames a top-level field, update `docs/master-data.example.json` in the
same PR so it stays a complete, accurate example of every category.
