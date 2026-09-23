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
