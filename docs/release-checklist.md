# 首次公開發佈 checklist

這份清單是「從本機側載專案」走到「公開散佈」的完整待辦。順序有意義:
**身分一定要在第一個公開版本之前定案**,之後永久凍結。

## 1. 定案套件身分(Name / Publisher)—— 只能定一次

`Package.appxmanifest` 目前是本機側載用的自簽身分:

```xml
<Identity Name="Notelet" Publisher="CN=Notelet Development" Version="0.1.0.0" />
```

為什麼這件事只能做一次:

- `Name` + `Publisher` 決定 package family name(PFN,目前是
  `Notelet_bf0n0751x5hse`)。換任何一個,PFN 就變。
- Windows 按 PFN 隔離每個套件的 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`。
  PFN 一變,舊的 `settings.json`(筆記資料夾、快速記下的分隔線與預覽開關)
  變成孤兒 —— 等於使用者的設定被重置。
- CmdPal 端的設定(alias、全域快速鍵、釘選、fallback 規則)也存在它自己的
  LocalState 裡,**以 PFN 為鍵**。PFN 一變,使用者設過的三個 alias 全部失效。
- 目前只有作者一台機器受影響,這正是換身分的唯一時機;一旦有人公開安裝,
  再換就是把所有使用者的設定與 alias 一起洗掉。

兩條路擇一:

### (a) Microsoft Store 代簽(建議先走這條)

- 註冊 Partner Center(**個人開發者現在免費**),保留 `Notelet` 這個名字,
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

`Notelet_bf0n0751x5hse` 這個字串目前硬編碼在八處(五個檔案),換 Publisher 後**全部會靜靜失效**
(讀不到檔案不會報錯,只會讓驗證失明):

- `CLAUDE.md`(兩處:DiagnosticLog 說明、文末的設定檔路徑表)
- `README.md`(兩處:〈設定存在哪,更新擴展之後還在嗎〉、疑難排解的 DiagnosticLog 段)
- `docs/manual-test-checklist.md`(§11 讀 diagnostic.log 那條)
- `.claude/skills/verify-cmdpal-ui/SKILL.md`(兩處:換測試資料夾的腳本、`diagnostic.on` 的路徑)
- `.github/ISSUE_TEMPLATE/bug_report.yml`(請回報者開診斷日誌的那段)

文檔類建議統一改寫成「`%LOCALAPPDATA%\Packages\<PFN>\LocalState`,PFN 用
`(Get-AppxPackage Notelet).PackageFamilyName` 查」,以後就不用再改。
`tools/cmdpal-ui.ps1` 已經是那樣(開頭用 `Get-AppxPackage -Name Notelet` 動態取 PFN,
取不到直接中止),不在清單內。

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
2. 跑過 `docs/manual-test-checklist.md`(至少發版相關的段落)。
3. 打 tag:`git tag v0.2.0 && git push origin v0.2.0`。
4. release.yml 自動:建 x64 + ARM64(trimmed publish)→ 注入版本 → 組 msix →
   (有設憑證 secret 才)簽章 → 組 msixbundle → 建 GitHub Release 附資產。
5. 走 Store 路線:從 Release 資產拿下 msixbundle,上傳 Partner Center 送審。

## 4. WinGet 上架

- 前提:已簽章的 msix((b) 路線的 CI 產出,或 Store 簽好拿回來的)。
- 在 microsoft/winget-pkgs 開 manifest:`InstallerUrl` 指向 GitHub Release 的資產,
  `License` / `LicenseUrl` 填 MIT 與 repo 的 LICENSE 連結(已備妥)。
- `PackageIdentifier` 建議 `<author>.Notelet`,版本與 tag 對齊。

## 5. CmdPal Extension Gallery 提交

前提:已上 WinGet 或 Store(gallery 的 `installSources` 必填其中一個 id)。

在 microsoft/CmdPal-Extensions 開 PR(需簽 Microsoft CLA):

- 建 `extensions/<author>/notelet/`,id 用 `<author>.notelet`,**必須與資料夾路徑一致**
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
- [ ] README 的安裝說明與 clone URL 換成真的。
- [ ] SECURITY.md 的私密回報管道在 repo 公開後確認可用(Security tab → Advisories)。
