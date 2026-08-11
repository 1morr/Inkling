# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Notelet 是 PowerToys Command Palette(CmdPal)的筆記擴展:MSIX 套件、跑在自己的
out-of-process COM server 裡,把想法存成資料夾裡的 Markdown 檔。使用者的目標是
「叫出 CmdPal → 打字 → Enter」,所以任何拖慢主搜尋框的做法都不能接受。

## 常用指令

```powershell
dotnet test                                             # Core 全部行為
dotnet test --filter "FullyQualifiedName~QuickCapture"  # 單一測試類別/方法
dotnet build src\Notelet\Notelet.csproj -p:Platform=x64 # 只建擴展

.\tools\deploy.ps1 -Configuration Release -Reload       # 日常部署(trimmed + 自動重載)
.\tools\deploy.ps1                                      # Debug 部署(~106 MB,不 trim)
.\tools\deploy.ps1 -Configuration Release -SkipBuild    # 只重新註冊

dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult
dotnet run --project tools\ApiDump -- --paths           # 設定檔實際存在哪
```

- **方案層級不能帶 `-p:Platform=x64`**(`Notelet.slnx` 沒有那個組態,會直接失敗)。
  x64 只屬於 `src/Notelet`;測試與工具專案跑 AnyCPU。
- 部署後**一定要 Reload**,否則 CmdPal 繼續用舊的擴展實例,你會以為改動沒生效。
  `-Reload` 需要 CmdPal 設定 → 一般 → For developers → Enable external reload。
- 擴展沒有主控台,`Debug.WriteLine` 在 Release 被編掉。要確認某段程式有沒有跑到,
  用 `DiagnosticLog`:在 `%LOCALAPPDATA%\Packages\Notelet_bf0n0751x5hse\LocalState\`
  建一個空檔 `diagnostic.on`,Reload,然後看同目錄的 `diagnostic.log`。

## 架構

兩層,界線是「能不能自動化測試」:

- **`src/Notelet.Core`** — 純 `net10.0`,**不引用任何 CmdPal 型別**。front matter 解析、
  檔名/id 產生、搜索排序、標題/內文切分(`QuickCapture.Split`)、預覽的換行規則,
  全部在這一層,因此全部有單元測試。
- **`src/Notelet`** — MSIX COM server,只負責把 Core 的結果翻譯成 `IListItem` / `IContent`。
  CmdPal 的 UI 沒有自動化介面,這一層的驗證只能靠 `docs/manual-test-checklist.md`。

新增行為時先問:這段邏輯能不能放進 Core?能的話就放,並補測試。
牽涉平台的部分(例如「刪除要送資源回收筒」需要 shell32)在 Core 留一個介面,
實作放 UI 層並從外面注入 —— `IFileDeleter` / `RecycleBinFileDeleter` 就是這個形狀,
測試因此可以用假的實作,不會真的去動使用者的資源回收筒。

`NoteletCommandsProvider` 持有一個 `ProviderState`(資料夾 + repository + 清單頁 +
快速記下頁 + 命令陣列)。**只有資料夾變了才整組重建**並釋放舊的 —— 那時 repository
非換不可。會訂閱 `repository.Changed` 的頁面都要進 `ProviderState` 並在 `Dispose` 裡退訂,
否則改幾次資料夾之後同一個事件會有好幾個死頁面在聽。

**其他設定不能靠重建生效。** CmdPal 手上握著的是使用者當下開著的那個頁面實例,
新建的頁面它不會去拿(實測 log:`BuildState` 之後一次 `GetItems` 都沒有,直到 Reload)。
硬重建反而會把還在被使用的 repository 給 Dispose 掉。這類設定要讓**現有頁面自己響應**,
範例見 `IDetailsWidthStore.DetailsWidthChanged`。

`TopLevelCommands()` 絕不碰磁碟(CmdPal 啟動時就會呼叫),載入延後到使用者真的打開清單頁。

## 跟 CmdPal 打交道的硬規則

這些都是踩過的坑,不是理論。改動前先讀 README 對應章節。

1. **每個頂層命令都要有固定 `Id`**(`src/Notelet/CommandIds.cs`)。沒設的話 CmdPal 會拿
   `ProviderId + DisplayTitle + Title + Subtitle` 做 WyHash64 當身分 —— 標題變一個字,
   使用者的 alias / 快速鍵 / 釘選 / fallback 設定就全部對不上。那幾個字串是對外承諾,不能改。
2. **`ListItem.Details` 只能整個換掉,不能就地改屬性。** `IDetails` 在 SDK IDL 裡沒有宣告成
   可觀察介面,`DetailsViewModel` 用執行期型別測試決定要不要訂閱,那個 QI 跨不過
   out-of-process 邊界,而通知的例外又被吞掉 —— 表現出來就是「值改了、畫面不動」。
   `Details.Size` 更只在初始化時讀一次。`ICommandItem` 則相反,無條件訂閱,走它一定收得到。
3. **不要把快速記下改回 fallback。** 這條路做完過,最後整個移除 —— 只有 fallback 拿得到
   使用者正在打的字(`UpdateQuery`),但沒命中前綴時我們只能把 `Title` 設成空字串,
   而 0.11.11762.0 只在底部 fallback 區塊那條路濾空標題,勾了「Include in the Global result」
   走的 `_scoredFallbackItems` 沒濾 —— 每次搜索都多一個點不動的空列,而且不勾就排在
   所有結果後面、失去意義。**這不是我們能修的**,查證過程見 README
   〈快速記下為什麼是頁面,不是 fallback〉,實作在 git 歷史裡。
   現在的入口是 `QuickCapturePage` + 使用者自設的 alias,按鍵數一樣。
   alias 觸發時送 `ClearSearchMessage`,所以 **alias 命令拿不到觸發當下那句話**,
   但進到自己的 `DynamicListPage` 之後打的字完全掌控 —— 那正是這個做法能成立的原因。
4. **Adaptive Cards 表單能調的極少**:欄位順序決定游標落在哪(沒有 autofocus / tabIndex)、
   多行輸入框的高度完全不可控(只能靠預填內容撐開)、沒有 `Ctrl+S`(表單值只活在 CmdPal 進程裡)。
5. **重新註冊套件後有時會出現兩個 Notelet** —— CmdPal 在套件安裝事件上沒有去重。再 Reload
   一次即可,不必重開 PowerToys。同一個根源還有一個更會騙人的症狀:**Reload / 重新部署之後,
   之前開著的設定頁是綁在舊擴展實例上的死物件,按 Save 靜靜地什麼都不做** ——
   不寫檔、不重建、不報錯。查這種「改設定沒反應」之前,先把設定頁關掉重開。
6. CsWinRT 的要求:任何實作 WinRT 投影介面的型別都要標 `partial`(內部型別也一樣)。
   trimming 只在 `dotnet publish` 生效,所以 trimming 相關的問題只有 Release 部署才驗得到。

## 慣例

- **文檔、程式碼註釋一律繁體中文**;識別符、字串常量、log、commit message 用英文。
- 註釋寫「為什麼」,特別是繞過 CmdPal 限制的地方 —— 這個 repo 的註釋密度刻意偏高,
  因為那些取捨從程式碼本身看不出來,半年後會被當成多餘而刪掉。
- `TreatWarningsAsErrors` + `AnalysisMode=Recommended` 全域開啟。測試專案只關掉
  CA1707 / CA1861。
- 擴展的執行期依賴釘在官方模板驗證過的版本(見 `Directory.Packages.props` 的說明),
  不要順手升級。
- 資料格式是承諾:`id` 才是身分(改標題不重新命名檔案)、不認得的 front matter 欄位
  原樣保留、沒有 front matter 的外來 `.md` 也要能列出來。
- 改了指令、設定項、資料格式或對外行為,同一輪更新 `README.md` 與
  `docs/manual-test-checklist.md`。

工具面的地雷:

- **圖示碼位不要寫成 `\uXXXX`。** `Icons.cs` 現在用 `Glyph(0xE70B)`
  (`char.ConvertFromUtf32`)。以前用 `\u` 逸出,結果是各種文字處理工具會把它當成
  逸出序列展開 —— 用工具改那個檔案時碼位會**無聲地**變成一個私用區字元,
  檔案看起來還是好的,圖示卻全部消失。數字碼位沒有這個問題,Edit 工具也比對得到。
- commit 用 `git commit -F <訊息檔>`。用 PowerShell here-string 傳 `-m` 曾經把整棵工作樹
  掃進 commit;送出前先 `git diff --cached --name-only` 確認範圍。

## 查證 CmdPal 的行為

Microsoft Learn 上的 API 參考有些頁面對不上 0.11 的實際簽章。要確認就直接問組件
(`tools/ApiDump`)或讀 PowerToys 原始碼。

原始碼曾經 sparse clone 在 `%TEMP%\cp-spike`(只取 `src/modules/cmdpal`,而且是 `--depth 1`,
沒有歷史可查),那是暫存目錄,可能已經不在;需要時重新 clone。**注意版本落差**:那份是 `main`,
比使用者裝的 0.11.11762.0 新。從原始碼得到的結論要跟安裝版對照 —— 實用手法是拿方法名去
byte-scan `Microsoft.CmdPal.UI.exe`,確認那條程式路徑在安裝版裡到底存不存在:

```powershell
$d = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe"
Get-ChildItem $d -Recurse -Include *.dll,*.exe | Where-Object {
  [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($_.FullName)).Contains('要找的方法名')
} | Select-Object -ExpandProperty Name
```

(別用 `Select-String -Encoding Byte`,PowerShell 7 已經移除那個參數,整條會靜靜地失敗。)

**已知落差**:`MainListRanker` / `ClassifyTier` / `FallbackFloor` 在 `main` 有、安裝版**沒有**。
README 曾經照 `main` 寫過一段 fallback 排序的說明,對安裝版來說整段是錯的 —— 這就是為什麼
每個從原始碼得到的結論都要 byte-scan 對照一次再寫進文檔。

兩份設定檔:

| | 位置 |
|---|---|
| Notelet 自己的設定 | `%LOCALAPPDATA%\Packages\Notelet_bf0n0751x5hse\LocalState\settings.json` |
| CmdPal 端(啟用、alias、快速鍵、釘選、fallback 規則) | `%LOCALAPPDATA%\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json` |
