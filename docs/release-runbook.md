# 發版 Runbook

**適用範圍:第一個公開版本已經上架、套件身分(`Identity` 的 `Name` / `Publisher`)已經凍結
之後,要發下一個版本時跑這一份。**

第一次上架本身走 [`release-checklist.md`](release-checklist.md) —— 那是一次性的身分定案與
通路開通清單,裡面很多事一輩子只做一次。這一份是**每次都要跑**的。

標記:`[自動]` = 指令或 CI 做得完;`[人工]` = 要到某個網站點,或只能靠眼睛。

---

## 第 0 部分:確認承諾面沒有被動到

### 1. `[自動]` 命令 Id 一個字都沒變

```powershell
git diff <上一個 tag>..HEAD -- src/Inkling/CommandIds.cs
```

必須**沒有任何 `const string` 的值變化**。那七個字串是 CmdPal 存 alias 的鍵,而
`Aliases` 的條目裡不帶 PFN、也不帶 provider 參照(見
[設計考證〈命令 Id 為什麼要寫死〉](design-notes.md#command-ids))。改一個字,使用者設過的
alias、全域快速鍵、釘選當場失效。

⚠ **除了 `tests/Inkling.Tests/CommandIdTests.cs` 逐字比對那七個常數之外沒有別的把關**,
這個 diff 是唯一的閘門,不要跳過。

> **一次性的例外,已經用掉了。** 2026-08-22(第一個公開版本之前)那七個字串的前綴從
> `Notelet.` 換成了 `Inkling.`,而 `Identity` 的身分欄位在 08-22 與 08-23 各動一次
> (後者換成 Partner Center 指派的那一組,身分就此定案)—— 也就是說**第一個
> tag 之前刻意違反過這條閘門與下面第 2 步**。從 `v1.0.0` 起兩條都無條件成立,
> 這一段留著只是為了讓「為什麼歷史上有一次違反」有答案,**不是可以再用一次的先例**。
> 理由見 [設計考證〈前綴換過一次,而且只有那一次〉](design-notes.md#command-ids)。

### 2. `[自動]` `Identity` 的身分欄位沒變

```powershell
git diff <上一個 tag>..HEAD -- src/Inkling/Package.appxmanifest
```

**只有 `Version` 屬性會變,而且是 CI 在自己的 checkout 上改的,不是你手改的。**

會決定使用者資料去留的身分字串**有四個**,不是兩個:

**按屬性名找,不要按行號** —— 那個檔案的註解一改行號就位移,而寫死的行號不會報錯。

| 字串 | 位置 | 動了會怎樣 |
|---|---|---|
| `Identity/@Name`(目前是 `CPPt.InklingNotes`) | `Package.appxmanifest` 的 `<Identity>` | PFN 變 → `LocalState` 換位置 → Inkling 自己的設定全部孤兒化 |
| `Identity/@Publisher`(目前是 Partner Center 指派的 `CN=<GUID>`) | 同一個元素 | 同上(PFN 由 Publisher 的雜湊決定) |
| `Application/@Id`(目前是 `App`) | `<Applications>` 底下的 `<Application>` | CmdPal 端 `ProviderSettings` / `PinnedCommands` 的鍵變 → 啟用狀態、釘選、fallback 設定全部孤兒化 |
| `uap3:AppExtension/@Id`(目前是 `Inkling`) | `<uap3:Extension Category="windows.appExtension">` 底下 | 同上 |

後兩個容易被忽略:CmdPal 用的鍵是 `<PFN>!<Application Id>!<AppExtension Id>`,實測是
`CPPt.InklingNotes_fsn608qftpbpp!App!Inkling`。注意**第三段不是 `CommandIds.Provider`,
即使兩個現在都是 `Inkling`** —— 那是巧合,第三段來自 manifest,改哪一邊都不會動到另一邊
(同一段警告在 [`release-checklist.md`](release-checklist.md) §1 也有一份,兩邊要一致)。

`Aliases` 因為只用純命令 Id 當鍵,四個都不受影響 —— 但也只有 alias 不受影響。
(唯一的例外是 2026-08-22 那次連命令 Id 一起換的身分變更,見第 1 步的方框。)

### 3. `[人工]` `Publisher` 仍然是 Partner Center 指派的那一份

第一次上架時它從側載用的 CN 換成了 Partner Center 的身分。如果有人在某次 merge 裡把
manifest 還原回舊的 CN,`makeappx pack` 與 CI **都不會報錯**,Partner Center 會在上傳時
退件。第 2 步的 diff 就看得到。

---

## 第 1 部分:決定版本號

### 4. `[人工]` major / minor / patch

判準綁在「使用者感覺得到什麼」與「什麼東西是承諾」:

- **major** —— 破壞承諾才動。承諾有兩組:**資料格式**(`id` 才是身分、不認得的 front matter
  欄位原樣保留、沒有 front matter 的外來 `.md` 也要列得出來)與**身分**(`CommandIds` 的
  字串、上面那四個 manifest 字串)。兩組都被設計成「不要動」,所以 major 幾乎不會遞增。
- **minor** —— 新命令、新設定鍵、鍵位變動、使用者感覺得到的行為變更。
- **patch** —— 只修 bug、文案、翻譯、圖示;沒有新命令、沒有新設定鍵、鍵位沒動。
- **不發版** —— 只改 `docs/*`、`CLAUDE.md`、CI、測試。這些東西不會進使用者的機器。

### 5. `[人工]` MSIX 的版本格式,這裡有一個會擋下發版的硬限制

- 四段 `major.minor.build.revision`。
- **第四段必須是 0**(Store 保留)。已經處理掉了:`release.yml` 把 tag 的三段補成 `<tag>.0`。
- 其餘各段 0–65535,**第一段不能是 0**。來源:Microsoft Learn
  [App package requirements for MSIX app](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
  —— 「The other sections must be set to an integer between 0 and 65535 (except for the
  first section, which cannot be 0)」。

  ⚠ **`v0.x.y` 的 tag 產不出 Store 收得下的套件**,而 `release.yml` 的 regex 目前放行它 ——
  `release.yml` 的版本 regex 已經收成 `^[1-9]\d*\.\d+\.\d+$`,打 `v0.*` 會在 CI 就被擋下。
- Store 端每個 package full name(Name + Publisher + Version + 架構)必須唯一,
  **同一個版本號不能傳第二次**,即使前一次的 draft 已經刪掉。所以試跑失敗要重來時,
  是重跑 `workflow_dispatch`,不是重推同一個 tag。
- 使用者要收到更新,新版本必須**嚴格大於**舊版本(Store 永遠給裝置「適用的最高版本」)。

### 6. `[自動]` 版本號實際存在的每一個位置

| # | 位置 | 這次要動嗎 |
|---|---|---|
| 1 | git tag `v<major>.<minor>.<patch>` | **要**。這是唯一來源 |
| 2 | `src/Inkling/Package.appxmanifest` 的 `Identity/@Version` | **不要手改**。那是開發期預設值,CI 在自己的 checkout 上覆寫,不 commit 回來 |
| 3 | `CHANGELOG.md` 的 `## [Unreleased]` | **要**。見第 7 步 |
| 4 | 任何 `.csproj` / `.props` 的 `<Version>` / `<AppxPackageVersion>` | **不存在,也不要新增**。全 repo grep 零命中;`AppxPackageVersion` 對這套單專案 MSIX 目標實測無效 |
| 5 | `docs/gallery/extension.json` | **不用動**。整份沒有 version 欄位,上游 schema 也沒有 |
| 6 | `.github/ISSUE_TEMPLATE/bug_report.yml` 的版本 placeholder | 可選。它只是提示文字,但停在舊版本會讓回報者以為那是當前版本 |
| 7 | Partner Center 的「What's new in this version」 | **要**,每次。見第 17 步 |
| 8 | WinGet manifest 的 `PackageVersion` / `InstallerUrl` / `InstallerSha256` / `SignatureSha256` | 只有走 WinGet 通路才有。見第 19 步 |

---

## 第 2 部分:整理工作樹

### 7. `[人工]` 收 CHANGELOG

把 `## [Unreleased]` 的內容整段移到新的版本段落標上日期,`[Unreleased]` 留空殼在最上面。

**`CHANGELOG.md` 維持繁體中文,不翻。** 對外只有兩個欄位要英文,而且一個版本各寫一次:

- **GitHub Release 正文** —— **不用寫也不要寫**。`release.yml` 用 `--generate-notes`,
  GitHub 從英文的 Conventional Commits 產。**不要把 CHANGELOG 貼進去**,那會把一段中文
  推到每個下載頁上。
- **Partner Center 的「What's new」** —— 手寫英文,從這次的 CHANGELOG 段落譯過去。
  留到第 17 步做。

### 8. `[人工]` 同輪更新使用者看得到的文檔

[CLAUDE.md](../CLAUDE.md)〈慣例〉那條規則在發版這一輪一樣成立:改了指令、設定項、
資料格式或對外行為,就要同時更新**兩份 README**、
[`manual-test-checklist.md`](manual-test-checklist.md) 與 [`CHANGELOG.md`](../CHANGELOG.md)。
章節、表格的列、截圖都要對得上。

發版特有的兩件:

- 兩份 README 的〈Install / 安裝〉章節在第一個 release 之後就不該再寫「還沒發佈」。
  **通路是一條一條開的**,開通一條就補一條,兩份一起改。
- 畫面變了就重拍 `docs/images/` 的截圖與 GIF(流程見
  [`development.md`](development.md))。重拍前先把筆記資料夾指到 demo 資料夾。
  ⚠ **Store listing 的截圖另有尺寸下限(≥1366×768),而 `cmdpal-ui.ps1` 抓的面板視窗
  只有 1200×720** —— 那一組**不要重拍,用合成的**:

  ```powershell
  pwsh -NoProfile -File tools\make-store-screenshots.ps1
  ```

  它把 `docs/images/` 那三張裁掉 PrintWindow 的黑邊,**原尺寸**放到 1920×1080 的畫布
  中央,背景鋪 Windows 自己的桌布(`%SystemRoot%\Web\Wallpaper\Windows\img0.jpg`)
  再補陰影,輸出到 `assets/store/`。**面板不放大** —— 那是文字截圖,放大就糊。
  背景用桌布而不是漸層,是因為 Command Palette 本來就浮在桌面上,配桌布才是它真正的
  樣子;**桌布檔案不進 repo**(那是微軟的美術資源),腳本讀機器上那一份,找不到就
  自動退回漸層。要換背景給 `-Background <路徑>`。
  來源沿用 `docs/images/` 是因為那三張已經是**英文介面**(Store listing 是英文,
  重拍要再登出登入切一次 Windows 顯示語言)而且內容是安排過的 demo 筆記。
  要換內容就先重拍 `docs/images/` 再跑這支,不要另外維護一套。
  ⚠ **輸出是 JPEG 不是 PNG,而且理由是大小**:換成照片式背景之後 PNG 一張 975 KB,
  而 CmdPal gallery 的 `screenshots/` 上限是 **1 MB/張** —— 只剩 5% 餘裕,
  換一張桌布就爆。JPEG 品質 95 約 195 KB,文字區塊與 PNG 的最大單通道差是 9/255
  (平均 0.95),1:1 看不出來。真要無損就 `-Format Png`,但**輸出之後一定要對大小**
  (腳本超過 1 MB 會自己警告)。
  `assets/store/` **不進 MSIX**(套件圖示在 `src/Inkling/Assets/`),
  檔名前綴就是上傳順序。

### 9. `[自動]` 圖示改過就重產 PNG

```powershell
pwsh -NoProfile -File tools\render-icons.ps1
```

`src/Inkling/Assets/*.png` 是產生出來的,不要手改。同一支腳本還產
`assets/gallery/icon.png`(兩份 README 頂部引用的就是它,也是 gallery 投稿用的那張)
與 `assets/social-preview.png`。

---

## 第 3 部分:本機驗證

### 10. `[自動]` 跑完整測試

```powershell
dotnet test --nologo
dotnet test tests\Inkling.Tests\Inkling.Tests.csproj -p:Platform=x64 --nologo
```

第二條**不能省**:那個專案刻意不在 `Inkling.slnx` 裡,`dotnet test` 不會碰到它,而它測的
正是最會漂的東西 —— 頁面命令順序與快速鍵、三個清單頁的快取鍵、`Dispose` 有沒有退訂。
CI 也會各跑一次,但在本機先跑掉比較便宜。

### 11. `[自動]` 部署 Release 到本機

```powershell
.\tools\deploy.ps1 -Configuration Release -Reload
```

Release 走 trimmed publish,而 **trimming 只在 `dotnet publish` 生效**,所以
「trimming 有沒有把東西砍壞」只有這條路驗得到。

### 12. `[自動]` 對進程時間,確認你驗的是新的那一份

```powershell
Get-Process Inkling | Select-Object StartTime
(Get-Item src\Inkling\bin\stage-Release\Inkling.dll).LastWriteTime
```

`StartTime` 必須**晚於** `LastWriteTime`。不是的話 `Stop-Process -Name Inkling`,
再不行連 `Microsoft.CmdPal.UI` 一起停掉。踩過一次:部署說成功、檔案也換了,但舊進程
還活著,於是驗到的是上一版的行為。

### 13. `[人工]` 確認設定頁的擴展清單裡只有一個 Inkling

兩個的話後面每一項看到的都可能是舊實例的畫面。另外**每次 Reload / 重新部署之後,
設定頁要退回 Extensions 清單再點進去** —— 舊的那個綁在死掉的擴展實例上,按 Save
靜靜地什麼都不做。

### 14. `[人工]` 跑 [`manual-test-checklist.md`](manual-test-checklist.md)

全跑最好;至少跑這幾段:

- **§2b alias 不會失效** —— 這是第 1 步那個 diff 的執行期對照,也是升級路徑上最貴的回歸。
- 這一版改到的功能對應的那幾節。
- §11 介面語言(改過 `.resx` 的話 —— 要改 Windows 顯示語言並重新登入,預留時間)。

分工:清單內容、快速鍵、placeholder、有沒有跳 toast 可以用 `tools\cmdpal-ui.ps1` 驅動
(見 `.claude/skills/verify-cmdpal-ui/`);顏色、圖示長相、游標位置只能靠眼睛;
跑出面板以外的視窗(外部編輯器、檔案總管、「瀏覽…」對話框)那個腳本管不到,
改用 `orca computer`。

---

## 第 4 部分:試跑打包這條路

### 15. `[自動]` 用 `workflow_dispatch` 把整條跑一次

```powershell
gh workflow run release.yml -f dry_run_version=1.1.0
gh run watch
```

它跑完整條路,只跳過最後建 Release 那一步。**改過打包相關的東西就一定要跑這個**,
不要拿真的 tag 當測試 —— tag 推上去之後要重跑還得先把 tag 刪掉,而那個版本號在 Store 端
可能已經被佔掉了。

版本號填**這次真的要發的那個**,不要留 `0.0.0`:資產檔名會跟著它,順便驗第 5 步那條
「第一段不能是 0」不會在真正發版時才被發現。

CI 每個 PR 就會做 stage + `makeappx pack` + 語言宣告檢查,所以平常的 push 已經擋掉大部分
打包問題;`workflow_dispatch` 多驗的是 ARM64 那一份與 bundle 那一段(`/bv`、
下載 artifact 合流)。

---

## 第 5 部分:打 tag,讓 CI 出貨

### 16. `[自動]` 推 tag

```powershell
git tag v1.1.0
git push origin v1.1.0
gh run watch
```

`release.yml` 依序做完:

1. 從 ref 名解析版本、驗格式、補成四段
2. 把版本寫進 `Package.appxmanifest` 的 `Identity/@Version`,**身分欄位不碰**
3. 跑 Core 測試與擴展層測試(tag 可能打在沒跑過 `ci.yml` 的 commit 上)
4. x64 與 ARM64 各一次 trimmed publish
5. `tools/stage-layout.ps1` 併佈局(publish 輸出裡沒有 `AppxManifest.xml`)
6. `makeappx pack`,**刻意不加 `/nv`**,要的就是完整套件驗證
7. 有 `SIGNING_CERT_BASE64` secret 才簽 `.msix`
8. `makeappx bundle /o /bv <版本>` 組 msixbundle —— **`/bv` 不能省**,否則 bundle 版本會
   變成打包當下的日期時間
9. **再簽一次 bundle** —— 簽章不會從裡面的 `.msix` 傳遞到外層,只簽裡面的話側載照樣被
   `0x800B0109` 擋下
10. `gh release create --generate-notes` 附上三個資產

**簽章分兩種情況,決定後面走哪幾條路:**

- **走 Store:不需要憑證。** Store 在通過認證後會用微軟的憑證**重新簽**你的 MSIX
  (Learn〈App package requirements〉的 “Code signing for Microsoft Store submissions”:
  不需要買 CA 憑證、不需要 HSM 或 USB token)。CI 這時產出的是**未簽章**的 bundle,
  那正是 Partner Center 要的東西。
- **要給人側載,或要上 winget-pkgs:必須自己簽。** Windows 不會安裝未簽章的 MSIX。
  把 PFX 以 base64 存進 repo secret `SIGNING_CERT_BASE64`、密碼存 `SIGNING_CERT_PASSWORD`,
  `release.yml` 的兩個簽章步驟會自動啟用(憑證只掛在真的要簽的那一步的 env,不放 job 層
  —— job 層的 env 連 `dotnet publish` 都讀得到)。

  ⚠ 在沒有憑證的期間,`release.yml` 仍然會把未簽章資產掛上公開 Release 且不加說明 ——
  沒有簽章時 `release.yml` 會把 release 建成 **prerelease** 並改用顯式的 `--notes`
  講明「這些資產沒有簽章、是給 Store 送審用的」,而不是 `--generate-notes`。

---

## 第 6 部分:Microsoft Store 送審

### 17. `[人工]` 在 Partner Center 開一個新的 submission

1. 該 app 的 Overview → **Update**。
2. **Packages**:上傳從 GitHub Release 抓下來的 `Inkling_v<版本>.msixbundle`。
   **每次發版一定要動這個。**
3. **Store listing → What's new in this version**:手寫英文,從這次的 CHANGELOG 段落譯過去。
   **每次發版一定要動這個** —— 這是使用者在 Store 上唯一看得到「這版改了什麼」的地方。
4. **通常不用動**:描述、關鍵字、定價、分類。畫面變了才換截圖(第 8 步已經重拍好了)。
5. **Notes for certification**(建議每次照抄同一段):說明這是 PowerToys Command Palette 的
   擴展,exe 是**純 COM server**(`src/Inkling/Program.cs`:沒帶
   `-RegisterProcessAsComServer` 就只印一行然後結束),manifest 刻意設 `AppListEntry="none"`,
   所以它**不會出現在開始功能表、直接啟動也不會有任何畫面**。
   ⚠ **不寫的話審查員很可能把「點了沒反應」當成 bug 退件。**
6. **Restricted capabilities 的說明欄**:`Package.appxmanifest` 的
   `rescap:Capability Name="runFullTrust"` 是**受限能力**,上傳之後 Partner Center 會跳出
   「Why do you need this capability?」要你填理由,**不填就送不出去**。
   而且它**不是只有第一次要填** —— 微軟自己的答覆是這個欄位可能在後續更新裡再次要求,
   即使能力宣告一個字都沒變(來源:Microsoft Q&A
   [Providing "Restricted Capabilities" explanation](https://learn.microsoft.com/en-us/answers/questions/505097/providing-restricted-capabilities-explanation-via))。
   照抄同一段就好:這個套件註冊一個 **out-of-process COM server**(Packaged COM)讓
   PowerToys Command Palette 啟動它,而 Learn 的
   [App capability declarations](https://learn.microsoft.com/en-us/windows/uwp/packaging/app-capability-declarations)
   寫明「to be able to register out-of-process COM servers for inter-process
   communication (IPC), a packaged app needs runFullTrust」—— 也就是說這個宣告是這種套件
   **無法省略**的,不是我們挑的。理由寫「框架要求」被退過件,要寫成上面那樣的具體用途。
7. 想控制風險就勾 **Roll out update gradually**,設一個初始百分比(例如 5%)。這只對 MSIX
   有效。發佈後可以在 Overview 頁拉百分比或按 Halt,不用開新的 submission。
   **注意:開下一個 submission 之前必須先把這次的 rollout finalize 或 halt。**
8. Submit to the Store。

### 18. `[人工]` 等審核

官方說法:submission 進入 certification 這一段「can take up to three business days」;
通過之後平均約 15 分鐘使用者就看得到 listing。沒過會拿到一份 certification report 指出
哪一項不合格;修完是**開一個新的 submission** 重跑,不是續傳。

---

## 第 7 部分:WinGet(只有在有自己的簽章時才走得通)

### 19. `[人工 + 自動]` 更新 winget-pkgs 的 manifest

**先確認前提**:winget-pkgs 不收未簽章的 MSIX,而且 `InstallerUrl` 必須是公開可下載的 URL。
所以這條路實際上要求你有自己的憑證,CI 產出已簽章的 bundle 掛在 GitHub Release 上,
manifest 指過去。

> ⚠ **只走 Store 的話,實務上的結論是跳過 winget-pkgs 的 PR。** Partner Center 沒有
> 文檔化的「把 Store 重簽後的套件下載回來」流程。winget 使用者照樣可以用
> `winget install --source msstore --id <Store product ID>` 裝到,而 CmdPal 的 gallery
> 也接受 `msstore` 型別的 `installSources`。

真的要送的話用 `wingetcreate`,不要手寫:

```powershell
winget install Microsoft.WingetCreate
wingetcreate update <PackageIdentifier> `
  --version 1.1.0 `
  --urls "https://github.com/1morr/Inkling/releases/download/v1.1.0/Inkling_v1.1.0.msixbundle" `
  --submit --token $env:GH_PAT
```

每次發版要跟著換:`PackageVersion`、`InstallerUrl`、`InstallerSha256`、`SignatureSha256`。
**不會變、但漏了會出事**的兩個:

- `PackageFamilyName` —— 少了它 winget 對不上「這台機器已經裝了」,升級與解安裝都會失準。
  用 `(Get-AppxPackage *Inkling*).PackageFamilyName` 取,不要寫死。
- `Tags` 必須含 **`windows-commandpalette-extension`** —— CmdPal 內建的擴展搜索靠這個 tag
  找套件,少了它使用者在 Command Palette 裡搜不到。

`SignatureSha256` 從 bundle 裡取(bundle 就是 zip):

```powershell
$tmp = Join-Path $env:TEMP 'inkling-sig'
Expand-Archive 'Inkling_v1.1.0.msixbundle' $tmp -Force
(Get-FileHash "$tmp\AppxSignature.p7x" -Algorithm SHA256).Hash
```

`ManifestVersion` 用當下 winget-pkgs 收的最新 schema,**不要照抄文檔裡的數字** ——
那份已經落後過一次。

---

## 第 8 部分:CmdPal Extension Gallery

### 20. `[人工]` 絕大多數版本這一步什麼都不用做

`docs/gallery/extension.json` 整份**沒有 version 欄位**,上游 schema 也沒有。gallery 的
條目只是指到 Store 或 WinGet,實際的散佈與版本都由那兩個通路處理 —— 也就是說
**新版本會自動被 gallery 的使用者拿到**。

只有這幾種情況才要再送一次 PR 到 `microsoft/CmdPal-Extensions`:

- `description` / `shortDescription` 講的功能過期了。
- 圖示換了(`assets/gallery/icon.png`,PNG/JPEG、≤100 KB、建議 256×256,不收 SVG)。
- 截圖過期了(`screenshots/` 至多 5 張、每張 ≤1 MB,**不收 GIF**,檔名字母序決定順序)。
- `installSources` 變了(例如原本只有 `msstore`,現在補上 `winget`)。

流程與欄位限制見 `docs/gallery/README.md`。第一次送 microsoft 的 repo 要簽 CLA。

---

## 第 9 部分:更新是怎麼送到使用者手上的

### 21. `[人工]` 知道要跟使用者說什麼,也知道自己要驗什麼

- **Store 安裝會自動更新**,但**沒有文檔保證的固定檢查間隔**。使用者要立刻拿到就去
  Microsoft Store → 媒體庫 → 取得更新。
- **MSIX 不會替換正在使用中的套件檔案。** 這對 Inkling 特別要緊:CmdPal 平常就把擴展的
  COM server 常駐拉著,所以 `Inkling.exe` 幾乎永遠是活的。
  ⚠ **實際會發生什麼,第一次真的發版時要在真機上驗掉並寫回這一份。** 可能的結果是
  「Store 說已更新,但 CmdPal 還在用舊的 `Inkling.exe`,直到那個進程退出」。
  使用者端的自救動作是關掉 CmdPal、`Stop-Process -Name Inkling`,或重開機。
- **升級不需要使用者手動 Reload。** 套件升級走「先移除再安裝」,CmdPal 訂閱 Windows 套件
  目錄的事件並據此重建 provider。也正因為是移除+安裝而不是「同版本重裝」,它**不會**觸發
  那個「兩個 Inkling」的重複 provider 症狀。這條也請在第一次真的發版時對照一次。
- **驗自己的機器拿到的是不是新版**:`(Get-AppxPackage *Inkling*).Version`。

### 22. `[人工]` 確認使用者的東西都還在

| 東西 | 存在哪 | 更新後 |
|---|---|---|
| 筆記檔本身 | 設定裡指到的一般資料夾 | **完全不受影響**,跟套件生命週期無關 |
| Inkling 自己的設定 | `%LOCALAPPDATA%\Packages\<PFN>\LocalState\settings.json` | **保留**(`Name` / `Publisher` 不變 → PFN 不變) |
| CmdPal 的 alias | CmdPal 套件的 settings.json,鍵是**純命令 Id** | **保留**,只要 `CommandIds.cs` 不動(第 1 步) |
| 釘選、啟用狀態、fallback 規則 | 同上,鍵是 `<PFN>!<Application Id>!<AppExtension Id>` | **保留**,只要那三段都不動(第 2 步) |
| 新增的設定項 | — | 舊檔案沒有那個鍵 → 用程式裡宣告的預設值 |
| 移除的設定項 | — | toolkit 的存檔是**合併**,不認得的鍵原樣留著 |
| 改掉的預設值 | — | **只對「檔案裡還沒有那個鍵」的人生效**。按過一次儲存的人不受影響 |

真的會弄丟設定的只有兩件事:動了第 2 步那四個身分字串,以及不帶
`-PreserveApplicationData` 的 `Remove-AppxPackage`。兩件在正常發版路徑上都不會發生。

### 23. `[人工]` 想驗 Store 版之前,先處理掉本機的側載註冊

第一次上架之後,repo 的 manifest 帶的是 Partner Center 身分,於是 `deploy.ps1` 註冊的
loose-file 佈局跟 Store 裝的**是同一個套件身分**。`deploy.ps1` 會先
`Remove-AppxPackage -PreserveApplicationData` 再 register —— 也就是**它會把 Store 版本
卸掉換成你的開發佈局**(設定會保住,但版本與簽章都變了)。要驗真正的發佈品,
用一台乾淨的機器或 VM,或者驗完之後記得從 Store 重裝。

---

## 第 10 部分:這一版是壞的怎麼辦

### 24. `[人工]` Store:沒有真正的 rollback,只有兩招,而且都救不了已經更新的人

- **正在 gradual rollout 中**:Overview 頁按 **Halt package rollout**。之後新的取得者與
  更新者只會拿到上一個 submission 的套件;**已經拿到新套件的人不會被退回**。
- **已經全量發佈**:開一個新的 submission,把有問題的 package 移掉、**把舊的那一份重新
  傳上去**。這會擋住新的取得者,但已經更新的人手上仍然是壞的那一版(舊套件的版本號比較低,
  不構成更新)。
  - **前提是你留著舊的套件檔。** GitHub Release 的資產就是你的備份 ——
    **不要為了整潔去刪舊 release 的資產。**
- **真正的修法**:修好,發一個**更高**的版本,走完整個 submission 流程(又是最多三個工作天)。
  這是唯一能把已經更新的使用者救回來的路。

### 25. `[人工]` WinGet / GitHub Release

- WinGet:對 winget-pkgs 送一個 PR 把壞版本的那個版本目錄移掉(新安裝就不會拿到它),
  然後照第 19 步送新版本。已經裝了的人不會自動退回。
- GitHub Release:可以把那個 release 標成 pre-release 或刪掉資產,但已經下載的人不受影響。
  **不要刪 tag** —— tag 刪了之後同一個版本號再也不能重推(Store 端那個版本也已經被佔掉),
  下一步永遠是往前發一個新版本,不是回頭修同一個。

### 26. `[人工]` 事後把學到的東西寫回文檔

「查過、量過,然後決定不做」的進 [設計考證〈評估過但沒有做〉](design-notes.md#deferred),
每一條要寫「什麼變了才該重新考慮」;還沒修的缺陷進 [`known-issues.md`](known-issues.md);
發版流程本身踩到的坑寫回這一份與 [`release-checklist.md`](release-checklist.md)。

這是這個 repo 的既有規則,發版這條路特別適用 —— 它一年跑不了幾次,忘得最快。

**第一個公開版本跑完之後,還要多做一件事:把 `release-checklist.md` 收掉。**
那份的定位是**一次性**的(身分定案與通路開通),而它的 §3 發版流程 / §4 WinGet /
§5 Gallery 跟這一份的第 3、7、8 部分講的是同一件事 —— 首版出去之後兩份並存,
下次發版第一個問題就會是「該翻哪一份」。做法:

1. §1 那張已定案的身分表(`Identity` 的四個欄位、Store ID、PFN)搬進這一份的**步驟 2**,
   它本來就在引用那張表。
2. §6〈公開 repo 之前的最後檢查〉是一次性的,首版之後不再適用,直接刪。
3. 其餘與這一份重複的段落刪掉,整份 `release-checklist.md` 移除,並改掉指向它的連結
   (`grep -rn release-checklist` 當下重查一次,以下是 2026-08-23 的清單):
   `CLAUDE.md`(文檔表那一列 + 硬規則 1)、`docs/development.md`(文檔表 + 兩處內文)、
   `docs/design-notes.md`、`docs/gallery/README.md`、
   `.claude/skills/publish-extension/SKILL.md`、`.github/workflows/release.yml` 的註解,
   以及這一份自己的步驟 1、2。**`CHANGELOG.md` 裡的不要動** —— 那是已發生的歷史,
   連結指向一份當時存在的文檔,改掉等於竄改記錄。
