# Notelet

PowerToys Command Palette 的筆記擴展,用來在幾秒內記下隨時冒出的想法。

叫出 Command Palette → 打 `n 買咖啡機的想法` → Enter。存檔完成,全程不離開鍵盤,
也不用進任何頁面。

> **裝好之後要手動打開一個開關**,那句「打字→Enter」才成立:CmdPal 設定 →
> Extensions → Notelet → Fallback commands → 記下想法 → 勾 **Include in the Global
> result**。CmdPal 對第三方擴展的 fallback 一律預設不勾,見下面〈快速新增為什麼是 fallback〉。

筆記是資料夾裡的純 Markdown 檔(YAML front matter + 內文),任何編輯器都能直接開。
多端同步交給雲端硬碟處理,Notelet 本身沒有任何同步程式碼。

## 功能

| | |
|---|---|
| 快速新增 | 在主搜尋框打 `n <想法>` 直接存檔;想連內文一起記就 `n <標題>;<內文>` |
| 新增(完整) | `Notelet:新增筆記` 開表單,可寫多行內文 |
| 瀏覽與搜索 | 標題與內文都能搜,多個關鍵字是 AND,標題命中排前面 |
| Markdown 預覽 | 選中筆記按 Enter 看渲染結果 |
| 原始文字 | 清單頁按 `Ctrl+U`,詳細窗格在渲染與原始 Markdown 之間切換 |
| 面板寬度 | 清單頁按 `Ctrl+D`,詳細窗格在窄 / 中 / 寬三檔之間循環 |
| 編輯 | 表單式編輯(`Ctrl+E`),Tab 到「儲存」按 Enter;或用「在預設編輯器開啟」跳出去改 |

刪除/封存、tag 分類、置頂還沒做。檔案格式已經預留 `tags` 欄位。

## 需求

| | |
|---|---|
| Command Palette | 0.11 以上(獨立 MSIX 套件 `Microsoft.CommandPalette`) |
| .NET SDK | 10.0 以上 |
| Windows | 10.0.19041 以上 |
| Developer Mode | 必須開啟。設定 → 系統 → 開發人員專用 |

不需要 Visual Studio,整套流程走 dotnet CLI。

## Build 與本機安裝

```powershell
git clone <repo> Notelet
cd Notelet
.\tools\deploy.ps1 -Configuration Release
```

然後在 Command Palette 執行 **Reload**(要選副標題是 `Reload Command Palette extensions`
的那一個),CmdPal 才會重新載入擴展。

`deploy.ps1` 會依序做:build/publish → 以 loose file 註冊套件 → 查 Windows 的
AppExtension 目錄確認 CmdPal 真的看得到它。最後一步是自動的,不必靠肉眼開 CmdPal 確認。

### Debug 與 Release 的差別

trimming 只在 `dotnet publish` 時生效,而 `Add-AppxPackage -Register` 註冊的是
build 佈局 —— 兩者不是同一份輸出。所以腳本對兩種組態走不同的路:

| | 做法 | 大小 | 用途 |
|---|---|---|---|
| `Debug` | 直接註冊 build 佈局 | ~106 MB | 開發。`Debug.WriteLine` 有作用,建置快 |
| `Release` | 先 publish(trimming 生效),再併入 build 產生的 `AppxManifest.xml` | ~30 MB | 日常使用 |

只用 `dotnet publish -c Release` 而不做後面那步的話,你註冊到的仍然是未 trim 的
build 佈局,等於完全沒驗到 trimming 有沒有把東西砍壞。

### 注意:loose file 註冊會綁住路徑

`Add-AppxPackage -Register` 不會複製檔案,Windows 直接引用 `src\Notelet\bin\` 底下
那個佈局。所以**不要在部署後刪掉 `bin\`**(`git clean -xfd` 也會刪),否則擴展會壞掉。
真的刪了就重跑一次 `deploy.ps1`。

### 移除

```powershell
Get-AppxPackage -Name Notelet | Remove-AppxPackage
```

## 同步設定

Notelet **不做同步**。它只是把 Markdown 檔寫進你指定的資料夾,同步 100% 交給雲端硬碟
客戶端。這是刻意的:同步程式碼是零,而離線可用性、衝突處理、手機端存取全部沿用
OneDrive / Dropbox 既有的能力。

預設資料夾是 `%OneDrive%\Notelet`(找不到 OneDrive 就退回 `文件\Notelet`)。
要改路徑:Command Palette → Notelet → `Ctrl+K` → 設定。

要在手機上看筆記,裝 OneDrive App 就行;想要好一點的 Markdown 閱讀體驗,可以再接
Obsidian 之類的工具指向同一個資料夾。

### OneDrive 使用者請注意

把 Notelet 資料夾設成「一律保留在此裝置上」(資料夾按右鍵)。開啟「檔案隨選」而檔案
只有雲端佔位符時,Notelet 讀取會觸發下載,搜索就會卡住。

多台機器同時編輯同一則筆記時,OneDrive 會產生 `檔名-電腦名.md` 這種副本。資料不會遺失,
那個副本會照樣出現在清單裡,自己決定要留哪一份。

## 資料格式

```markdown
---
id: 20260810-143052-a7f3
title: 買咖啡機的想法
created: 2026-08-10T14:30:52+08:00
updated: 2026-08-11T09:15:00+08:00
tags: []
---

先查一下手沖跟義式的差別。
```

幾個刻意的決定:

- **`id` 才是身分,檔名只是給人看的。** 改標題不會重新命名檔案 —— 在雲端同步資料夾裡
  頻繁 rename 是產生重複檔與衝突檔的頭號原因。
- **不認得的 front matter 欄位會原樣保留。** 你用 Obsidian 之類的工具加的 `aliases`、
  `cssclass`,經過 Notelet 編輯一輪之後不會被吃掉。
- **沒有 front matter 的 `.md` 照樣會出現在清單裡。** 標題取內文第一個標題,時間取檔案
  時間戳。你可以直接把既有的筆記資料夾指給 Notelet。
- 會掃子資料夾,新筆記一律寫在根目錄。

### 預覽的換行處理

標準 Markdown 裡單一換行等於空格,所以打三行會顯示成一行。對一個隨手記想法的工具來說
那不是使用者要的,所以**預覽時**會把單一換行當成真的換行。

只動拿去渲染的那份字串,**磁碟上的 `.md` 一個字都不變** —— 用別的編輯器打開仍然是標準
Markdown。程式碼區塊、表格、縮排程式碼、setext 標題底線這些「換行本來就有意義」的地方
會避開。規則在 `Notelet.Core/NotePreview.cs`,測試在 `NotePreviewTests.cs`。

已知的取捨:貼進來的 Markdown 文件如果有「中途硬換行的段落」,渲染時會固定斷在原本的
折行處,而不是隨視窗寬度重排。

曾經考慮過依內容自動判斷「這則是 Markdown 文件還是隨手記」,只對後者保留換行,結論是
**不做**。判斷的單位只能是整則筆記,而誤判的代價落在最常見的情況上 —— 底下這種一個標題
加幾行隨手記,會因為偵測到 `#` 就被判成 Markdown 文件,兩行 prose 被併成一行:

```markdown
# 想法
今天很累
明天再說
```

而且使用者無從預測:同樣打三行,加了個 `#` 之後渲染就變了,還看不出為什麼。
Obsidian、Bear、Apple Notes、Google Keep 這些做筆記的一律預設保留換行,沒有一個去猜意圖;
Obsidian 把嚴格 CommonMark 做成 Strict line breaks 設定,預設也是關閉。要處理那個 case
的話,顯式開關(front matter 欄位或全域設定)比啟發式判斷可靠 —— 目前沒有需求,先不加。

### 原始文字模式(`Ctrl+U`)

清單頁按 `Ctrl+U`,右邊的詳細窗格在「渲染結果」與「原始 Markdown」之間切換,**不必進預覽頁**。
用途是直接在清單上選取、複製帶符號的原文:標題的 `#`、粗體的 `**`、連結的 `[](…)`
渲染完就消失了,但要複製走的往往正是這些符號。

實作上的兩個重點:

- **切換不會重建清單。** 只換掉每個項目的 `ListItem.Details`,CmdPal 收到屬性變更後
  只重畫右邊那一塊。若改用 `RaiseItemsChanged`,CmdPal 是拿 `IListItem` 的**物件識別**
  當鍵在快取 viewmodel 的,想讓它重讀詳細內容就得換掉整批項目物件,整份清單翻新一次,
  選中項就有機會跑掉 —— 而按下這個鍵的當下正在看某一則筆記,跳走就沒有意義了。
- **原文靠逐字逃脫顯示,不用程式碼區塊。** 反斜線只存在於送給渲染器的字串裡,
  畫面上顯示的、以及選取複製走的,都是還原後的原文,複製保真度不受影響。
  用程式碼區塊雖然連空白都能一字不差,但 CmdPal 會替它畫上外框與底色,
  樣式寫在 CmdPal 自己的資源裡、擴展改不動,在窄窄的詳細窗格裡太搶版面。

切換狀態會記住,搜索、切換筆記都不會跑掉,直到擴展重載或改設定為止。

已知的取捨:**行首縮排與連續空行會被渲染器正規化**。段落開頭的四個空白在 CommonMark 裡
就是縮排程式碼區塊,留著等於把外框畫回來,所以一律去掉。實測本專案自己的開發筆記
(43 行),只有 4 行受影響,全是巢狀清單的續行縮排,其餘一字不差。

#### 為什麼不是就地改 `Details.Body`

那樣更省,而且 `Details.Body` 的 setter 確實會發出屬性變更通知 —— 但**跨進程時那條路是斷的**,
實測結果是值改到了、畫面不動,要重新進入清單頁才看得到。

原因在 SDK 的 `IDetails` 沒有宣告成可觀察介面。CmdPal 的 `DetailsViewModel` 因此是全專案唯一
用執行期型別測試(`model is INotifyPropChanged`)決定要不要訂閱的,而那個 QI 過不了
out-of-process 邊界;`BaseObservable.OnPropertyChanged` 又把例外整個吞掉,失敗完全無聲。

`ICommandItem` 相反 —— 它在 IDL 裡就繼承 `INotifyPropChanged`,`CommandItemViewModel`
對它是無條件訂閱。所以要通知 CmdPal「這一項變了」,走 `ListItem` 的屬性一定收得到,
走 `Details` 的屬性則不一定。

### 詳細面板寬度(`Ctrl+D`)

清單頁按 `Ctrl+D`,詳細窗格在**窄 → 中 → 寬**三檔之間循環,對應清單與詳情的比例
3:1 → 2:1 → 1:1。看原始文字時特別有感,窄窗格會把幾乎每一行都折斷。

**只有三檔,而且拖不動。** 寬度來自 `IDetails.Size`,CmdPal 只認 `Small / Medium / Large`
(`DetailsSizeToGridLengthConverter`);自由拖曳這件事它自己也沒做 —— 整個介面裡連一個
`GridSplitter` 都沒有。

`Size` 比 `Body` 更沒得商量:它根本不走屬性變更通知,而是 `DetailsViewModel.InitializeProperties`
經由 `IExtendedAttributesProvider.GetProperties()` 讀一次就定了。所以切換寬度跟切換原始文字
走同一條路 —— 換上新的 `Details` 物件,別無他法。

選好的檔位會存回擴展設定,重開之後照舊。存的時候刻意只寫檔、不發 `SettingsChanged`:
那個事件會讓整個 provider 重建(換掉 repository 與清單頁),而按 `Ctrl+D` 的當下人正看著
某一則筆記,清單被翻新一次選中項就跑掉了。設定頁裡也有同一個選項,兩邊改的是同一個值。

### 編輯表單

表單是 Adaptive Cards,能調的東西比想像中少。下面三件事都是繞出來的:

- **游標落在哪一格,由欄位順序決定。** CmdPal 進表單頁後會聚焦卡片裡第一個可聚焦的控件
  (`ContentFormControl.FindFirstFocusableElement`),而 Adaptive Cards 既沒有 autofocus
  也沒有 tabIndex。所以**編輯**時內文排在標題前面 —— 進來就是要改內容,標題頁首已經寫著了;
  **新增**時維持標題在前,因為是先想標題。
- **新增時內文框預填 5 行空白。** 渲染器對多行輸入只設 `AcceptsReturn` 與 `TextWrapping`,
  完全不碰高度,所以空的內文框就是一行高,看起來像只能寫一行。卡片沒有「幾行高」這種屬性,
  唯一撐得開它的就是內容本身。代價是 placeholder 不再顯示(框裡有東西了),而空行有機會
  被存進檔案,所以新增的存檔路徑會 `Trim()`;編輯時不動,那些空行是使用者自己的排版。
  (`Container` 的 `minHeight` 配上 `height: stretch` 試過,不成立:輸入框連同它的標籤會被
  包進一個 `StackPanel`,多出來的空間留在容器裡,框還是一行。)
- **沒有 `Ctrl+S`,存檔是 Tab 到「儲存」按 Enter。** 表單的輸入值只活在 CmdPal 進程裡的
  `RenderedAdaptiveCard.UserInputs`,擴展唯一的取值管道是 CmdPal 反過來呼叫
  `SubmitForm(inputs)` —— 就算把 `Ctrl+S` 綁到擴展的命令上,手上也沒有使用者剛打的字。
  CmdPal 端唯一的鍵盤提交路徑是 `ContentFormControl.OnFormKeyDown`,只認 Enter、只在單行
  輸入框裡有效,而且 0.11.11762.0 還沒有這段程式碼。真要 `Ctrl+S` 得改 PowerToys 本身。

### 快速新增為什麼是 fallback

**只有 fallback 拿得到使用者正在打的字**(`IFallbackHandler.UpdateQuery`)。頂層命令被叫起來時
搜尋框已經清空了 —— 換句話說「打字→Enter 就存檔」這件事非 fallback 不可,做成命令就只剩
「開一個頁面再打字」,那已經是 `Notelet:新增筆記` 在做的事。

**前綴則是因為筆記沒有形狀。** 內建的 fallback 不需要觸發詞:計算機看查詢算不算得出來、
Run 看是不是一個可執行檔、開網址看像不像 URL,不像就自己藏起來。筆記沒有這種判準 ——
任何一句話都是合法的筆記。所以改用前綴當意圖判斷:有前綴才現身,沒有就把 `Title` 設成
空字串隱藏(空標題的項目會被 `MainListPage.GetSearchViewItems` 濾掉),不去污染每一次搜索。

CmdPal 端有三個開關會影響它,都在 設定 → Extensions → Notelet:

| 開關 | 要怎麼設 | 為什麼 |
|---|---|---|
| Include in the Global result | **要勾** | 不勾的話它只會出現在結果最底下那個 fallback 區塊,而不是跟一般結果一起排。CmdPal 對第三方擴展一律預設不勾(`ProviderSettings.WithConnection` 裡 `wrapper.Extension is null` 才給 true) |
| Manage fallback order | 隨意 | 這個順序只決定底部 fallback 區塊的排列。勾了 global 之後就走一般計分,跟這個順序無關 |
| Alias | **別設成 `n`** | alias 比 fallback 早一步處理:`MainListPage.UpdateSearchTextCore` 開頭就 `if (aliases.CheckAlias(newSearch)) return;`。indirect alias 存的鍵是「alias + 空白」,所以 alias `n` 會在你打完 `n ` 的那一刻把搜尋框清掉,快速新增再也看不到那句查詢 |

排到多前面就不是我們能控制的了:CmdPal 的 `MainListRanker.ClassifyTier` 把**所有** fallback
一律歸在最低的 `FallbackFloor` 層,任何一個字面命中的命令或應用程式都排在前面。實務上
`n 買咖啡機` 這種查詢不會命中別的東西,所以它就是第一個。

### 標題與內文用分號分隔

`n 買咖啡機;比較過 Breville 跟 Sage` → 標題是「買咖啡機」,分號之後的都進內文。
只切第一個分號(內文本來就可能有分號),全形 `；` 一樣算 —— 中文輸入法打出來的就是它。
代價是**標題裡不能有分號**,需要的話請走完整表單。只打了分號還沒打內文不影響,存的就是標題。

### 命令 Id 為什麼要寫死

`src/Notelet/CommandIds.cs` 裡那幾個字串是對外承諾,跟資料格式一樣不能改。

CmdPal 把使用者對命令做的設定 —— alias、全域快速鍵、釘選、fallback 的顯示規則與排序 ——
全部存在自己的 settings.json 裡,鍵就是命令的 `Id`。而**命令沒有設 `Id` 時 CmdPal 會現場算一個**:
`TopLevelViewModel.GenerateId` 拿 `ProviderId + DisplayTitle + Title + Subtitle` 去做 WyHash64。
也就是說標題變一個字,那個命令對 CmdPal 來說就變成了另一個命令,使用者設過的東西全部對不上。

對 fallback 更致命,它的標題本來就跟著使用者打的字一直變。這是實際踩到的:CmdPal 的
settings.json 裡留下了兩個 Notelet fallback 條目,把其中一個的雜湊反推回去,正好是標題
`記下:你好` —— 某次重新載入時搜尋框裡剛好是那句話。表現出來就是「改了一次設定,
快速新增就莫名其妙不會出現了,連改回原本的前綴也救不回來」。

## 設定項

| 設定 | 預設 | 說明 |
|---|---|---|
| 筆記資料夾 | `%OneDrive%\Notelet` | 存放 Markdown 檔的位置 |
| 啟用快速新增 | 開 | 關掉就不會在主搜尋框出現 |
| 快速新增前綴 | `n ` | 以字母或數字結尾時會自動補一個空白 |
| 詳細面板寬度 | 窄 | 清單頁按 `Ctrl+D` 也能循環,兩邊改的是同一個值 |

前綴為什麼要補空白:設成 `n` 而不補的話,`note about x` 這種普通查詢會被當成快速新增,
而且第一個字母會被吃掉變成記下 `ote about x`。符號前綴(例如 `,`)則不需要空白。

設定存在 `%LOCALAPPDATA%\Packages\Notelet_<套件雜湊>\LocalState\settings.json`
(`Utilities.BaseSettingsPath` 會走 MSIX 的路徑重導向),跟其他擴展的做法一致 —— 一人一份,
互不干涉。CmdPal 自己那份(啟用與否、alias、快速鍵、釘選、fallback 規則)則存在 CmdPal
的套件底下,由 CmdPal 管理,擴展碰不到,所以那些設定不會跟著擴展重新部署被清掉 ——
只有命令的 `Id` 變了才會對不上。

## 專案結構

```
src/
  Notelet.Core/      純 net10.0 類別庫,不引用任何 CmdPal 型別 → 100% 可單元測試
    Note              筆記模型
    NoteFile          YAML front matter 讀寫(手寫,不用 YamlDotNet)
    NoteFileName      id 產生與檔名 slug
    FileSystemNoteRepository  讀寫、快取、FileSystemWatcher 失效
    NoteSearch        過濾與排序(純函式)
    QuickCapture      快速新增的觸發判斷與標題/內文切分
    NoteletOptions    執行期設定
  Notelet/           CmdPal 擴展(MSIX COM server)
    NoteletExtension / NoteletCommandsProvider / SettingsManager
    CommandIds        頂層命令的固定 Id(改了會清掉使用者的 alias/快速鍵/釘選)
    QuickCaptureFallbackItem  主搜尋框的快速新增
    Pages/            清單、預覽、編輯、新增
tests/
  Notelet.Core.Tests/  xUnit
tools/
  deploy.ps1           build → 註冊 → 驗證
  VerifyRegistration/  查 AppExtension 目錄的探針
  ApiDump/             印出 CmdPal Toolkit 型別的實際簽章
```

分層的重點:`Notelet.Core` 不知道 Command Palette 的存在。所有容易寫錯的邏輯
(front matter 解析、檔名、搜索排序、前綴判斷)都在那一層,因此都有單元測試涵蓋。
`src/Notelet` 只負責把 Core 的結果翻譯成 `IListItem` / `IContent`。

## 開發

```powershell
dotnet test                                        # Core 的全部行為
.\tools\deploy.ps1                                 # Debug 部署
.\tools\deploy.ps1 -Configuration Release          # trimmed 部署
.\tools\deploy.ps1 -Configuration Release -Reload  # 部署完自動重新載入
```

改完程式重新部署後,記得在 Command Palette 執行 **Reload**,或用 `-Reload` 讓腳本代勞
(需要先打開 CmdPal 設定 → 一般 → For developers → **Enable external reload**,
它走的是 `x-cmdpal://reload` 這個 protocol)。

### 為什麼 Reload 之後有時會冒出兩個 Notelet

CmdPal 那邊的問題,不是擴展的。重新註冊套件會讓 Windows 的套件目錄發出**安裝**事件
(套件版本從頭到尾都是 0.1.0.0,所以它算是重裝而不是升級 —— 升級走的是「先移除再安裝」,
反而不會出事)。CmdPal 收到安裝事件後會替同一個擴展再建一個 `CommandProviderWrapper`,
而 `TopLevelCommandManager.RegisterAndLoadCommandsAsync` 是直接 `AddRange`,不去重。

手動 Reload 如果搶在那個非同步事件之前跑完,被清掉的是舊清單,事件補進來的就成了第二個。
所以 `-Reload` 會先等幾秒再送重新載入。已經看到兩個的話,再 Reload 一次就好,
不必重開 PowerToys 或 CmdPal。

### 查 SDK 的實際簽章

Microsoft Learn 上的 Command Palette API 參考有些頁面是 2025 年初寫的,跟 0.11 的實際
簽章對不上(至少 `FallbackCommandItem` 的建構子與 `KeyChordHelpers.FromModifiers` 的
參數個數都不一樣)。與其靠編譯錯誤一次次試,直接問組件:

```powershell
dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult ListItem
dotnet run --project tools\ApiDump -- --paths     # 設定檔存在哪
```

### 排錯:讓擴展自己說話

擴展跑在獨立的 COM server 進程裡,沒有主控台;`Debug.WriteLine` 又掛著
`[Conditional("DEBUG")]`,Release 建置整個編掉,而日常安裝的正是 Release。
所以要確認某段程式有沒有被執行到,得看 `DiagnosticLog` 寫出來的檔。

預設關閉。開啟方式是在設定資料夾裡建一個空檔,然後 Reload:

```powershell
$ls = "$env:LOCALAPPDATA\Packages\Notelet_bf0n0751x5hse\LocalState"
New-Item -ItemType File "$ls\diagnostic.on"
Get-Content "$ls\diagnostic.log" -Encoding utf8 -Wait   # 邊操作邊看
```

(資料夾名稱裡的雜湊值由套件識別決定,`dotnet run --project tools\ApiDump -- --paths`
在未封裝情況下印的是另一個路徑,別搞混。)

沒有 `diagnostic.on` 時每次呼叫只是一個布林判斷。用完把 `.on` 檔刪掉即可。
`Ctrl+U` 那個功能就是靠它定位的:紀錄顯示值明明改到了,才確定問題出在通知而不是命令。

### 效能上的規矩

需求裡有一條「擴展不能拖慢 Command Palette」,對應到程式碼是三件事:

- `TopLevelCommands()` 絕不碰磁碟。CmdPal 一啟動就會呼叫它。
- `GetItems()` 每按一鍵就會被呼叫一次,所以筆記有記憶體快取,搜索是純字串比對不用 regex,
  同一個查詢字串不重建項目。
- 清單一次最多送 200 則(每個項目都要跨進程 COM 封送)。被截斷時清單最後會明講
  還有幾則,不會默默少東西。

`tests/Notelet.Core.Tests/PerformanceTests.cs` 是這幾條的防退化警戒線。

### 疑難排解

**改了程式但 CmdPal 沒反應** — 要跑 Reload,而且要選副標題是
`Reload Command Palette extensions` 的那一個。

**build 失敗說檔案被佔用** — CmdPal 把擴展的 COM server 留著沒關。`deploy.ps1` 會自動
先停掉它;直接跑 `dotnet build` 的話要自己 `Get-Process Notelet | Stop-Process -Force`。

**部署說成功,跑的卻還是舊版本** — 同一個 identity + version 已經註冊時,
`Add-AppxPackage -Register` 會**靜默地什麼都不做**,舊的 `InstallLocation` 原封不動。
在 Debug 與 Release 之間切換時特別容易中招。`deploy.ps1` 已經處理:位置不同就先
`Remove-AppxPackage -PreserveApplicationData` 再註冊,事後還會確認 `InstallLocation`
真的變了。手動註冊時要自己記得這件事。想確認目前跑的是哪一份:

```powershell
(Get-AppxPackage -Name Notelet).InstallLocation
```

**設定頁按 Save 什麼都沒發生** — 那個頁面是綁在**某一個擴展實例**上的。中間只要發生過
Reload 或重新部署,舊的擴展進程就被換掉了,設定頁手上的物件已經死了,按下去靜靜地什麼也不會做:
不寫檔、不重建、不報錯。**把設定頁關掉重開**(退回 Extensions 清單再點進來)就好。

查證方式是打開 `DiagnosticLog`(見上一節)再按一次 Save:

- 什麼都沒印 → 呼叫根本沒到擴展這邊,就是上面這件事
- 印出 `SettingsChanged: prefix=…` 跟 `SaveSettings(...): 已寫入 …` → 設定確實存下去了

擴展這一側的存檔失敗現在也會記進同一個檔。toolkit 的 `JsonSettingsManager.SaveSettings`
自己把例外吞掉,只往 CmdPal 的 log 丟一行字,所以 `SettingsManager.Save` 另外記了一筆完整的例外。

**擴展沒出現在 CmdPal 裡** — 跑 `dotnet run --project tools\VerifyRegistration`。
它會列出 Windows 認得的所有 CmdPal 擴展。Notelet 不在裡面就是註冊沒成功;
在裡面卻不出現,那是 CmdPal 端的問題,先試 Reload。

**`APPX1707` 警告** — 官方擴展模板也會出現,無害。
