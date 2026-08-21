# 首次公開發佈 checklist

這份清單是「從本機側載專案」走到「公開散佈」的完整待辦。順序有意義:
**身分一定要在第一個公開版本之前定案**,之後永久凍結。

## 1. 定案套件身分(Name / Publisher)—— 只能定一次

`Package.appxmanifest` 目前是本機側載用的自簽身分:

```xml
<Identity Name="Inkling" Publisher="CN=Notelet Development" Version="0.1.0.0" />
```

`Publisher` 還寫著 `Notelet` 不是漏改的 —— 見下面〈Name 換過一次〉。

為什麼這件事只能做一次:

- `Name` + `Publisher` 決定 package family name(PFN,目前是
  `Inkling_bf0n0751x5hse`)。**後綴那串雜湊只由 `Publisher` 決定**,`Name` 換了
  只換前半段。
- Windows 按 PFN 隔離每個套件的 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`。
  PFN 一變,舊的 `settings.json`(筆記資料夾、快速記下的分隔線與預覽開關)
  變成孤兒 —— 等於使用者的設定被重置。
- CmdPal 端的設定分兩種鍵,**不要混為一談**(實測 CmdPal 的 settings.json 得到的):
  - `ProviderSettings` 與 `PinnedCommands` 用 `<PFN>!App!<ProviderId>` 當鍵,PFN 一變就孤兒化。
  - **`Aliases` 用的是純命令 Id**(`"CommandId": "Notelet.List"`),條目裡沒有 PFN、
    也沒有 provider 參照 —— 所以只要 `CommandIds.cs` 的字串不動,alias 換身分後還在。
- 目前只有作者一台機器受影響,這正是換身分的唯一時機;一旦有人公開安裝,
  再換就是把所有使用者的設定一起洗掉。

### Name 換過一次(Notelet → Inkling)

改名時只動了 `Identity Name`,`Publisher` 刻意不動:

- 動 `Publisher` 的話 PFN 的雜湊後綴也會變,而且自簽憑證要重發、重新信任。
  它只是側載用的 CN,對外不可見,留著沒有壞處。
- `CommandIds.cs` 那六個字串一併保留原值(還叫 `Notelet.*`),換來的是使用者
  設過的 alias 全部活下來。那些字串使用者看不到,不值得為了整齊清掉他們的設定。

真正該換 `Publisher` 的時機是下面兩條路擇一的時候,一次換完。

兩條路擇一:

### (a) Microsoft Store 代簽(建議先走這條)

- 註冊 Partner Center(**個人開發者現在免費**),保留 `Inkling` 這個名字,
  取得 Partner Center 指派的 `Name` / `Publisher`,改進 manifest。
- 上傳 `.github/workflows/release.yml` 產出的**未簽章** msixbundle,Store 審核後代簽。
- 成本:帳號免費;時間成本是審核(首次通常數天)。
- 好處:不用買憑證、不用管憑證續期與保管;使用者從 Store 安裝,信任鏈由微軟處理。

### (b) 自購 OV 程式碼簽章憑證(走 WinGet / 直接散佈)

- 買公開信任的 OV 程式碼簽章憑證(約每年 USD 70–400,視 CA 與年數),
  `Publisher` 改成憑證的完整 DN。
- 憑證 PFX 以 base64 存進 repo secret `SIGNING_CERT_BASE64`、密碼存
  `SIGNING_CERT_PASSWORD`,release.yml 的 Sign 步驟就會啟用,產出可直接側載的
  已簽章 msix / msixbundle。
- 成本:憑證年費 + 私鑰保管責任(私鑰外洩 = 憑證作廢重辦;`.gitignore` 已擋
  `*.pfx` / `*.p12`,不要把憑證檔放進 repo)。
- EV 憑證能立刻取得 SmartScreen 信譽但貴得多;OV 需要累積信譽,初期使用者
  可能看到 SmartScreen 提示。

常見組合是 (a) 先上架 Store,再把 Store 簽好的 msix 拿去發 WinGet manifest ——
兩個管道同一身分、同一個 LocalState。

### 換身分當下必須同步更新的硬編碼 PFN

**已經處理掉了。** 那個字串以前硬編碼在八處(五個檔案),換身分後會全部靜靜失效
(讀不到檔案不會報錯,只會讓驗證失明)。改名那一輪順手全部改成動態查:

```powershell
(Get-AppxPackage Inkling).PackageFamilyName
```

文檔裡一律寫成 `%LOCALAPPDATA%\Packages\<PFN>\LocalState`,腳本片段直接內插上面那一行。
`tools/cmdpal-ui.ps1` 本來就是那樣(開頭動態取 PFN,取不到直接中止)。

**新增文檔時別再把 PFN 寫死。** 唯一還留著字面值的是 `Package.appxmanifest` 的註解
與本節上方 —— 那兩處的重點正是那個字串本身。

## 2. 版本策略

- **單一來源是 git tag**:`v<major>.<minor>.<patch>`。release.yml 會注入成四段的
  `<major>.<minor>.<patch>.0` —— MSIX 版本必須四段,且第四段必須是 0(Store 規定)。
- manifest 裡的 `0.1.0.0` 只是開發期預設,發版不用手改;CI 在 checkout 上改完就丟。
- Store 與 WinGet 都以 manifest 的 Version 判斷升級,**每次發版都要嚴格遞增**,
  版本不動使用者永遠收不到更新。
- 注意 `-p:AppxPackageVersion` 對這套單專案 MSIX 目標**沒有作用**(本機實測,
  產生的 AppxManifest.xml 仍是 0.1.0.0),所以 release.yml 是直接改 manifest 檔。

## 3. 發版流程(身分與簽章都定案之後)

1. 把 `CHANGELOG.md` 的 `[Unreleased]` 內容移到新版本段落,標上日期。
   **`CHANGELOG.md` 本身維持繁體中文** —— 它是維護者紀錄,而且是這個 repo
   churn 前四名的檔案,雙語等於每個 commit 都要翻兩次(見
   [CLAUDE.md 〈文檔語言分層〉](../CLAUDE.md#docs-language))。對外那兩個欄位才用英文,
   而且一個版本只寫一次:
   - **GitHub Release 的正文** —— 不用手寫也不要手寫。release.yml 用 `--generate-notes`,
     GitHub 從 commit message 產生,而那些本來就是英文(Conventional Commits)。
     **不要把 `CHANGELOG.md` 貼進去**,那會把一段中文推到每一個下載頁面上。
   - **Partner Center 的「What's new in this version」** —— 這一欄要自己寫英文,
     從當次的 CHANGELOG 段落譯過去(第 5 步送審時)。
2. 跑過 `docs/manual-test-checklist.md`(至少發版相關的段落)。
3. 打 tag:`git tag v0.2.0 && git push origin v0.2.0`。
4. release.yml 自動:跑測試 → 建 x64 + ARM64(trimmed publish)→ 注入版本 → 組 msix →
   (有設憑證 secret 才)簽 msix → 組 msixbundle(帶 `/bv`,版本跟著 tag)→
   (有設憑證 secret 才)**再簽 bundle** → 建 GitHub Release 附資產。
   **兩次簽章都要**:簽章不會從裡面的 `.msix` 傳遞到外層 bundle,只簽裡面的話
   Release 上掛的 bundle 側載一樣會被 `0x800B0109` 擋下。
5. 走 Store 路線:從 Release 資產拿下 msixbundle,上傳 Partner Center 送審。

## 4. WinGet 上架

- 前提:**已簽章**的 msix((b) 路線的 CI 產出,或 Store 簽好拿回來的)。
  winget-pkgs 不收未簽章的 MSIX。
- `PackageIdentifier` 建議 `<author>.Inkling`,版本與 tag 對齊。
- `License` / `LicenseUrl` 填 MIT 與 repo 的 LICENSE 連結(已備妥)。

MSIX 專屬的欄位別漏(用 `winget-create` 產 manifest 的話它會問,手寫容易漏):

| 欄位 | 值 | 漏了會怎樣 |
|---|---|---|
| `InstallerType` | `msix` | 型別錯了驗證直接擋下 |
| `PackageFamilyName` | `(Get-AppxPackage Inkling).PackageFamilyName` | WinGet 對不上「這台機器已經裝了」,升級與解安裝會失準 |
| `SignatureSha256` | `AppxSignature.p7x` 的 SHA256 | 少了它沒有串流安裝;而 MSIX 本來就必須簽章才收 |
| `InstallerSha256` | 資產本身的 SHA256 | 必填 |
| `Platform` | `Windows.Desktop` | —— |
| `MinimumOSVersion` | `10.0.19041.0`,跟 `Package.appxmanifest` 的 `TargetDeviceFamily/@MinVersion` 一致 | 兩邊不一致會裝到不支援的機器上 |
| `Tags` | 要含 **`windows-commandpalette-extension`** | CmdPal 自己的擴展搜索是靠這個 tag 找套件的 —— 少了它,使用者在 Command Palette 裡搜不到 |

`SignatureSha256` 從 msixbundle 裡取(bundle 是 zip):

```powershell
$bundle = 'artifacts\Inkling_v0.2.0.msixbundle'
$tmp = Join-Path $env:TEMP 'inkling-sig'
Expand-Archive $bundle $tmp -Force
(Get-FileHash "$tmp\AppxSignature.p7x" -Algorithm SHA256).Hash
```

`ManifestVersion` 用當下 winget-pkgs 收的最新 schema —— **不要照抄這份文檔裡的版本號**,
schema 一路在動(`.claude/skills/publish-extension` 底下那份參考也已經落後過一次)。

## 5. CmdPal Extension Gallery 提交

前提:已上 WinGet 或 Store(gallery 的 `installSources` 必填其中一個 id)。

在 microsoft/CmdPal-Extensions 開 PR(需簽 Microsoft CLA):

- 建 `extensions/<author>/inkling/`,id 用 `<author>.inkling`,**必須與資料夾路徑一致**
  (CI 會驗 schema)。
- `extension.json`:categories 建議 `productivity`,tags ≤ 5,title **不得含
  "for Command Palette"**。
- `icon.png`:PNG/JPEG、≤ 100KB、建議 256x256,**SVG 不收** —— 用
  `tools/render-icons.ps1` 從 `assets/icon/*.svg` 另外算一張。
- `screenshots/` 至多 5 張,每張 ≤ 1MB。

## 6. 公開 repo 之前的最後檢查

- [ ] LICENSE 已存在(MIT)—— 已完成。
- [ ] `.gitignore` 擋住 `*.pfx` 等簽章產出物 —— 已完成。
- [ ] git 歷史裡沒有任何憑證、私鑰或本機路徑敏感資訊(`git log -p | grep -i pfx` 之類掃一次)。
- [ ] 兩份 README(`README.md` 英文、`README.zh-Hant.md` 繁中)的〈安裝〉章節:目前是
      「還沒發佈」的佔位,第一個 release 出來就把對應那一條換成真的下載 / 安裝指令
      (Releases → WinGet → Store,開通一條補一條),而且兩份一起改。
      `docs/development.md` 的 clone URL 已經是真的。
- [ ] 兩份 README 共用的截圖與 GIF(`docs/images/`)跟當前版本一致(命令標題、圖示、版面都會過期;
      重拍前先把筆記資料夾指到 demo 資料夾,別把真的筆記放進公開 repo;
      流程見 `docs/development.md`〈重拍截圖與 GIF〉)。
- [ ] GitHub repo 的 description / topics 還對得上
      (`gh repo view --json description,repositoryTopics`);social preview 是
      `assets/social-preview.png`,沒有 API,要手動到 Settings → General → Social preview 上傳。
- [x] SECURITY.md 的私密回報管道已啟用並確認開著
      (`gh api repos/<owner>/<repo>/private-vulnerability-reporting` → `{"enabled":true}`)。
      **沒開的話那條路對外部回報者是死的** —— 只有有寫入權限的人打得開 advisories 的表單,
      而 SECURITY.md 同時又叫人不要開公開 issue,等於兩條路都沒有。
      要關是同一條路換 `-X DELETE`。
