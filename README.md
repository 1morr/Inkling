<p align="center">
  <img src="assets/gallery/icon.png" width="96" alt="Inkling">
</p>
<h1 align="center">Inkling</h1>
<p align="center">Take notes without leaving PowerToys Command Palette. Type your thought, press Enter, and it is saved as a Markdown file in a folder you choose.</p>
<p align="center">
  <a href="https://github.com/1morr/Inkling/actions/workflows/ci.yml"><img src="https://github.com/1morr/Inkling/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/1morr/Inkling" alt="License: MIT"></a>
</p>
<p align="center"><b>English</b> · <a href="README.zh-Hant.md">繁體中文</a></p>

<!-- This file and README.zh-Hant.md are the same document in two languages: change one,
     change the other (same sections, same table rows).

     Outward-facing copy has a single source: docs/copy.md. The pitch above, the Store listing
     in three languages, the gallery card, and the manifest Description all live there — change
     that file first, then carry the wording here. The rules that copy follows (an alias is
     optional, not step one; say what it does, not why; keep implementation vocabulary out of
     the Store) are at the top of the same file.

     The icon at the top is assets/gallery/icon.png straight from render-icons.ps1 —
     edit the SVG and rerun the script and this picks it up; do not add a second copy. -->

Inkling adds note taking to Command Palette: capture a thought in seconds, then browse, search,
and edit your notes without opening another app. Sync, phone access, and heavier editing come
from the cloud drive and editor you already use.

![Top-level commands](docs/images/top-level-commands.png)

<!-- Screenshots are taken on a real machine with the `shot` action of tools\cmdpal-ui.ps1
     (PrintWindow; see that script and .claude/skills/verify-cmdpal-ui). Retake them whenever
     icons, command titles, or layout change — and point the notes folder at a demo folder
     first, so real notes never land in a public repo. The GIF is a burst of shots from the
     same script stitched with ffmpeg; the procedure is in docs/development.md. -->

The UI follows your Windows display language (English, Traditional Chinese, Simplified Chinese);
the screenshots here were taken on an English system.

## Requirements

| | |
|---|---|
| Windows | 10.0.19041 or later |
| Command Palette | 0.11 or later (the standalone MSIX package `Microsoft.CommandPalette`) |

**No .NET install needed** — the published package is self-contained.

## Install

**Microsoft Store** — [Inkling Notes](https://apps.microsoft.com/detail/9NDGWN4JTXHH). The Store
name is "Inkling Notes"; inside Command Palette the commands are just "Inkling".

WinGet installs the same package: `winget install --source msstore --id 9NDGWN4JTXHH`.

The assets attached to GitHub Releases are built by CI for the Store submission and are **not
signed**, so Windows will not sideload them. To build it yourself:
[docs/development.md](docs/development.md) (Traditional Chinese) — `tools\deploy.ps1` builds,
registers, and reloads in one command.

## Getting started

Summon Command Palette, type `Inkling`, and pick **Inkling: Quick capture**. Type your thought
and press Enter. That is the whole loop.

To write the body in the same breath, use the separator:
`coffee machine idea;;Look up pour-over vs. espresso first`. Everything before `;;` is the title,
everything after it is the body. The separator is optional; a title on its own is a perfectly
good note.

**Want it in fewer keystrokes?** Give Quick capture an alias: CmdPal Settings → Extensions →
Inkling → `Inkling: Quick capture` → Alias. With `!` set, typing `!` and a space in the palette
opens capture directly. Punctuation beats a letter here, because a letter collides with real
searches. A global hotkey skips the search box entirely. The notes list and New note can have
aliases too (the `#` and `@` on the right of the first screenshot).

![Quick capture](docs/images/quick-capture.gif)

## Features

| | |
|---|---|
| Quick capture | Type and press Enter to save. `<title>;;<body>` writes both at once (the separator is configurable). Notes with similar titles are listed underneath so the same thing is not captured twice. When the clipboard holds multi-line text, an extra row uses it as the body ([why](docs/design-notes.md#paste-multiline)) |
| Preview after capture | Saving keeps you on the note so you can check it; Enter again closes the palette and confirms what was saved. On by default, switchable in Settings |
| New note (full form) | `Inkling: New note`, or `Ctrl+N` on the list page, opens a form with a multi-line body. Saving confirms and closes the palette; editing an existing note stays put instead ([why](docs/design-notes.md#edit-form)) |
| Browse and search | Titles and bodies are both searched, multiple words are AND-ed, and title matches rank first. The subtitle is the first line of the body. With no matches, Enter goes straight to quick capture ([the two empty states](docs/design-notes.md#empty-content)) |
| Markdown preview | Select a note and press Enter to see it rendered. A single newline you typed shows as a line break, and the file on disk is left alone ([why](docs/design-notes.md#preview-line-breaks)) |
| Source view | `Ctrl+U` switches between rendered and raw text, and the choice is remembered. Pasted HTML or SVG that disappears when rendered is visible here ([details](docs/design-notes.md#source-mode)) |
| Edit | `Ctrl+E` opens the built-in form: `Tab` to "Save", then `Enter`. `Ctrl+O` opens the file in your default editor instead |
| Copy body / show file | `Ctrl+Shift+C` copies the body without the front matter and names the note it copied at the bottom of the palette ([why it says so](docs/design-notes.md#copy-feedback)). `Ctrl+L` shows the `.md` in File Explorer |
| After jumping out | `Ctrl+O` and `Ctrl+L` hand off to another app and the palette steps aside; the hotkey brings it back on the same page ([why](docs/design-notes.md#open-external-return)). The edit form and the scratchpad close the palette instead, so an old copy on screen cannot overwrite what you just edited outside. If the file moved, or nothing is registered to open `.md`, the reason shows at the bottom |
| Scratchpad | `Inkling: Scratchpad` is one permanent note for anything that does not need a title. There is no autosave ([why](docs/design-notes.md#scratchpad-no-autosave)): `Tab` then `Enter` saves and closes the palette. `Ctrl+O` opens it in your default editor |
| Delete | `Ctrl+D` on the list page deletes after a confirmation, and the file goes to the Recycle Bin. The selection moves to the next note, so you can delete several in a row ([why that takes work](docs/design-notes.md#selection-survives-rebuild)). Network drives and devices without a Recycle Bin delete for good; the Delete notes page says so in its details pane |
| Delete many / clear all | `Inkling: Delete notes` deletes with `Enter` (asks once) or `Ctrl+Enter` (immediately). "Delete all" lists the files it would remove first, and files not created by Inkling are always confirmed ([why those two keys](docs/design-notes.md#delete-keys)) |
| UI language | English, Traditional Chinese, Simplified Chinese, following the Windows display language — [no setting](docs/design-notes.md#ui-language) |

Archiving, tags, and pinning are not built yet. The `tags` field is understood, but is not written when empty.

![Notes list](docs/images/note-list.png)

## Keyboard shortcuts

On the list page and the preview page, with a note selected:

| Key | Action | List | Preview |
|---|---|:-:|:-:|
| `Ctrl+E` | Edit (form) | ✅ | ✅ |
| `Ctrl+N` | New note (form) | ✅ | — |
| `Ctrl+U` | Toggle rendered / source (global, remembered) | ✅ | ✅ |
| `Ctrl+Shift+C` | Copy body | ✅ | ✅ |
| `Ctrl+O` | Open the `.md` with the system default app | ✅ | ✅ |
| `Ctrl+L` | Select the file in File Explorer | ✅ | ✅ |
| `Ctrl+D` | Delete (confirmation first) | ✅ | — |
| `Ctrl+K` | Open the menu; every item shows its own key | ✅ | ✅ |

**`Enter` and `Ctrl+Enter` press the two buttons in the bottom toolbar**, so what they do depends
on the page:

| Page | `Enter` | `Ctrl+Enter` |
|---|---|---|
| Notes list | Preview | Edit |
| Preview | Edit | Done (dismiss) |
| Capture and preview (where quick capture's Enter lands) | Done (dismiss) | Edit |
| `Inkling: Delete notes` | Delete (asks once) | Delete now |
| Edit form | Keep editing (does nothing) | Open in default editor |
| Scratchpad | Discard changes (inside the text box `Enter` is a newline — `Tab` out first) | Open in default editor |

On the edit form, save with `Tab` to "Save" and then `Enter`; the form stays open, and `Esc` goes
back. `Enter` in the title field does nothing on purpose. The scratchpad saves the same way and
closes the palette, telling you what happened. Why the preview and capture-and-preview pages are
deliberately mirrored: [design notes](docs/design-notes.md#secondary-command).

**Only Copy carries Shift**, because `Ctrl+C` belongs to the search box. Which letters are off
limits and why Delete is `Ctrl+D`: [design notes](docs/design-notes.md#list-shortcuts).
**CmdPal does not let users rebind an extension's shortcuts**; the aliases and global hotkeys of
top-level commands are yours to configure.

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

- **The `id` is the note's identity, and the file name is only for humans.** Renaming a title does
  not rename the file, which keeps cloud-synced folders out of trouble. Which *file* a row acts on
  is decided by its path, so a cloud conflict copy stays independently editable.
- **Front matter Inkling does not understand is kept as it is**, including dates it cannot parse
  and fields such as `aliases` or `cssclass` added by Obsidian. `created` and `updated` are written
  as ISO 8601, and an empty `tags` is not written at all.
- **`.md` files without front matter show up in the list too**, with the title taken from the first
  meaningful line and the timestamps from the file. Point Inkling at an existing notes folder and
  it just works. Those files are never rewritten by Inkling, and the delete pages always confirm
  before touching them ([why](docs/design-notes.md#delete-page)).
- **Files must be UTF-8** (a BOM is fine). Anything else is skipped rather than read as garbage,
  and the list says how many were skipped. Subfolders are scanned; new notes go in the root.
- **The scratchpad is `scratchpad.md` in the root of the same folder**, plain text with no front
  matter, so any other editor opens exactly what you typed. It does not appear in the list or in
  search, and a `scratchpad.md` inside a subfolder is just a note.

## Settings

Select the **Inkling** row in the main search box and press `Ctrl+K` → Settings
(`Ctrl+Enter` goes straight there), or CmdPal Settings → Extensions → Inkling.

| Setting | Default | Notes |
|---|---|---|
| Notes folder | `%OneDrive%\Inkling`, or `Documents\Inkling` when there is no OneDrive | Full paths only. A folder that does not exist yet is created on first save. "Browse…" opens the system folder picker and saves your choice on the spot |
| Quick capture separator | `;;` | Title before it, body after. Any length; half-width and full-width count as the same; clearing it restores `;;`. A quick capture page that is already open picks up the change |
| Preview after capture | On | Enter captures and stays on the note, and a second Enter closes the palette. Off means capture and close at once |

A successful save shows a toast and returns you to the main search box. A rejected one (relative
path, write failure) keeps the form up with your values so you can fix it. Where the settings file
lives, its format, and whether it survives an update:
[docs/development.md](docs/development.md#settings-file).

## Sync

Inkling **does not sync**. It writes Markdown files into the folder you choose and leaves the rest
to your cloud drive: offline availability, conflict handling, and phone access are whatever
OneDrive or Dropbox already give you. To read your notes on a phone, install that drive's app;
Obsidian and similar tools can point at the same folder.

**OneDrive users**: mark the Inkling folder "Always keep on this device" (right-click the folder).
With Files On-Demand, cloud-only placeholders trigger a download on read and search stalls. If two
machines edit the same note at once, OneDrive creates a `name-ComputerName.md` copy; both rows show
up in the list, tagged **Conflict copy**. Each row acts on its own file, so compare the two in the
details pane, keep the one you want, and delete the other.

## Troubleshooting

**Changed a setting and nothing happened** — close the settings page and reopen it. That page is
bound to one extension instance, so after a reload or a redeploy, Save silently does nothing.

**Wrong UI language** — the language follows the Windows **display language** (not the "regional
format" setting) and there is no override. After changing it, sign out and back in.

**Your settings went back to the defaults** — if `settings.json` was left in a state that is not
valid JSON (an editor mishap, a crash mid-write), Inkling moves it aside as
`settings.json.corrupt-<timestamp>` and starts from the defaults, so that saving works again
instead of failing silently forever. The settings page says so at the top and names the file; the
old values are still in it if you want to pick them back out.

Everything else (deploy, registration, trimming, the extension's own diagnostic log):
[docs/development.md → troubleshooting](docs/development.md#troubleshooting).

## Documentation

The in-depth docs are written in Traditional Chinese.

| | |
|---|---|
| [docs/development.md](docs/development.md) | Build, deploy, project layout, troubleshooting |
| [docs/design-notes.md](docs/design-notes.md) | The full "why" behind every decision (why quick capture is a page and not a fallback, why feedback has exactly three channels, why the confirm dialog's buttons have no color…), for maintainers and other CmdPal extension authors |
| [docs/copy.md](docs/copy.md) | The source of every outward-facing string: Store listing, gallery card, manifest description |
| [docs/known-issues.md](docs/known-issues.md) | Defects that are known and not yet fixed, each with a reproduction |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Read before changing this repo |
| [PRIVACY.md](PRIVACY.md) | What the app collects and sends (nothing, and nothing) |

## Contributing

Bug reports and feature requests are welcome (use the issue templates). Before opening a PR,
read [CONTRIBUTING.md](CONTRIBUTING.md) — this repo has a few rules that are not obvious
(UI strings live in three `.resx` files that change together; behavior changes update both
READMEs and the manual test checklist in the same change).

## License

[MIT](LICENSE)
