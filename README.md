<p align="center">
  <img src="assets/gallery/icon.png" width="96" alt="Inkling">
</p>
<h1 align="center">Inkling</h1>
<p align="center">Take notes without leaving PowerToys Command Palette.<br>Type your thought, press Enter, and it is saved as a Markdown file in a folder you choose.</p>
<p align="center">
  <a href="https://github.com/1morr/Inkling/actions/workflows/ci.yml"><img src="https://github.com/1morr/Inkling/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://apps.microsoft.com/detail/9NDGWN4JTXHH"><img src="https://img.shields.io/badge/Microsoft%20Store-Inkling%20Notes-0078D4" alt="Microsoft Store"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/1morr/Inkling" alt="License: MIT"></a>
</p>
<p align="center"><b>English</b> · <a href="README.zh-Hant.md">繁體中文</a></p>

Inkling adds note taking to Command Palette: capture a thought in seconds, then
browse, search, and edit your notes without opening another app. Sync, phone
access, and heavier editing come from the cloud drive and editor you already use.

![Top-level commands](docs/images/top-level-commands.png)

## Install

**[Microsoft Store — Inkling Notes](https://apps.microsoft.com/detail/9NDGWN4JTXHH)**,
or `winget install --source msstore --id 9NDGWN4JTXHH`. Inside Command Palette
the commands are just "Inkling".

| Requires | |
|---|---|
| Windows | 10.0.19041 or later |
| Command Palette | 0.11 or later (the standalone `Microsoft.CommandPalette` package) |

No .NET install needed — the published package is self-contained. The assets on
GitHub Releases are CI builds for the Store submission and are **not signed**, so
Windows will not sideload them; to build it yourself see
[docs/development.md](docs/development.md).

## Getting started

Summon Command Palette, type `Inkling`, and pick **Inkling: Quick capture**.
Type your thought and press Enter. That is the whole loop.

To write the body in the same breath, use the `;;` separator:
`coffee machine idea;;Look up pour-over vs. espresso first`. Everything before it
is the title, everything after is the body. A title on its own is a perfectly
good note.

**Want it in fewer keystrokes?** Give Quick capture an alias: CmdPal Settings →
Extensions → Inkling → `Inkling: Quick capture` → Alias. With `!` set, typing `!`
and a space opens capture directly. Punctuation beats a letter, which would
collide with real searches. A global hotkey skips the search box entirely.

![Quick capture](docs/images/quick-capture.gif)

## Features

The UI follows your Windows display language — English, Traditional Chinese, or
Simplified Chinese. Screenshots here were taken on an English system.

| | |
|---|---|
| Quick capture | Type and press Enter. Notes with similar titles are listed underneath so the same thing is not captured twice. Multi-line text on the clipboard offers itself as the body ([why](docs/design-notes.md#paste-multiline)) |
| Preview after capture | Saving keeps you on the note so you can check it; Enter again closes the palette. Switchable in Settings |
| Browse and search | Titles and bodies are both searched, multiple words are AND-ed, title matches rank first. With no matches, Enter goes straight to quick capture |
| Markdown preview | Enter renders the note. `Ctrl+U` toggles raw source and remembers the choice — useful for pasted HTML that vanishes when rendered |
| Edit | `Ctrl+E` opens the built-in form; `Ctrl+O` opens the file in your default editor instead. Saving closes the palette, the same as creating a note |
| Copy and locate | `Ctrl+Shift+C` copies the body without the front matter; `Ctrl+L` shows the `.md` in File Explorer |
| Scratchpad | A note that is always there, for anything that needs no title. No autosave ([why](docs/design-notes.md#scratchpad-no-autosave)) — `Tab` then `Enter` saves and closes |
| Delete | `Ctrl+D` deletes after confirming, and the file goes to the Recycle Bin. `Inkling: Delete notes` handles many at once and always confirms before touching files Inkling did not create |

Archiving, tags, and pinning are not built yet.

![Notes list](docs/images/note-list.png)

## Keyboard shortcuts

With a note selected, on the list and preview pages:

| Key | Action |
|---|---|
| `Ctrl+E` | Edit (form) |
| `Ctrl+N` | New note (list page only) |
| `Ctrl+U` | Toggle rendered / source |
| `Ctrl+Shift+C` | Copy body |
| `Ctrl+O` | Open in the default app |
| `Ctrl+L` | Select the file in File Explorer |
| `Ctrl+D` | Delete (list page only) |
| `Ctrl+K` | Open the menu — every item shows its own key |

Only Copy carries Shift, because `Ctrl+C` belongs to the search box.
[Which letters are off limits and why](docs/design-notes.md#list-shortcuts).

`Enter` and `Ctrl+Enter` press the two buttons in the bottom toolbar, so what
they do depends on the page — the toolbar always shows the current pair. On the
edit form, save with `Tab` to "Save" then `Enter`; `Esc` leaves without saving.
[Why the pages are deliberately mirrored](docs/design-notes.md#secondary-command).

CmdPal does not let users rebind an extension's shortcuts. The aliases and
global hotkeys of top-level commands are yours to configure.

## What a note looks like

```markdown
---
id: 20260810-143052-a7f3
title: coffee machine idea
created: 2026-08-10T14:30:52+08:00
updated: 2026-08-11T09:15:00+08:00
---

Look up pour-over vs. espresso first.
```

The data format is a promise:

- **The `id` is the note's identity; the file name is only for humans.** Renaming
  a title never renames the file, which keeps cloud-synced folders out of trouble.
- **Front matter Inkling does not understand is left alone**, including fields
  added by Obsidian and dates it cannot parse.
- **`.md` files without front matter show up too**, with the title taken from the
  first meaningful line. Point Inkling at an existing notes folder and it just
  works; those files are never rewritten.
- **Files must be UTF-8** (a BOM is fine). Anything else is skipped rather than
  read as garbage, and the list says how many were skipped. Subfolders are
  scanned; new notes go in the root.
- **The scratchpad is `scratchpad.md` in the root**, plain text with no front
  matter, hidden from the list and from search.

## Settings

Select the **Inkling** row in the main search box and press `Ctrl+K` → Settings,
or CmdPal Settings → Extensions → Inkling.

| Setting | Default |
|---|---|
| Notes folder | `%OneDrive%\Inkling`, or `Documents\Inkling` without OneDrive. Full paths only; created on first save. "Browse…" opens the folder picker |
| Quick capture separator | `;;` — any length; half-width and full-width count as the same; clearing it restores the default |
| Preview after capture | On |

## Sync

Inkling **does not sync**. It writes Markdown into the folder you choose and
leaves offline availability, conflict handling, and phone access to your cloud
drive. Obsidian and similar tools can point at the same folder.

**OneDrive users**: mark the folder "Always keep on this device". With Files
On-Demand, cloud-only placeholders trigger a download on read and search stalls.
If two machines edit one note at once, OneDrive makes a `name-ComputerName.md`
copy; both rows appear, tagged **Conflict copy**, and each acts on its own file.

## Troubleshooting

**Changed a setting and nothing happened** — close the settings page and reopen
it. That page is bound to one extension instance, so after a reload Save
silently does nothing.

**Wrong UI language** — it follows the Windows *display* language, not the
regional format, and there is no override. Sign out and back in after changing it.

**Your settings reverted to the defaults** — if `settings.json` was left invalid,
Inkling moves it aside as `settings.json.corrupt-<timestamp>` and starts fresh
rather than failing silently forever. The old values are still in that file.

Everything else — deploy, registration, trimming, the diagnostic log — is in
[docs/development.md](docs/development.md#troubleshooting).

## Documentation

The in-depth docs are written in Traditional Chinese.

| | |
|---|---|
| [docs/development.md](docs/development.md) | Build, deploy, project layout, troubleshooting |
| [docs/design-notes.md](docs/design-notes.md) | The reasoning behind every decision, for maintainers and other CmdPal extension authors |
| [docs/known-issues.md](docs/known-issues.md) | Known defects, each with a reproduction |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Read before changing this repo |
| [PRIVACY.md](PRIVACY.md) | What the app collects and sends (nothing, and nothing) |

## Contributing

Bug reports and feature requests are welcome — use the issue templates. Before
opening a PR read [CONTRIBUTING.md](CONTRIBUTING.md); this repo has a few rules
that are not obvious.

## License

[MIT](LICENSE)
