# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Inkling 是 PowerToys Command Palette(CmdPal)的筆記擴展:MSIX 套件、跑在自己的
out-of-process COM server 裡,把想法存成資料夾裡的 Markdown 檔。使用者的目標是
「叫出 CmdPal → 打字 → Enter」,所以任何拖慢主搜尋框的做法都不能接受。

## 常用指令

```powershell
dotnet test                                             # Core 全部行為
dotnet test --filter "FullyQualifiedName~QuickCapture"  # 單一測試類別/方法
dotnet test tests\Inkling.Tests\Inkling.Tests.csproj -p:Platform=x64   # 擴展層(不在方案裡,見下)
dotnet build src\Inkling\Inkling.csproj -p:Platform=x64 # 只建擴展(進程活著時會鎖輸出,見下方)

.\tools\deploy.ps1 -Configuration Release -Reload       # 日常部署(trimmed + 自動重載)
.\tools\deploy.ps1                                      # Debug 部署(~106 MB,不 trim)
.\tools\deploy.ps1 -Configuration Release -SkipBuild    # 只重新註冊

dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult
dotnet run --project tools\ApiDump -- --paths           # toolkit 在當前身分下用的設定路徑(ApiDump 不是 packaged,印的是 unpackaged 路徑,僅供對照;擴展實際的設定檔看文末表格)

pwsh -NoProfile -File tools\render-icons.ps1                # 改完圖示的 SVG 之後重產 PNG

# 在真機上驗畫面(要 pwsh,一整串動作必須在同一次呼叫裡跑完)
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1400|tree:6"
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "notes"   # 先確認資料夾是不是真資料
```

- **方案層級建不了擴展。** `dotnet build Inkling.slnx` 走 AnyCPU,會撞 MSIX 打包目標的
  「Packaged .NET applications with an app host exe cannot be ProcessorArchitecture neutral」
  —— 打包專案不吃 AnyCPU,帶 `-p:Platform=x64` 也救不了(`Inkling.slnx` 沒有那個組態,
  帶了直接失敗;`.slnx` 的 Platform 對應語法試過,它的 schema 不吃)。要建擴展就指定專案:
  `dotnet build src\Inkling\Inkling.csproj -p:Platform=x64`。`dotnet test` 不受影響,
  它只建測試專案與 Core。完整考證見 `src/Inkling/Inkling.csproj` 的 `PublishSingleFile` 註解。
- **「只建擴展」那條在擴展進程活著時會失敗**(MSB3021,輸出檔被佔用)—— CmdPal 平常
  就把擴展的 COM server 常駐拉起。先 `Stop-Process -Name Inkling`,或直接用
  `deploy.ps1`(它會先停進程再建)。
- 部署後**一定要 Reload**,否則 CmdPal 繼續用舊的擴展實例,你會以為改動沒生效。
  `-Reload` 需要 CmdPal 設定 → 一般 → For developers → Enable external reload。
  **Reload 之後還是要對一次進程時間**:實際踩過一次 —— 部署說「已送出 reload」、檔案也真的
  換了,但舊的 `Inkling.exe` 還活著,CmdPal 一路用它,畫面上看到的是上一版的行為
  (那次是選單順序,查了很久才發現不是程式碼的問題)。一行就驗得掉:
  `Get-Process Inkling | Select-Object StartTime` 要**晚於**
  `src\Inkling\bin\stage-Release\Inkling.dll` 的 `LastWriteTime`;不是的話 `Stop-Process -Name Inkling`,
  再不行就連 `Microsoft.CmdPal.UI` 一起停掉重來。
- 擴展沒有主控台,`Debug.WriteLine` 在 Release 被編掉。要確認某段程式有沒有跑到,
  用 `DiagnosticLog.Write`:在 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\`
  建一個空檔 `diagnostic.on`,Reload,然後看同目錄的 `diagnostic.log`。
  `<PFN>` 用 `(Get-AppxPackage Inkling).PackageFamilyName` 查 —— 不要把它寫死進文檔,
  換套件身分時那些字串會靜靜失效(讀不到檔案不報錯,只會讓驗證失明)。
  **失敗要用 `DiagnosticLog.Failure`**:上面那個檔預設是關的,而使用者回報問題時
  失敗現場多半沒被記下來。`Failure` 會另外送一份到 CmdPal 自己的 log
  (`%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\<版本>\`),那份永遠開著,
  訊息帶 `[Inkling]` 前綴(所有擴展共用同一份)。**只有真的失敗才用它** ——
  追蹤性質的訊息塞進去會把別的擴展的線索淹掉。實測見 `docs/development.md`。

## 架構

兩層,界線是「能不能自動化測試」:

- **`src/Inkling.Core`** — 純 `net10.0`,**不引用任何 CmdPal 型別**。front matter 解析、
  檔名/id 產生、搜索排序、標題/內文切分(`QuickCapture.Split`)、預覽的換行規則,
  全部在這一層,因此全部有單元測試。跨消費者的概念只留一份實作 ——
  「內文的第一行有效文字」收在 `NoteBody`(清單摘要、外來檔案的推導標題、
  預覽判斷「內文是否已含標題」三處共用),曾經各寫一份而且字元集已經漂移過。
- **`src/Inkling`** — MSIX COM server,只負責把 Core 的結果翻譯成 `IListItem` / `IContent`。
  這一層**有測試**(`tests/Inkling.Tests`),但只測「不需要 CmdPal 在跑」的那部分:
  頁面的命令順序與快速鍵(底部工具列那兩顆按鈕是位置鍵,插一項就換掉 `Enter` 的意思)、
  三個清單頁的快取鍵、`Dispose` 有沒有把訂閱退乾淨。那個專案**刻意不在 `Inkling.slnx` 裡**
  —— 它引用 MSIX 專案,所以必須 x64 且 self-contained,而方案層級走 AnyCPU,加進去只會讓
  每次 `dotnet test` 都印一個 NETSDK1150。跑法是上面那條指定專案的指令,CI 兩條路都有跑。
  能看到 internal 是靠 `Inkling.csproj` 裡的 `InternalsVisibleTo`,不是把型別改成 public。
  畫面本身(顏色、圖示、游標位置、有沒有跳 toast)還是靠 `docs/manual-test-checklist.md`;
  清單內容、快速鍵、placeholder、有沒有跳 toast 這些**可以**用
  `tools\cmdpal-ui.ps1` 驅動 Windows 的 UI Automation 驗掉(見
  `.claude/skills/verify-cmdpal-ui/`),但顏色、圖示長相、游標位置只能靠眼睛。
  **命令跑出面板以外的視窗時(在編輯器開啟、檔案位置、「瀏覽…」對話框)那個腳本管不到,
  改用 `orca computer`** —— 它讀不到 CmdPal 的面板,但檔案總管、編輯器、Win32 對話框
  都讀得到也點得動,用法與踩過的坑同樣寫在那份 skill 裡。

新增行為時先問:這段邏輯能不能放進 Core?能的話就放,並補測試。
牽涉平台的部分(例如「刪除要送資源回收筒」需要 shell32)在 Core 留一個介面,
實作放 UI 層並從外面注入 —— `IFileDeleter` / `RecycleBinFileDeleter` 就是這個形狀,
測試因此可以用假的實作,不會真的去動使用者的資源回收筒。

`InklingCommandsProvider` 持有一個 `ProviderState`(資料夾 + repository + 清單頁 +
快速記下頁 + 刪除頁 + 命令陣列)。**只有資料夾變了才整組重建**並釋放舊的 —— 那時 repository
非換不可。會訂閱 `repository.Changed` 的頁面都要進 `ProviderState` 並在 `Dispose` 裡退訂,
否則改幾次資料夾之後同一個事件會有好幾個死頁面在聽。

**其他設定不能靠重建生效。** CmdPal 手上握著的是使用者當下開著的那個頁面實例,
新建的頁面它不會去拿(實測 log:`BuildState` 之後一次 `GetItems` 都沒有,直到 Reload)。
硬重建反而會把還在被使用的 repository 給 Dispose 掉。這類設定要讓**現有頁面自己響應**:
`SettingsManager` 為每一項開一個窄介面 + 一個事件,由頁面自己訂閱 ——
`ICaptureSeparatorStore.CaptureSeparatorChanged` 與
`ICapturePreviewStore.CapturePreviewChanged`(兩個都在快速記下頁)就是這個形狀。
第三個 `ISourceModeStore`(`Ctrl+U` 的原始文字模式)形狀一樣但**有 setter** ——
它是頁面自己寫的檢視狀態,不是設定頁寫的,而且會存進 settings.json。
**只有長壽的頁面能訂閱那個事件**:預覽頁與記下並預覽頁是清單裡每個項目各建一個的短命物件,
訂閱等於一路累積死掉的訂閱者,那兩頁改成在 `GetContent()` 當下讀一次。
清單頁的項目快取收在 `VersionedItemsCache`(三個清單頁共用,基底型別不同抽不了
共同基底,所以用組合):**快取鍵要帶 repository 的 `Version` 與每一個影響內容的設定值**,
否則事件收到了、拿到的還是舊結果(「筆記明明存好了,清單卻說還沒有」就是漏帶
Version 的症狀)。

同一則筆記的 `Ctrl+K` 選單項(編輯 / 複製內文 / 在編輯器開啟 / 開啟檔案位置)收在
`NoteCommands`,清單頁、預覽頁、記下並預覽頁三個畫面共用 —— 鍵位與圖示跨頁要一致,
曾經各頁各刻一份而且已經漂移過(其中一份用了預設 `ShowToast` 的 `CopyTextCommand`,
一複製整個面板消失)。各頁專屬的項仍由各頁自己插。

**順序有語意,而且兩種頁面的算法不一樣。** 底部工具列的主命令(`Enter`)與次命令
(`Ctrl+Enter`)坐的是誰**只看排序**,跟命令自己的 `RequestedShortcut` 無關:
`ListPage` 的一列是「那一列自己的命令 + `MoreCommands[0]`」,`ContentPage` 是
「`Commands[0]` + `Commands[1]`」。兩個 `ContentPage` 的前兩項是「編輯」與「完成」,
**順序刻意相反**:預覽頁是「編輯、完成」(在清單裡找到某一則才進來的,下一步多半是改它),
記下並預覽頁是「完成、編輯」(剛打完字看一眼,下一步是收工)。曾經兩頁都是「完成、編輯」
好讓 `Ctrl+Enter` 跨頁同義,但那讓預覽頁的 `Enter` 變成「把面板收掉」,代價更大。
**加新項目不要插進前兩個位置**(踩過:切換原始文字排到第二個,複製內文就被擠掉了)。考證見
[設計考證〈兩個位置鍵:預覽頁與記下並預覽頁刻意相反〉](docs/design-notes.md#secondary-command)。

`TopLevelCommands()` 絕不碰磁碟(CmdPal 啟動時就會呼叫),載入延後到使用者真的打開清單頁。

建置、部署與排錯全部在 [`docs/development.md`](docs/development.md) ——
[建置與本機部署](docs/development.md#build)、[專案結構](docs/development.md#structure)、
[CI 覆蓋到哪裡](docs/development.md#workflows)。

## 跟 CmdPal 打交道的硬規則

這些都是踩過的坑,不是理論。改動前先讀 `docs/design-notes.md` 的對應章節。

1. **每個頂層命令都要有固定 `Id`**(`src/Inkling/CommandIds.cs`)。沒設的話 CmdPal 會拿
   `ProviderId + DisplayTitle + Title + Subtitle` 做 WyHash64 當身分 —— 標題變一個字,
   使用者的 alias / 快速鍵 / 釘選 / fallback 設定就全部對不上。那幾個字串是對外承諾,不能改。
   **它們現在還叫 `Notelet.*`,那是改名前的名字,故意留著** —— CmdPal 的 `Aliases` 用純命令 Id
   當鍵(條目裡沒有 PFN),改了等於把使用者設過的 alias 清掉,而使用者根本看不到這些字串。
   新增命令時給新 Id,不要為了整齊回頭改舊的。考證見
   [設計考證〈命令 Id 為什麼要寫死〉](docs/design-notes.md#command-ids)。
2. **`ListItem.Details` 只能整個換掉,不能就地改屬性。** `IDetails` 在 SDK IDL 裡沒有宣告成
   可觀察介面,`DetailsViewModel` 用執行期型別測試決定要不要訂閱,那個 QI 跨不過
   out-of-process 邊界,而通知的例外又被吞掉 —— 表現出來就是「值改了、畫面不動」。
   `Details.Size` 更只在初始化時讀一次。`ICommandItem` 則相反,無條件訂閱,走它一定收得到。
3. **不要把快速記下改回 fallback。** 這條路做完過,最後整個移除 —— 只有 fallback 拿得到
   使用者正在打的字(`UpdateQuery`),但沒命中前綴時我們只能把 `Title` 設成空字串,
   而 0.11.11762.0 只在底部 fallback 區塊那條路濾空標題,勾了「Include in the Global result」
   走的那條評分路沒濾 —— 每次搜索都多一個點不動的空列,而且不勾就排在
   所有結果後面、失去意義。(那條路的名字對不到可驗證的識別名:0.11 安裝版兩種編碼
   都掃不到,`main` 也只有 `MainListPageResultFactory.Create` 的同義參數
   `scoredFallbackItems`;結論本身來自當時的真機重現。)**這不是我們能修的**,查證過程見
   [設計考證〈快速記下為什麼是頁面,不是 fallback〉](docs/design-notes.md#capture-page-not-fallback),實作在 git 歷史裡。
   現在的入口是 `QuickCapturePage` + 使用者自設的 alias,按鍵數一樣。
   alias 觸發時送 `ClearSearchMessage`,所以 **alias 命令拿不到觸發當下那句話**,
   但進到自己的 `DynamicListPage` 之後打的字完全掌控 —— 那正是這個做法能成立的原因。
4. **Adaptive Cards 表單能調的極少**:欄位順序決定游標落在哪(沒有 autofocus / tabIndex)、
   **游標在那一格裡的位置完全指定不了**(CmdPal 只做 `Focus(FocusState.Programmatic)`,
   `Input.Text` 沒有 caret / selection 屬性,WinUI `TextBox` 因此固定停在索引 0 ——
   編輯頁「游標放到內文最後」查過,做不到,現在是在卡片底部提示按 `Ctrl+End`)、
   多行輸入框的高度完全不可控(只能靠預填內容撐開)、沒有 `Ctrl+S`(表單值只活在 CmdPal 進程裡)。
   `TextBlock` 不是 `Control`,加幾塊說明文字不會把焦點從輸入框搶走。
5. **設定頁有兩個入口**(主搜尋框選中「Inkling」那一列按 `Ctrl+K` → 設定 —— 它掛在
   **頂層那一列的 `MoreCommands`** 上,所以 `Ctrl+Enter` 也直接到;**不是**進了清單頁之後按。
   另一個入口是 CmdPal 設定 → Extensions → Inkling)。
   後者 CmdPal **只初始化一次**,而且它拿的是 `ICommandSettings.SettingsPage` ——
   所以我們自己實作了 `ICommandSettings`(見 `InklingCommandSettings`),把
   `InklingSettingsPage` 交出去,才發得出 `ItemsChanged` 讓它重讀。那個頁面因此
   **不能跟著 `ProviderState` 重建**:CmdPal 在 provider 剛連上時就把 `Settings` 讀走了。
   **表單送出後一定要 `InklingSettingsPage.Refresh()`**(`OnSettingsApplied` 一進來就做,
   排在「資料夾沒變就 return」前面):卡片的值是建構時烤進 `DataJson` 的,而這個入口
   不會因為導覽進去就重新 `GetContent()`。漏掉的話那張卡片永遠停在啟動時的值,
   **而且下一次送出會把那個過期值當成使用者輸入寫回設定** —— 只改資料夾按儲存,
   就足以把別的設定默默還原。加新設定項時特別容易忘,忘了不會報錯。
   設定頁表單後面曾經掛著一塊**空的** `MarkdownContent`,用來擋「背景的設定視窗跳到前面」——
   **已經移除**:那招依賴的 `OnlyControlOnPage` 判斷在安裝版裡根本不存在(見〈已知落差〉),
   而且會觸發的情境隨著詳細面板寬度循環的移除一起沒了(`Ctrl+D` 當時是那個循環的鍵;
   它後來給了刪除、中間一度整個拿掉,現在又回到刪除身上 —— 見
   [設計考證〈`Ctrl+D` 兜了一圈回來〉](docs/design-notes.md#ctrl-d-roundtrip)),拿掉還換回了「打開設定頁游標自動落在第一個欄位」。
   說明文字一律寫在卡片裡(那裡才有 `isSubtle`,
   而且區塊之間有 32px 收不掉的間距)。表單本身也不是 toolkit 的 `Settings.ToContent()`
   而是自己畫的卡片(`InklingSettingsForm`):那張卡片放不下「瀏覽…」按鈕,而且它把
   `Label` 塞進 `Input.Text` 沒有的 `title` 屬性,欄位名等於不會顯示。
   存檔因此走 `SettingsManager.Apply`(toolkit 的 `RaiseSettingsChanged()` 是 internal)。
   細節見 [設計考證〈設定頁有兩個入口,而且只有一個會自己更新〉](docs/design-notes.md#settings-two-entries)。
6. **重新註冊套件後有時會出現兩個 Inkling** —— CmdPal 在套件安裝事件上沒有去重。再 Reload 一次
   有時收得回去,但**不保證**(實測兩次都沒有);清不掉就把 `Microsoft.CmdPal.UI`
   進程停掉讓它重啟,PowerToys 本身不用重開。
   **有另一種長得很像但成因完全不同的「多一列 Inkling」**:Windows 的應用程式清單項
   被 CmdPal 內建的應用程式搜索列進結果,按 Enter 沒反應(exe 是純 COM server)。
   副標是 manifest 的英文 `Description` 而不是我們的資源字串,而且只多一列不是多五列。
   `Get-StartApps | Where-Object { $_.Name -like '*Inkling*' }` 有輸出就是這一種 ——
   manifest 的 `AppListEntry="none"` 掉了,那一行**不要拿掉**,考證見
   [設計考證〈套件刻意不出現在開始功能表〉](docs/design-notes.md#app-list-entry)。同一個根源還有一個更會騙人的症狀:**Reload / 重新部署之後,
   之前開著的設定頁是綁在舊擴展實例上的死物件,按 Save 靜靜地什麼都不做** ——
   不寫檔、不重建、不報錯。查這種「改設定沒反應」之前,先把設定頁關掉重開。
7. **擴展進程沒有視窗,也不是前景進程。** 要開系統對話框(目前只有設定頁的「瀏覽…」,
   見 `FolderPicker`)得自己開一條 STA 執行緒,而且開出來的視窗**搶不到焦點** ——
   Windows 只讓前景進程這麼做。`FolderPicker` 用「找自己的可見頂層視窗 → SetForegroundWindow」
   兜過去;拉不上來時的退路是工作列按鈕,所以對話框**刻意不掛 owner**
   (代價是那顆按鈕的圖示固定是套件的 Square44x44Logo —— 工作列按鈕的圖示擴展改不了)。
   另外 CmdPal 主視窗一失焦就自己隱藏(沒有開關),所以對話框選完的結果要**當場存**,
   不能指望使用者回到表單再按儲存。
8. **想讓使用者「做完之後留在畫面上看」,就一個 toast 都不能發。** toast 是另一個會搶焦點的
   視窗,而主視窗一失焦就自我隱藏(第 7 條那個機制)—— 快速記下之後「CmdPal 整個消失」的
   成因是 toast,不是 `GoHome()`,後者的語意明明白白是「回主頁但保持開著」。
   **`ToastArgs.Result = KeepOpen` 救不回來**,搶焦點的是那個視窗本身,不是回傳值;
   toolkit 有幾個現成命令(例如 `CopyTextCommand`)預設就回 `ShowToast`,拿來用要記得改掉。
   **反過來,收工那一下 toast 是免費的,而且該發。** 判準不是「這裡會不會關面板」,
   而是**「使用者接下來還要不要看著這個面板」**:不要的話,toast 是唯一能在面板消失之後
   還留在畫面上的通道(InfoBadge 畫在面板上,面板收了就跟著沒了)。記下並預覽頁的「完成」、
   隨手草稿的存檔與「捨棄變更」都走這條 —— 記下並預覽頁那一顆跟關掉「記下後先看一眼」
   那條路共用 `Resources.CaptureSaved`,少了它同一個動作換個設定就沒有結尾確認。
   **唯一的例外是跳到外部程式那幾條**(第 11 條):那時焦點剛給了編輯器或檔案總管,
   toast 比它晚出現會把它壓下去,所以成功路徑一個字都不說。
   **這一條專指 `CommandResult.ShowToast`。`ToastStatusMessage` 名字很像但不是同一件事** ——
   它呼叫的是 `IExtensionHost.ShowStatus`,由 CmdPal 畫成一條橫跨面板底部的 `InfoBar`
   加一個計數 `InfoBadge`,不開視窗、不關面板,存檔提示用的就是它。
   **前提是 `ExtensionHost` 接到了 host**:那是靜態的,沒有在
   `CommandProvider.InitializeWithHost` 裡呼叫 `ExtensionHost.Initialize(host)` 的話,
   `Show()` 靜靜地什麼都不做 —— 這條路曾經整個是死的,而文檔一直寫成通的。
   見 [設計考證〈`ToastStatusMessage` 不是那個 toast〉](docs/design-notes.md#toast-status-message)。
   需要回饋又要留在原地時走 `ListItem.Tags`(見〈查證 CmdPal 的行為〉最後那段)。
   而**導頁也不能靠回傳值**:`CommandResult.GoToPage` 是空殼,SDK 有型別但
   `ShellViewModel.UnsafeHandleCommandResult` 的 switch 裡沒有那個 case(安裝版沒有,
   `main` 也沒有)。唯一還通的路是讓那一列的命令**本身就是一個 `IPage`** ——
   CmdPal 對頁面的處理是導覽而不是 `Invoke`,副作用因此得寫在 `GetContent()` 裡
   (`CapturedNotePage` 就是這個形狀)。三件事跟著來:`GetContent` **會被呼叫很多次**,
   副作用要自己上一次性旗標;CmdPal 讀 `Commands` 的時機**比 `GetContent` 早**
   (`InitializeProperties` 先 `BuildCommandViewModels` 後 `FetchContent`),要在存檔後
   才建得出來的命令只能換掉整個陣列、靠 `PropChanged` 讓它重讀;而清單項本身
   **不會**觸發 `GetContent`(`CommandViewModel.InitializeProperties` 只讀 Id / Name / Icon),
   所以「打字打到一半就存檔」不會發生。細節見 [設計考證〈記下之後要不要先看一眼〉](docs/design-notes.md#capture-preview)。
9. CsWinRT 的要求:任何實作 WinRT 投影介面的型別都要標 `partial`(內部型別也一樣)。
   trimming 只在 `dotnet publish` 生效,所以 trimming 相關的問題只有 Release 部署才驗得到。
   `[GeneratedComInterface]`(shell 的 COM 介面)需要 `AllowUnsafeBlocks`,
   而且**方法的宣告順序就是 vtable 順序** —— 排錯不會有編譯錯誤,只會呼叫到別的函式。
10. **快速鍵全部收在 `src/Inkling/Shortcuts.cs`,而且不能碰搜尋框的文字編輯鍵。**
    清單頁的焦點永遠在搜尋框上,而 CmdPal 在 `ShellPage_OnPreviewKeyDown` 的 tunneling
    階段就把鍵送去比對(`TryCommandKeybindingMessage` → `CheckKeybinding`),比 `TextBox`
    早收到 —— 綁走等於從搜尋框拿掉。**不能用的**:`Ctrl+A` / `C` / `X` / `V` / `Z` / `Y`、
    `Ctrl+Backspace`、`Delete`、`Ctrl+Delete`、`Ctrl+方向鍵`(以上 `TextBox` 的),
    以及 `Ctrl+K` / `Ctrl+Enter` / `Ctrl+,` / `Ctrl+I`(CmdPal 自己的)。
    已經用掉的:`Ctrl+E` 編輯、`Ctrl+N` 新增筆記(清單頁,開新增表單)、`Ctrl+U` 原始文字、
    `Ctrl+O` 外部開啟、`Ctrl+L` 檔案位置、`Ctrl+D` 刪除、`Ctrl+Shift+C` 複製。
    **偏好 `Ctrl+` 一個字母**,少一個修飾鍵就少一個;CmdPal 的 `WellKnownKeyChords`
    與各內建擴展的 `KeyChords.cs` 只當參考,跟「好按」衝突時以好按為準
    (現在的 `Ctrl+L` / `Ctrl+D` 就是這樣壓過 `Ctrl+Shift+E` / `Ctrl+Shift+Delete` 的;
    複製維持 `Ctrl+Shift+C`,因為 `Ctrl+C` 是搜尋框的,而那組鍵本來就是複製的慣例)。
    另外**同一個項目的選單裡撞鍵不會報錯** —— CmdPal 用 `TryAdd`,第二個被靜靜丟掉,
    只在它自己的 log 留一行 warning。
11. **toolkit 的現成命令會吞掉失敗,而且預設 `Result` 彼此不一致 —— 拿來用就要顯式指定。**
    `OpenUrlCommand` 預設 `KeepOpen`、`ShowFileInFolderCommand` 預設 `Dismiss`,
    於是同一個 `Ctrl+K` 選單裡兩個「跳出去」的鍵行為相反,而那不是誰決定的。
    失敗更麻煩:`OpenUrlCommand.Invoke` 把 `ShellHelpers.OpenInShell` 的 `bool` 丟掉,
    `ShowFileInFolderCommand` 對不存在的路徑整段跳過,兩個都**靜靜地什麼都不做**。
    現在各包一層(`OpenNoteFileCommand` / `ShowNoteInFolderCommand`)。
    **預設值問不到就實際 `new` 一個出來讀** —— `tools/ApiDump` 只印簽章,印不出欄位初始值。
    另外**這是少數「發提示看得見」的地方**:失敗的定義就是沒有外部視窗跳出來,面板還在
    前景,所以 `ToastStatusMessage` 的 InfoBadge 讀得到(成功那條路相反,發什麼都是白費)。
    考證見[設計考證〈跳出去之後回得到哪一頁〉](docs/design-notes.md#open-external-return)。

## 慣例

- **介面字串不准寫在程式碼裡**,一律放 `src/Inkling/Properties/` 的三份 `.resx`
  (`Resources.resx` 英文=中性 / `Resources.zh-Hant.resx` / `Resources.zh-Hans.resx`),
  用產生出來的 `Resources.<鍵>` 取。**三份一起改**,註解只寫在中性那一份;
  `ResourceParityTests` 會擋住只改一份、佔位符對不上、值是空的、英文那份混進中文。
  key 不含底線(那是 C# 屬性名)。要帶值的用 `Strings.Format`,不要自己 `string.Format`。
  進 Adaptive Cards 的字串一律經過 `CardText.Json` 跳脫 —— 翻譯裡一個雙引號就能讓
  整張卡片變成不合法的 JSON。
  語言跟著 `CultureInfo.CurrentUICulture`(= Windows 顯示語言)走,**沒有設定項**,
  理由見 [設計考證〈介面語言跟著 Windows 走〉](docs/design-notes.md#ui-language)。**Core 不碰資源檔**:那一層連例外訊息都是英文,
  因為它會被 UI 包進「刪除失敗：{0}」裡,而同一個位置平常裝的是 .NET 自己的英文訊息。
- **文檔、程式碼註釋一律繁體中文**;識別符、字串常量、log、commit message 用英文。
- 註釋寫「為什麼」,特別是繞過 CmdPal 限制的地方 —— 這個 repo 的註釋密度刻意偏高,
  因為那些取捨從程式碼本身看不出來,半年後會被當成多餘而刪掉。
- `TreatWarningsAsErrors` + `AnalysisMode=Recommended` 全域開啟。測試專案只關掉
  CA1707 / CA1861。
- 擴展的執行期依賴釘在官方模板驗證過的版本(見 `Directory.Packages.props` 的說明),
  不要順手升級。
- 資料格式是承諾:`id` 才是身分(改標題不重新命名檔案)、不認得的 front matter 欄位
  原樣保留、沒有 front matter 的外來 `.md` 也要能列出來。
- 改了指令、設定項、資料格式或對外行為,同一輪更新**兩份 README**、
  `docs/manual-test-checklist.md` **與 `CHANGELOG.md`**(使用者感覺得到的變更一定要進
  `[Unreleased]` —— 這一條以前漏在這份規則外,`CONTRIBUTING.md` 列了、這裡沒有,
  結果是好幾個 commit 的行為改動沒有記錄)。**README 有兩個語言版本**:`README.md` 英文是預設、
  `README.zh-Hant.md` 繁中,是同一份文檔的兩個版本 —— 章節、表格的列、截圖都要對得上,
  改一份就改另一份(英文 pitch 以 `README.md` 為準,Store listing 與 gallery 從那裡拿)。
  **文檔分三份,別放錯**:README 是使用者文檔(怎麼用、有哪些鍵、筆記檔長什麼樣),
  `docs/development.md` 是建置/部署/專案結構/排錯,`docs/design-notes.md` 是
  「為什麼是這樣」的考證 —— README 每一節只留結論加連結(快速鍵那一節曾經漂回考證口吻,
  壓回去過一次)。改了 build、部署腳本或圖示流程,更新的是 `docs/development.md`。
  `CONTRIBUTING.md`(英文)是對外的薄入口,只指路、不重複規則 —— 規則在這份與
  `docs/development.md`,README 的文檔表對外列的是它而不是這份。

- **圖示的原始檔是 `assets/icon/*.svg`**,`src/Inkling/Assets/*.png` 是
  `tools/render-icons.ps1` 產生的,不要手改 PNG。同一支腳本還產 `assets/gallery/icon.png`
  (gallery 投稿用,**兩份 README 頂部引用的就是它**)與 `assets/social-preview.png`
  (GitHub repo 的 social preview,沒有上傳 API,要手動到 Settings 貼)。那些 PNG 必須帶
  `CopyToOutputDirectory`(見 `Inkling.csproj` 的註解)—— 少了它套件照樣註冊得起來,
  只是所有圖示變成 Windows 的預設灰方塊,而且 `IconHelpers.FromRelativePath` 讀不到檔。

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
比使用者裝的 0.11.11762.0 新。從原始碼得到的結論要跟安裝版對照 —— 實用手法是 byte-scan
`Microsoft.CmdPal.UI.exe`,確認那條程式路徑在安裝版裡到底存不存在:

```powershell
$d = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe"
Get-ChildItem $d -Recurse -Include *.dll,*.exe | Where-Object {
  $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
  [System.Text.Encoding]::UTF8.GetString($bytes).Contains('要找的東西') -or
  [System.Text.Encoding]::Unicode.GetString($bytes).Contains('要找的東西')
} | Select-Object -ExpandProperty Name
```

**UTF-8 與 UTF-16 都要掃,只掃一種會得到假的「不存在」。** .NET 的 metadata 分兩個 heap:
**識別名**(型別名、方法名)在 `#Strings`,是 **UTF-8**;**C# 字串常量**在 `#US`,
是 **UTF-16**。同一個 `Microsoft.CmdPal.UI.exe` 的實測對照:

| 掃的東西 | UTF-8 | UTF-16 | 它是什麼 |
|---|---|---|---|
| `get_IsCritical` | 命中 | 沒有 | 方法名(#Strings) |
| `windows-commandpalette-extension` | 沒有 | 命中 | 字串常量(#US) |
| `x-cmdpal://reload` | 沒有 | 命中 | 字串常量(#US) |
| `Reload Command Palette extensions` | 命中 | 沒有 | .resw 資源字串(編進 exe 的資源段) |
| `set_DefaultButton` | 沒有 | 沒有 | 真的不存在 |

只掃 UTF-8 的話,gallery 的 tag(`windows-commandpalette-extension`)、reload 的 URI、
gallery feed 的名字(`CmdPal-ExtensionsJson`)全都會被誤判成「安裝版沒有」——
**兩種編碼都掃不到,才算沒有。** 下面〈已知落差〉的每一條都用兩種編碼重掃過,結論不變。

(別用 `Select-String -Encoding Byte`,PowerShell 7 已經移除那個參數,整條會靜靜地失敗。)

**找 XAML 的東西要再多掃一種檔案。** 樣板名、資源鍵、Style 的 `x:Key` 不在
`.exe` 裡,而是編進 `resources.pri`,而且是 **UTF-16**。拿上面的 exe/dll 掃法去找
`CriticalContextMenuViewModelTemplate` 會得到「找不到」——但那是掃法不對,不是真的沒有:

```powershell
$bytes = [System.IO.File]::ReadAllBytes("$d\resources.pri")
[System.Text.Encoding]::Unicode.GetString($bytes).Contains('要找的資源鍵')
```

**已知落差**(都是 `main` 有、安裝版**沒有**,而且都曾經被當成事實寫進文檔):

- `MainListRanker` / `ClassifyTier` / `FallbackFloor` —— README 曾經照 `main` 寫過一段
  fallback 排序的說明,對安裝版來說整段是錯的。
- `ContentFormControl` 自動聚焦的 `OnlyControlOnPage` 判斷 —— 同一條路上的
  `ContentFormControl` / `OnFrameworkElementLoaded` / `FindFirstFocusableElement` 都掃得到,
  只有這個判斷沒有(`OnlyControl` / `SoleControl` / `SingleControl` 各種變體也都沒有)。
  設定頁因此曾經多掛一塊空的 `MarkdownContent` 去「湊滿兩塊內容」,而那招在安裝版上
  八成從來沒生效過,現在已經移除,見 [設計考證〈表單後面那塊空白已經拿掉了(而且它八成從來沒生效過)〉](docs/design-notes.md#blank-markdown-removed)。
- 確認框的 `ContentDialog.DefaultButton` —— `main` 在 `IsPrimaryCommandCritical` 時把它設成
  `Close`,安裝版整個套件掃不到 `set_DefaultButton`(同一段的 `set_PrimaryButtonText` /
  `set_CloseButtonText` / `set_XamlRoot` 都掃得到,所以不是掃描失準)。也就是說**那個旗標
  在使用者手上完全沒有效果**,批次刪除跟單則刪除的確認框長得一模一樣,Enter 都是確認。
  README 曾經照 `main` 寫成「設了它預設按鈕就變取消」,手動驗證清單還照那個寫了一條測試項。
  順帶一提**按鈕的顏色擴展碰不到**:`ConfirmationArgs` 只有四個屬性,而 CmdPal 那段把主要
  按鈕標紅的樣式是註解掉的 TODO。見 [設計考證〈確認框的按鈕沒有顏色,也沒有「危險」樣式〉](docs/design-notes.md#confirm-dialog-colors)。
- `ListItemsView` 的 sticky selection —— `main` 在清單更新後會盡量把選中項留在原處
  (`_stickySelectedItem`),留不住才退回 `GetFirstSelectableIndex()` 選第一個可選項;
  安裝版 `_stickySelectedItem` / `firstUsefulIndex` / `ensureSelectionVisible` **一個都掃不到**。
  也就是說**刪掉當前那一列之後焦點落在哪,在使用者手上沒有保證**,舊版大概率就是跳第一列。
  刪除頁「刪除全部」排第一就是踩在這上面 —— 順手按 Enter 有機會落到它身上,靠確認框擋;
  而 `Ctrl+Enter` 那條連續刪的路踩不到(那一列沒有次要命令)。
  見 [設計考證〈「刪除全部」排第一的代價〉](docs/design-notes.md#delete-all-first)。

這就是為什麼每個從原始碼得到的結論都要 byte-scan 對照一次再寫進文檔。

**反過來也有「掃得到」的**:`IPlainTextContent`(整頁的純文字內容,預覽頁的原始文字模式
在用)安裝版是有的 —— `ContentPlainTextViewModel` / `PlainTextContentViewer` /
`get_WrapWords` / `PlainTextTemplate` 在 `Microsoft.CmdPal.UI.exe`(UTF-8),
`PlainTextContentTemplate` 與那個檢視器的右鍵選單字串在 `resources.pri`(UTF-16)。
`ListItem.Tags` 改了畫面會即時更新這條路,安裝版也是有的
(`UpdateTags` / `VisibleTags` / `HasTags` / `TagViewModel` 都掃得到)—— 刪除頁的多選
曾經靠它做出來過(後來整個移除,見 [設計考證〈為什麼沒有多選〉](docs/design-notes.md#no-multiselect)),而**現在真的在用它**:
複製內文之後那一列右邊的「已複製」標籤就是這條路(`NoteListPage.FlashTag`)——
需要「不關面板、不重整清單、就地改一列的狀態」時就用它,別再想 toast。
byte-scan 不是只拿來否定,拿來確認一樣有用。

`CommandContextItem.IsCritical`(把 `Ctrl+K` 選單那一列變紅,IDL 的註解就寫著
「make this red」)也是掃得到的:`ContextItemTemplateSelector` / `get_IsCritical` 在
`Microsoft.CmdPal.UI.exe`,`CriticalContextMenuViewModelTemplate` /
`ContextItemTitleTextBlockCriticalStyle` 在 `resources.pri`(UTF-16)。
**這是擴展碰得到的唯一一處紅色** —— 底部工具列的按鈕寫死 `SubtleButtonStyle`,
確認框的按鈕連屬性都沒有(那個 `IsPrimaryCommandCritical` 是另一回事,而且沒作用),
見 [設計考證〈刪除的紅色只有一個地方碰得到〉](docs/design-notes.md#critical-red)。

兩份設定檔:

| | 位置 |
|---|---|
| Inkling 自己的設定 | `%LOCALAPPDATA%\Packages\<PFN>\LocalState\settings.json`(`<PFN>` 見上) |
| CmdPal 端(啟用、alias、快速鍵、釘選、fallback 規則) | `%LOCALAPPDATA%\Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json` |
