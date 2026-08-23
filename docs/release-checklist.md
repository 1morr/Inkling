# 首次公開發佈 checklist

**只剩兩件真的一輩子只做一次的事**:套件身分為什麼永久凍結(§1),
與公開 repo 之前的最後檢查(§3)。順序有意義 ——
**身分一定要在第一個公開版本之前定案**,之後永久凍結。

> **每次發新版本走 [`release-runbook.md`](release-runbook.md),不是這一份。**
> 這裡原本還抄了版本策略、發版流程、WinGet、gallery 四節,2026-08-23 整組刪掉,
> 對照表留在 §2。**§3 那兩項打完勾之後整份就可以移除**,做法見 runbook 第 26 步。

## 1. 套件身分 —— ✅ 已定案(2026-08-23)

**這一節已經做完了,留著是為了說明那幾個字串為什麼不能再動。**
`Package.appxmanifest` 帶的是 Partner Center 指派的身分:

```xml
<Identity Name="CPPt.InklingNotes" Publisher="CN=CCDB8684-D6F1-4A3A-BF5C-F31F3FE830E9" Version="0.1.0.0" />
```

| Partner Center 的值 | 去哪裡 |
|---|---|
| `CPPt.InklingNotes` | `Identity/@Name` |
| `CN=CCDB8684-D6F1-4A3A-BF5C-F31F3FE830E9` | `Identity/@Publisher` |
| `CPPt` | `Properties/PublisherDisplayName` |
| 保留的名字 `Inkling Notes` | `Properties/DisplayName` 與 `uap:VisualElements/@DisplayName` |

來源是該 app 的 **產品管理 → 產品標識**。**對不上的話 `makeappx pack` 與 CI 都不會報錯**,
只有上傳 Partner Center 那一刻才會被退。

**Store 上的名字是「Inkling Notes」,CmdPal 裡看到的仍然是「Inkling」** ——
後者走 `.resx`,跟保留名稱無關。`Inkling` 在 Store 被商標擋下(Inkling Systems /
inkling.com),所以上架名加了 Notes;命令標題沒有跟著加,短的比較好按。
**gallery 與 Store 的 product 資訊**:Store ID `9NDGWN4JTXHH`,
listing 是 <https://apps.microsoft.com/detail/9NDGWN4JTXHH>(上架後才會活)。

⚠ **這裡沒有任何憑證,以後也不會有。** repo 樹內沒有任何 `.pfx` / `.p12` / `.cer`,
Store 會在通過認證之後用微軟的憑證重簽;本機部署走 `Add-AppxPackage -Register`
(開發者模式的 loose-file 註冊),兩條路都不需要我們自己簽。**只有要側載散佈或上
winget-pkgs 才需要買憑證**(見下面 (b))。

為什麼這件事只能做一次:

- `Name` + `Publisher` 決定 package family name(PFN,目前是
  `CPPt.InklingNotes_fsn608qftpbpp`)。**後綴那串雜湊只由 `Publisher` 決定**,`Name` 換了
  只換前半段。**那個雜湊算不出來,只能註冊一次之後量** ——
  `(Get-AppxPackage '*Inkling*').PackageFamilyName`
  (實測與 Partner Center 產品標識頁預告的 PFN 一字不差)。
- Windows 按 PFN 隔離每個套件的 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`。
  PFN 一變,舊的 `settings.json`(筆記資料夾、快速記下的分隔線與預覽開關)
  變成孤兒 —— 等於使用者的設定被重置。
- CmdPal 端的設定分兩種鍵,**不要混為一談**(實測 CmdPal 的 settings.json 得到的):
  - `ProviderSettings` 與 `PinnedCommands` 用 `<PFN>!<Application Id>!<AppExtension Id>`
    當鍵(實測是 `CPPt.InklingNotes_fsn608qftpbpp!App!Inkling`),PFN 一變就孤兒化。
    ⚠ **第三段不是 `CommandIds.Provider`,即使兩個現在是同一個字。** 鍵的結尾那個
    `Inkling` 來自 `Package.appxmanifest` 的 `uap3:AppExtension Id`,第二段來自
    `Application Id="App"`;`CommandIds.Provider` 現在剛好也是 `"Inkling"`,
    **那是巧合,兩邊沒有任何關係** —— 改 `CommandIds.Provider` 不會動到這個鍵,
    改 manifest 的 `AppExtension Id` 也不會動到 `CommandIds.Provider`。
    (前綴改名前這兩個值長得不一樣,陷阱看得見;現在重疊了,所以要寫死在這裡。)
    **manifest 那兩個字串跟 `Name` / `Publisher` 一樣是「只能定一次」的身分**,
    動了同樣會把使用者的啟用狀態、釘選與 fallback 設定洗掉。
  - **`Aliases` 用的是純命令 Id**(`"CommandId": "Inkling.List"`),條目裡沒有 PFN、
    也沒有 provider 參照 —— 所以只要 `CommandIds.cs` 的字串不動,alias 換身分後還在。
- 定案那時只有作者一台機器受影響,那正是換身分的唯一時機。**現在那個窗口已經關了** ——
  一旦有人公開安裝,再換就是把所有使用者的設定一起洗掉。

### 身分換過三次,全部在第一個公開版本之前

**2026-08-20 · 改名 Notelet → Inkling** —— 只動 `Identity Name`:

- `Publisher` 刻意不動,所以 PFN 的雜湊後綴沒變(`Notelet_bf0n0751x5hse` →
  `Inkling_bf0n0751x5hse`)。
- `CommandIds.cs` 當時那六個字串(`Scratchpad` 隔天才加)一併保留原值,
  換來的是使用者設過的 alias 全部活下來 —— 實地驗過,見
  [設計考證〈命令 Id 為什麼要寫死〉](design-notes.md#command-ids)。

**2026-08-22 · 把舊名字整個清掉** —— `Publisher` 換成 `CN=Inkling Development`,
命令 Id 前綴換成 `Inkling.`:

- 代價是 PFN 換掉(`Inkling_b83qevkfx7m2r` —— **過渡值,隔天就被下面那一步取代了**,
  不要拿它去對任何東西),擴展自己的 `settings.json`、CmdPal 端的啟用狀態與釘選
  全部孤兒化,alias 也因為 Id 變了而失效。
- **那時安裝基數是作者一台機器**,實際損失是重設三個 alias,而留著舊名字要讓五個檔案
  長期說謊。理由與「什麼變了才該重新考慮」寫在
  [設計考證〈前綴換過一次,而且只有那一次〉](design-notes.md#command-ids)。
- ⚠ 換 `Publisher` 之後**第一次部署先顯式移除舊套件**:
  `Get-AppxPackage '*Inkling*' | Remove-AppxPackage -PreserveApplicationData`。
  `deploy.ps1` 自己的移除分支**只在 `InstallLocation` 不同時才觸發**,而這裡佈局路徑沒變、
  變的是身分,那個分支會被跳過(這是讀 `deploy.ps1` 得到的,不是實測 ——
  兩次換身分都先移除了,沒讓它走那條路)。
  它開頭那道 `$installed.Count -gt 1` 會擋下「已經有兩個」,但那是**下一次**部署才擋;
  同一次跑到最後的 `$registered -ne $targetLocation` 驗證**擋不住** ——
  兩個套件指向同一個路徑時那個比較會把陣列過濾成空,而空陣列是 falsy。

**2026-08-23 · 換成 Partner Center 指派的身分 —— 這一次是最後一次**:

- 開個人開發者帳號(**註冊費已取消,個人與公司都免費**;驗證方式是政府核發證件加自拍,
  帳號建好後最多 30 分鐘才全面生效)。
- 保留名字。`Inkling` **不可用** —— 撞到 Inkling Systems(inkling.com)的商標,
  而且那條路連 `reportapp@microsoft.com` 都救不了(那是給持有商標的人用的)。
  保留成 **`Inkling Notes`**;CmdPal 裡的命令標題沒有跟著改。
- 從 產品管理 → 產品標識 抄回 `Name` / `Publisher` / `PublisherDisplayName`,
  PFN 變成 `CPPt.InklingNotes_fsn608qftpbpp`(**與產品標識頁預告的一字不差**),
  **擴展自己的 `settings.json` 又孤兒化一次**(alias 這次沒事 —— 命令 Id 沒動)。
  移除舊套件那條注意事項同上一步。
- ⚠ **實測踩到的是另一件事:主搜尋框變成十列,兩組五列。** 那是 CmdPal 在套件安裝事件上
  沒有去重(CLAUDE.md 第 6 條的第一種),不是兩個套件 —— `Get-AppxPackage` 全程只回一個。
  再 Reload 一次收不回去,**停掉 `Microsoft.CmdPal.UI` 讓它重啟就好**,PowerToys 本身不用重開。
  換身分之後看到十列先想到這個。

### (a) Microsoft Store 代簽 —— ✅ 走的是這條

- 註冊 Partner Center(**個人開發者現在免費**),保留名字,
  取得指派的 `Name` / `Publisher`,改進 manifest。**以上都做完了。**
- 上傳 `.github/workflows/release.yml` 產出的**未簽章** msixbundle,Store 審核後代簽。
- 成本:帳號免費;時間成本是審核(首次通常數天)。
- 好處:不用買憑證、不用管憑證續期與保管;使用者從 Store 安裝,信任鏈由微軟處理。

### (b) 自購 OV 程式碼簽章憑證(走 WinGet / 直接散佈)—— 沒有走

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
兩個管道同一身分、同一個 LocalState。**但那個「拿回來」沒有文檔化的做法**,見第 4 節。

### 換身分當下必須同步更新的硬編碼 PFN

**已經處理掉了。** 那個字串以前硬編碼在八處(五個檔案),換身分後會全部靜靜失效
(讀不到檔案不會報錯,只會讓驗證失明)。改名那一輪順手全部改成動態查:

```powershell
(Get-AppxPackage '*Inkling*').PackageFamilyName
```

文檔裡一律寫成 `%LOCALAPPDATA%\Packages\<PFN>\LocalState`,腳本片段直接內插上面那一行。
`tools/cmdpal-ui.ps1` 本來就是那樣(開頭動態取 PFN,取不到直接中止)。

**新增文檔時別再把 PFN 寫死。** 唯一還留著字面值的是本節上方與〈身分換過三次〉——
那幾處的重點正是那個字串本身。**換過 `Publisher` 就要把它們重量一次**,
`Package.appxmanifest` 的註解現在只寫「要量」而不寫值,就是為了少一處會過期的地方。

## 2. 每次發版的東西全部搬到 runbook 了

這裡原本有〈版本策略〉〈發版流程〉〈WinGet 上架〉〈Gallery 提交〉四節,而
[`release-runbook.md`](release-runbook.md) 逐步寫過同樣的事,而且寫得更細。
兩份副本並存的話,下次發版第一個問題就會是「該翻哪一份」,而先過期的那一份
**不會有任何東西報錯** —— 所以四節整組刪掉,只留這張對照表:

| 原本的哪一節 | 現在看哪裡 |
|---|---|
| 版本策略(tag 是單一來源、MSIX 四段版本、第一段不能是 `0`) | runbook 第 4 ~ 6 步 |
| 發版流程(收 CHANGELOG → 驗證 → 打 tag → CI 出貨 → 送審) | runbook 第 7 ~ 18 步 |
| 發版時哪兩個欄位要英文(GitHub Release 正文、Store 的「What's new」) | runbook 第 7 步 |
| WinGet 上架的必填欄位與 `SignatureSha256` 的取法 | runbook 第 19 步 |
| CmdPal Extension Gallery 投稿 | runbook 第 20 步;欄位規則在 [`gallery/README.md`](gallery/README.md) |

## 3. 公開 repo 之前的最後檢查

- [x] LICENSE 已存在(MIT)。
- [x] `.gitignore` 擋住 `*.pfx` 等簽章產出物。
- [x] git 歷史裡沒有任何憑證、私鑰或本機路徑敏感資訊(`git log -p | grep -i pfx` 之類掃一次)。
      **2026-08-23 在身分定案那一輪之後重掃過**:命中全部是文檔在講憑證本身
      (「這裡沒有憑證」、「PFX 要以 base64 放進 repo secret」),沒有任何金鑰;
      `C:\Users\<名字>` 這類本機路徑在追蹤檔案裡零命中。
      **加了新 commit 就要重掃**,這條不是一勞永逸的。
- [ ] 兩份 README(`README.md` 英文、`README.zh-Hant.md` 繁中)的〈安裝〉章節:目前是
      「還沒發佈」的佔位,第一個 release 出來就把對應那一條換成真的下載 / 安裝指令
      (Releases → WinGet → Store,開通一條補一條),而且兩份一起改。
      `docs/development.md` 的 clone URL 已經是真的。
- [x] 兩份 README 共用的截圖與 GIF(`docs/images/`)跟當前版本一致(命令標題、圖示、版面都會過期;
      重拍前先把筆記資料夾指到 demo 資料夾,別把真的筆記放進公開 repo;
      流程見 `docs/development.md`〈重拍截圖與 GIF〉)。
- [ ] GitHub repo 的 description / topics 還對得上
      (`gh repo view --json description,repositoryTopics`)—— **2026-08-23 對過**,
      description 與九個 topics 都在,含 `windows-commandpalette-extension`。
      **還沒做的是 social preview**:`assets/social-preview.png` 沒有 API,
      要手動到 Settings → General → Social preview 上傳。
- [x] SECURITY.md 的私密回報管道已啟用並確認開著
      (`gh api repos/<owner>/<repo>/private-vulnerability-reporting` → `{"enabled":true}`)。
      **沒開的話那條路對外部回報者是死的** —— 只有有寫入權限的人打得開 advisories 的表單,
      而 SECURITY.md 同時又叫人不要開公開 issue,等於兩條路都沒有。
      要關是同一條路換 `-X DELETE`。
