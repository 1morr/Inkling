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
>    上架換成 Partner Center 的身分就一定會發生這件事。README〈設定存在哪,更新擴展之後還在嗎〉。
> 2. **WinGet 那條路正文叫你切成 unpackaged EXE + Inno Setup —— 對本專案是錯的,別走。**
>    實際在架的 CmdPal 擴展(`lin-ycv.EverythingCmdPal`、`8LWXpg.ProcessKillerforCommandPalette`)
>    用的是 `InstallerType: msix`:x64/arm64 指向 GitHub Release 上同一個簽章過的
>    `.msixbundle`,帶 `PackageFamilyName`、`SignatureSha256`、`RestrictedCapabilities: runFullTrust`。
>    擴展是靠 MSIX 的 `com:ComServer` + `uap3:AppExtension` 被 CmdPal 找到的,
>    切 unpackaged 等於把發現機制整個重寫,而且完全沒有必要。`references/winget-publishing.md`
>    留著當上游參考,但它的 unpackaged 路線本專案不適用。manifest 的 Tags 要帶
>    `windows-commandpalette-extension` —— CmdPal 內建的擴展瀏覽靠這個 tag 過濾,
>    有帶的套件從 gallery 安裝時還會被設 `SkipDependencies`(見下一點)。
> 3. **同一份還說 manifest 必須宣告 `Microsoft.WindowsAppRuntime` 依賴 —— 對本專案也是錯的。**
>    Notelet 沒有引用 WindowsAppSDK(pubxml 是 self-contained),在架的 ProcessKiller
>    manifest 也沒有 Dependencies 區段。而且 CmdPal 對帶 `windows-commandpalette-extension`
>    tag 的套件設 `SkipDependencies = true`,宣告了從 gallery 安裝也會被跳過,
>    只有走 winget CLI 的人會被多裝幾百 MB 的執行期。**什麼時候才要宣告:專案真的
>    參考 `Microsoft.WindowsAppSDK` 的時候。**
> 4. **gallery 的條目只是指到 Store 或 WinGet。** CmdPal 內建的擴展瀏覽讀的 feed 是
>    `aka.ms/CmdPal-ExtensionsJson`,內容來自 `github.com/microsoft/CmdPal-Extensions`
>    的 `extensions.json` —— 要進 gallery 是對那個 repo 送 PR,而前提是擴展已經先在
>    Store 或 WinGet 上架。
> 5. 上架前要決定 manifest 的 `DisplayName` / `Description` 要不要本地化
>    (`ms-resource:` + `.resw` + MakePri)。目前是單語,而介面本身已經有三種語言,
>    見 [設計考證〈介面語言跟著 Windows 走〉](../../../docs/design-notes.md#ui-language)。
> 6. `APPX1707` 警告官方模板也有,無害。

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
