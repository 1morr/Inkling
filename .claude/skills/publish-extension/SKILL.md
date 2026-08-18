---
name: publish-extension
description: >-
  Publish your Command Palette extension to the Microsoft Store or WinGet.
  Use when asked to publish, distribute, release, deploy to store,
  create MSIX packages, submit to WinGet, set up CI/CD for releases,
  or automate builds with GitHub Actions.
---

> **這份是 CmdPal 官方擴展模板附的 skill,正文原封不動搬進來。**
>
> **本專案目前是側載自簽,沒有上架。** 真的要走的話先注意:
>
> 1. **改 `Identity` 的 `Name` 或 `Publisher` 會弄丟使用者的設定。** 套件家族名跟著變,
>    等於換一個空的 `LocalState` —— 舊的 `settings.json` 還在磁碟上但再也沒人去讀。
>    上架換成 Partner Center 的身分就一定會發生這件事。README〈設定存在哪〉。
> 2. **WinGet 那條路要把專案切成 unpackaged**,而擴展是靠 MSIX 的
>    `com:ComServer` + `uap3:AppExtension` 被 CmdPal 找到的 —— 那不是改個開關的事,
>    先確認 unpackaged COM 註冊怎麼做。
> 3. 上架前要決定 manifest 的 `DisplayName` / `Description` 要不要本地化
>    (`ms-resource:` + `.resw` + MakePri)。目前是單語,而介面本身已經有三種語言,
>    見 README〈介面語言跟著 Windows 走〉。
> 4. `APPX1707` 警告官方模板也有,無害。

# Publish Your Command Palette Extension

Guide for distributing your Command Palette extension through the Microsoft Store, WinGet, or both.

## When to Use This Skill

- Publishing your extension to the Microsoft Store
- Submitting your extension to WinGet for `winget install` discovery
- Setting up GitHub Actions to automate builds and releases
- Creating MSIX packages for Store submission
- Creating EXE installers for WinGet submission

## Publishing Options

| Channel | Package Format | Discovery | Auto-Updates |
|---------|---------------|-----------|--------------|
| Microsoft Store | MSIX bundle | Store app, `ms-windows-store://` link | Yes |
| WinGet | EXE installer | `winget install`, CmdPal browse | Yes (via manifest) |

**Recommendation**: Publish to both for maximum reach. WinGet enables direct discovery from within Command Palette.

## Workflows

### Microsoft Store Publishing
See [store-publishing.md](references/store-publishing.md) for the complete step-by-step guide.

**Summary:**
1. Register for Partner Center
2. Update `Package.appxmanifest` and `.csproj` with Partner Center identity
3. Build MSIX for x64 and ARM64
4. Create MSIX bundle
5. Submit to Partner Center

### WinGet Publishing
See [winget-publishing.md](references/winget-publishing.md) for the complete step-by-step guide.

**Summary:**
1. Switch project to unpackaged mode
2. Create Inno Setup installer script
3. Build EXE installers
4. Submit manifest via `wingetcreate new`
5. Optionally automate with GitHub Actions

## Prerequisites

- [Visual Studio](https://visualstudio.microsoft.com/) with C# and WinUI workloads
- [Partner Center account](https://partner.microsoft.com/dashboard/home) (for Store publishing)
- [GitHub CLI](https://cli.github.com/) (for WinGet publishing)
- [WingetCreate](https://github.com/microsoft/winget-create) — `winget install Microsoft.WingetCreate`
- [Inno Setup](https://jrsoftware.org/isdl.php) (for WinGet EXE packaging)

## Important Notes

- Your extension's CLSID (the `[Guid("...")]` in your main .cs file) must be unique and consistent across all files
- WinGet manifests must include the `windows-commandpalette-extension` tag for CmdPal discovery
- MSIX packages require both x64 and ARM64 builds for Store submission
- WindowsAppSdk must be listed as a dependency in WinGet manifests
