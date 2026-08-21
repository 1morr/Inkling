# Contributing

Inkling is a small, opinionated project. This is the short version of what to know before
opening an issue or a pull request.

## Issues

- **Bugs and feature requests**: use the issue templates. For bugs, the Inkling and Command
  Palette versions and — when the problem is inside the extension — its diagnostic log cut the
  guesswork; the template says how to get it.
- **Security or data-loss problems** (wrong file deleted, note content lost): see
  [SECURITY.md](SECURITY.md) and do not open a public issue.

## Pull requests

Welcome without asking first: bug fixes, documentation fixes, UI-string corrections, small
improvements. For anything that **changes behaviour or adds a feature, open an issue first** —
much of the design is constrained by what Command Palette can and cannot do, and several
obvious-looking ideas have already been tried, measured, and written down in
[docs/design-notes.md](docs/design-notes.md) (Traditional Chinese). A short discussion saves
a rewrite.

Before you start:

1. Read [docs/development.md](docs/development.md) (Traditional Chinese) — build, deploy,
   project layout, troubleshooting. Everything under `src/Inkling.Core` is unit-tested.
   `src/Inkling`, the thin Command Palette layer, has tests too (`tests/Inkling.Tests`, run
   separately — it is deliberately outside the solution), but they only cover what does not
   need Command Palette running: command order and shortcuts, cache keys, unsubscribing on
   dispose. The screen itself is verified by hand
   ([docs/manual-test-checklist.md](docs/manual-test-checklist.md), driven by
   `tools\cmdpal-ui.ps1`).
2. Read [CLAUDE.md](CLAUDE.md) (Traditional Chinese). Despite the name it is the
   repository's rulebook — the architecture, the hard-won rules for dealing with Command
   Palette, and the conventions — written for AI coding assistants and humans alike.

The rules that bite:

- **UI strings never live in code.** They go into all three `.resx` files under
  `src/Inkling/Properties/` (English is the neutral one); `ResourceParityTests` fails when one
  is missing or a placeholder does not match.
- **Logic that can live in `Inkling.Core` goes there, with tests.** `dotnet test` must pass.
- **Docs move with the code, in the same PR.** Commands, settings, the file format, or any
  user-visible behaviour → both `README.md` and `README.zh-Hant.md` (they are one document in
  two languages; keep the sections and table rows aligned) plus
  `docs/manual-test-checklist.md`; build or deploy changes → `docs/development.md`;
  and an entry under `[Unreleased]` in `CHANGELOG.md`.
- **Top-level command Ids** (`src/Inkling/CommandIds.cs`) **and the package identity are
  promises to users** — renaming them wipes people's aliases, hotkeys, and settings.
- **Commit messages** follow Conventional Commits: imperative, lower-case subject, no trailing
  period, ≤ 72 characters.

The maintainer docs and the code comments are Traditional Chinese only; the two READMEs are
the exception. [CLAUDE.md](CLAUDE.md#docs-language) says why. Contribute in English — the
translation is handled on the way in.
