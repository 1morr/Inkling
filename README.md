<p align="center">
  <img src="assets/gallery/icon.png" width="96" alt="Inkling">
</p>
<h1 align="center">Inkling</h1>
<p align="center">Summon PowerToys Command Palette, type, press Enter — the thought is saved as a Markdown file in a folder you choose.</p>
<p align="center">
  <a href="https://github.com/1morr/Inkling/actions/workflows/ci.yml"><img src="https://github.com/1morr/Inkling/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/1morr/Inkling" alt="License: MIT"></a>
</p>
<p align="center"><b>English</b> · <a href="README.zh-Hant.md">繁體中文</a></p>

<!-- This file and README.zh-Hant.md are the same document in two languages: change one,
     change the other (same sections, same table rows).

     The English pitch above is the reference wording. It is NOT copied verbatim anywhere:
     three slots say the same thing at three lengths, each cut to fit, and they are
     deliberately different sentences —

       1. this pitch (one line, em dash, room to name the file format);
       2. docs/gallery/extension.json  shortDescription (two short sentences, reads
          standalone on a gallery card);
       3. Package.appxmanifest  uap:VisualElements/@Description (shortest; Windows shows
          it in the app list, so it has to survive being truncated).

     Change what the product claims and all three move together. Change the phrasing of
     one and the others can stay.

     The icon at the top is assets/gallery/icon.png straight from render-icons.ps1 —
     edit the SVG and rerun the script and this picks it up; do not add a second copy. -->

Capture a thought in seconds without leaving the keyboard. Sync, mobile access, and editing
come from whatever cloud drive and editor you already use; Inkling itself contains zero sync code.

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

**Not published yet.** There is no GitHub Release, no WinGet package, and no Microsoft Store
listing so far — the package identity and code signing are still being settled
(see [docs/release-checklist.md](docs/release-checklist.md), Traditional Chinese).

**Signing decides the order**, because Windows will not sideload an unsigned MSIX. The plan
is Store first: it signs the package for you, which is what makes the other channels possible.
Each one gets its instructions here as it lands:

1. **Microsoft Store**, and from there the CmdPal Extension Gallery (the gallery needs a
   Store or WinGet id).
2. **WinGet** — `winget install <id>`, pointing at the signed package; it carries the
   `windows-commandpalette-extension` tag, so it will also show up when you search from
   inside Command Palette.
3. **GitHub Releases** — the release workflow already builds a `.msixbundle` on every `v*`
   tag, but **until there is a certificate those assets are unsigned**: they exist for Store
   submission, not for sideloading.

To use it today, build from source: [docs/development.md](docs/development.md)
(Traditional Chinese) — `tools\deploy.ps1` builds, registers, and reloads in one command.

## Getting started

Once installed, **set an alias first**; otherwise quick capture is a long scroll down from
the main search box: CmdPal Settings → Extensions → Inkling → `Inkling: Quick capture` → Alias `!`.

> **Pick punctuation, not a letter.** A letter collides with real searches (every query starting
> with `n` would trigger it); punctuation does not. For even fewer keystrokes give it a global
> hotkey and skip the `!` altogether. The notes list and New note can get aliases too
> (the `#` and `@` on the right of the first screenshot).

Then: summon Command Palette → type `!` and a space → the quick capture page opens →
type `coffee machine idea;;Look up pour-over vs. espresso first` → Enter. Everything before
the separator is the title, everything after it is the body. Saved, hands never left the
keyboard — and the separator is optional, a title on its own is a perfectly good note.

![Quick capture](docs/images/quick-capture.gif)

## Features

| | |
|---|---|
| Quick capture | Type and it is saved. To add a body in the same breath, type `<title>;;<body>` (the separator is configurable). Existing notes with similar titles are listed underneath so the same thing is not captured twice. When the clipboard holds multi-line text an extra row, "Body from the clipboard", gets around the single-line search box ([why](docs/design-notes.md#paste-multiline)) |
| Preview after capture | After saving, stay on the note to check it, then press Enter once more to dismiss — that dismissal shows "Captured: title", the same message you get with this option turned off. On by default; switch it off in Settings |
| New note (full form) | `Inkling: New note` opens a form with a multi-line body; `Ctrl+N` on the list page opens it too |
| Browse and search | Titles and bodies are both searched, multiple words are AND-ed, title hits rank first; the subtitle is the first line of the body. No hits says so — "No matching notes" — and Enter jumps straight into quick capture ([the two empty states](docs/design-notes.md#empty-content)) |
| Markdown preview | Select a note, press Enter to see it rendered. **A single newline you typed shows as a line break**, while the `.md` on disk is not touched ([why](docs/design-notes.md#preview-line-breaks)) |
| Source view | `Ctrl+U` toggles between rendered and raw text; **the state is global and remembered**. Pasted HTML / SVG that vanishes when rendered is visible here ([details](docs/design-notes.md#source-mode)) |
| Edit | Form-based editing (`Ctrl+E`), Tab to "Save" and press Enter; or "Open in default editor" (`Ctrl+O`) and edit outside |
| Copy body / Open file location | `Ctrl+Shift+C` copies the body (without front matter, **the palette stays open** — [why that matters](docs/design-notes.md#copy-feedback)); `Ctrl+L` selects the `.md` in File Explorer |
| After jumping out | After `Ctrl+O` or `Ctrl+L` hands off to another app, the palette only steps aside — the hotkey brings it back **on the same page** ([why](docs/design-notes.md#open-external-return)). Two pages are the exception and dismiss the palette instead: the edit form and the scratchpad, because both hold a copy you could still save over the file you just went out to edit. If the file was renamed or moved, or nothing is registered to open `.md`, the reason shows at the bottom instead of [silently doing nothing](docs/design-notes.md#open-external-silent) |
| Scratchpad | `Inkling: Scratchpad` is one permanent sticky note: it opens with whatever you left there, no title required. **No autosave** (CmdPal cannot do it, [why](docs/design-notes.md#scratchpad-no-autosave)): `Tab` → `Enter` saves, **shows "Saved to scratchpad" and dismisses the palette by itself** (Discard changes says so too); `Ctrl+O` opens it in the system's default editor, where autosave lives |
| Delete | `Ctrl+D` on the list page; after confirming, the file **goes to the Recycle Bin**. On a network drive, or a device without a Recycle Bin, Windows deletes it for good instead — the **Delete notes** page spells this out in its details pane |
| Delete many / clear all | `Inkling: Delete notes` opens a page where `Enter` deletes (asks once) and `Ctrl+Enter` deletes immediately; "Delete all" on the same page lists which files it would remove first. Files not created by Inkling are always confirmed on both paths ([why those two keys](docs/design-notes.md#delete-keys)) |
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

**`Enter` and `Ctrl+Enter` are positional, not bound to a command**: they press the two fixed
buttons in the bottom toolbar, and which commands sit there is decided by command order alone.
So each page gives `Enter` the natural next step on that path:

| Page | `Enter` | `Ctrl+Enter` |
|---|---|---|
| Notes list | Preview | Edit |
| Preview | Edit | Done (dismiss) |
| Capture and preview (where quick capture's Enter lands) | Done (dismiss) | Edit |
| `Inkling: Delete notes` | Delete (asks once) | Delete now |
| Edit form | Keep editing (does nothing) | Open in default editor |
| Scratchpad | Discard changes (inside the text box `Enter` is a newline — `Tab` out first) | Open in default editor |

On the edit form, **saving is `Tab` to "Save", then `Enter`**, and the form **stays open** —
press `Esc` to go back. `Enter` from the single-line title field is deliberately harmless there:
it used to jump straight to the external editor and dismiss the palette, dropping whatever you
had typed into the card.

On the scratchpad, **saving is `Tab` to "Save", then `Enter`**, and the palette dismisses itself;
both exits say what just happened, because a vanishing palette alone cannot tell "saved" from
"not saved". Why the preview and capture-and-preview pages are **deliberately mirrored**:
[design notes, "two positional keys"](docs/design-notes.md#secondary-command).

**Only Copy carries Shift**: `Ctrl+C` belongs to the search box. Which letters are off limits and
why Delete is `Ctrl+D`: [design notes, "list page shortcuts"](docs/design-notes.md#list-shortcuts).
**CmdPal does not let users rebind an extension's shortcuts**; only the aliases and global hotkeys
of top-level commands are configurable.

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

The data format is a promise. A few deliberate choices:

- **`id` is the identity; the file name is only for humans.** Renaming a title does not rename
  the file — frequent renames inside a cloud-synced folder are the top source of duplicate and
  conflict copies. Which *file* a row acts on is decided by its path, not by `id`, so a cloud
  conflict copy (same `id`, different file) stays independently editable.
- **Dates it cannot read are left alone.** `created`/`updated` are written and read as ISO 8601
  (`2026-08-10T14:30:52+08:00`). Anything else — `2024-01-05 (approx)`, or the ambiguous
  `05/01/2024` — is kept verbatim instead of being guessed at and rewritten.
- **Unknown front matter fields are preserved as-is.** `aliases` or `cssclass` added by Obsidian
  or similar tools survive a round of editing in Inkling. An empty `tags` is not written.
- **`.md` files without front matter still show up in the list**, with the title taken from the
  first meaningful line of the body and the timestamps from the file. Point Inkling at an existing
  notes folder and it just works. Such files are marked (`Note.IsExternal`): browsing treats them
  the same, only the delete paths handle them separately ([why](docs/design-notes.md#delete-page)).
  "Created by Inkling" means the `id` has the shape Inkling generates — an `id:` of your own
  (Zettelkasten, Hugo, Obsidian) keeps the file on the *not mine* side, and Inkling never
  overwrites it.
- **Files must be UTF-8.** Anything else (Big5, GBK, Latin-1 without a BOM) is skipped rather
  than read as garbage, and the list says how many were skipped. Files with a BOM — UTF-8,
  UTF-16 LE/BE — read fine. This is on purpose: decoding such a file would replace every
  non-ASCII byte with `` and a single edit in Inkling would write that back permanently.
- Subfolders are scanned; new notes are always written to the root.
- **The scratchpad is `scratchpad.md` in the root of the same folder**, plain text with no front
  matter, so any other editor opens exactly what you typed. It **does not appear in the list or in
  search**, but **only the top-level one** is special — a `scratchpad.md` inside a subfolder is
  just a note. Switching the notes folder leaves the old scratchpad where it is; switch back and
  it is still there.

## Settings

Select the **Inkling** row in the main search box and press `Ctrl+K` → Settings
(`Ctrl+Enter` goes straight there), or CmdPal Settings → Extensions → Inkling.

| Setting | Default | Notes |
|---|---|---|
| Notes folder | `%OneDrive%\Inkling`, or `Documents\Inkling` when there is no OneDrive | **Full paths only** (a relative path rejects the whole save); a folder that does not exist yet is flagged immediately and created on first save. "Browse…" next to it opens the system folder picker and saves the choice on the spot |
| Quick capture separator | `;;` | Title before it, body after. Any length; half-width and full-width count as the same; clearing it restores `;;`. A quick capture page that is already open picks up the change — no reload |
| Preview after capture | On | Enter captures and stays on the note, a second Enter dismisses; off means capture and dismiss at once |

Those three are all the form shows. `settings.json` has a fourth key, `Inkling.ShowSource`
(source view), which `Ctrl+U` writes back itself — deliberately not in the form, because the
toggle key is its interface. Where the settings file lives, its format, and whether it survives
an update: [docs/development.md](docs/development.md#settings-file).

## Sync

Inkling **does not sync**. It writes Markdown files into the folder you choose and leaves sync
entirely to your cloud drive client — offline availability, conflict handling, and phone access
are whatever OneDrive / Dropbox already give you. To read notes on your phone, install the
OneDrive app; Obsidian or similar tools can point at the same folder.

**OneDrive users**: mark the Inkling folder "Always keep on this device" (right-click the folder).
With Files On-Demand, cloud-only placeholders trigger a download on read and search stalls.
When two machines edit the same note at once OneDrive creates a `name-ComputerName.md` copy.
Both files show up in the list, and because the copy carries the **same `id`** as the original,
Inkling tags both rows **Conflict copy** so you can tell what happened. Editing or deleting a row
acts on that row's own file — the two are independent. Inkling does not merge them: compare the
two rows in the details pane, keep the one you want, and delete the other.

## Troubleshooting

**Changed a setting and nothing happened** — close the settings page and reopen it. That page is
bound to one extension instance; if a reload or redeploy happened in between, Save silently does nothing.

**An extra "Inkling" row in search results that does nothing on Enter** — only if you are upgrading
from a build made before the package stopped registering a Start menu entry. That row is the Windows
app-list entry picked up by CmdPal's built-in app search, not a duplicate extension: the package's
exe is a pure COM server, so "launching" it was never going to do anything. Reinstalling clears it.

**Wrong UI language** — the language follows the Windows **display language** (not the "regional
format" setting) and there is no override. After changing the display language, sign out and back in.

**Your settings went back to the defaults** — if `settings.json` was left in a state that is not
valid JSON (an editor mishap, a crash mid-write), Inkling moves it aside as
`settings.json.corrupt-<timestamp>` and starts from the defaults, so that saving works again
instead of failing silently forever. The settings page says so at the top and names the file;
the old values are still in it if you want to pick them back out.

Everything else (deploy, registration, trimming, the extension's own diagnostic log):
[docs/development.md → troubleshooting](docs/development.md#troubleshooting).

## Documentation

The in-depth docs are written in Traditional Chinese.

| | |
|---|---|
| [docs/development.md](docs/development.md) | Build, deploy, project layout, troubleshooting |
| [docs/design-notes.md](docs/design-notes.md) | The full "why" behind every decision (why quick capture is a page and not a fallback, why not a single toast may be shown, why the confirm dialog's buttons have no color…), for maintainers and other CmdPal extension authors |
| [docs/known-issues.md](docs/known-issues.md) | Defects that are known and not yet fixed, each with a reproduction |
| [CHANGELOG.md](CHANGELOG.md) | Release history |
| [CONTRIBUTING.md](CONTRIBUTING.md) | Read before changing this repo |

## Contributing

Bug reports and feature requests are welcome (use the issue templates). Before opening a PR,
read [CONTRIBUTING.md](CONTRIBUTING.md) — this repo has a few rules that are not obvious
(UI strings live in three `.resx` files that change together; behavior changes update both
READMEs and the manual test checklist in the same change).

## License

[MIT](LICENSE)
