# Notelet

PowerToys Command Palette 的筆記擴展,用來在幾秒內記下隨時冒出的想法。

叫出 Command Palette → 打 `n 買咖啡機的想法` → Enter。存檔完成,全程不離開鍵盤。

> **裝好之後要自己設一個 alias**,那句「打字→Enter」才成立:CmdPal 設定 →
> Extensions → Notelet → `Notelet:快速記下` → Alias 填 `n`。之後打 `n` 空白就直接進
> 快速記下頁,接著打字、Enter 存檔 —— 按鍵數跟直接在主搜尋框打完全一樣。
> 想更快就再給它一個全域快速鍵,連 `n` 都省了。

筆記是資料夾裡的純 Markdown 檔(YAML front matter + 內文),任何編輯器都能直接開。
多端同步交給雲端硬碟處理,Notelet 本身沒有任何同步程式碼。

## 功能

| | |
|---|---|
| 快速記下 | `Notelet:快速記下` 打字直接存檔;想連內文一起記就 `<標題>;;<內文>`(分隔符可以在設定裡換掉)。底下會列出標題相近的既有筆記,免得同一件事記兩遍 |
| 記下後先看一眼 | 存好之後停在筆記上,確認沒記錯再按一次 Enter 收起。`Ctrl+Enter` 是另一條路,設定可以對調哪一條掛在 Enter 上 |
| 新增(完整) | `Notelet:新增筆記` 開表單,可寫多行內文 |
| 瀏覽與搜索 | 標題與內文都能搜,多個關鍵字是 AND,標題命中排前面 |
| Markdown 預覽 | 選中筆記按 Enter 看渲染結果 |
| 原始文字 | 清單頁按 `Ctrl+U`,詳細窗格在渲染與原始 Markdown 之間切換 |
| 面板寬度 | 清單頁按 `Ctrl+D`,詳細窗格在窄 / 中 / 寬三檔之間循環 |
| 編輯 | 表單式編輯(`Ctrl+E`),Tab 到「儲存」按 Enter;或用「在預設編輯器開啟」跳出去改 |
| 刪除 | 清單頁按 `Ctrl+Del`,確認後**移到資源回收筒**(不是永久刪除) |
| 清空 | `Notelet:刪除所有筆記` 先開一頁**列出會刪掉哪些檔案**,不是 Notelet 建立的排在最前面,確認後才動手 |

封存、tag 分類、置頂還沒做。檔案格式已經預留 `tags` 欄位。

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
- **但它們身上有記號。** front matter 裡沒有 `id` 的檔案,`Note.IsExternal` 是 true ——
  身分是我們從路徑推出來的,不是 Notelet 寫的。日常瀏覽時兩者一視同仁,
  只有批次刪除會分開處理(見〈刪除全部為什麼是一頁〉)。
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

表單是 Adaptive Cards,能調的東西比想像中少。下面四件事都是繞出來的:

- **游標落在哪一格,由欄位順序決定。** CmdPal 進表單頁後會聚焦卡片裡第一個可聚焦的控件
  (`ContentFormControl.FindFirstFocusableElement`),而 Adaptive Cards 既沒有 autofocus
  也沒有 tabIndex。所以**編輯**時內文排在標題前面 —— 進來就是要改內容,標題頁首已經寫著了;
  **新增**時維持標題在前,因為是先想標題。
- **落在那一格的哪個位置,則完全指定不了 —— 一律是開頭。** 這條查過了,不要再試:
  CmdPal 只做 `focusableElement?.Focus(FocusState.Programmatic)`
  (`ContentFormControl.OnFrameworkElementLoaded`),而 Adaptive Cards 的 `Input.Text`
  沒有任何 caret / selection 屬性。擴展手上只有 `TemplateJson` 與 `DataJson`,
  碰不到底下那個 WinUI `TextBox`,而它被程式化聚焦時游標固定在索引 0。
  想要「一進來就在內文最後」只有兩條路:改 PowerToys 本身,或在表單上另外加一個空的
  「追加」框(空框的開頭就等於結尾,存檔時接到內文尾端)。後者評估過 ——
  不值得為了偶爾的追記,讓每次編輯都多一塊多行輸入框。現在的做法是把 `Ctrl+End` 講出來:
  編輯表單底部有一行淡色提示,新增時不顯示(內文本來就是空的,沒有差別)。
  那行字是 `TextBlock` 不是 `Control`,`FindFirstFocusableElement` 不會選中它,
  所以擺進去不影響焦點還是落在內文框。
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

### 快速記下為什麼是頁面,不是 fallback

**這條路試過,而且做完了,最後整個移除。** 別再試一次 —— 下面是為什麼。

「在主搜尋框打字→Enter」這件事本身非 fallback 不可:**只有 fallback 拿得到使用者正在打的字**
(`IFallbackHandler.UpdateQuery`),頂層命令被叫起來時搜尋框已經清空了。

問題出在**沒命中的時候藏不起來**。內建的 fallback 不需要觸發詞:計算機看查詢算不算得出來、
Run 看是不是一個可執行檔、開網址看像不像 URL,不像就自己藏起來。筆記沒有這種判準 ——
任何一句話都是合法的筆記。所以只能用前綴當意圖判斷,沒命中就把 `Title` 設成空字串隱藏。

而那招要成立,得靠 CmdPal 端把空標題的項目濾掉,**0.11.11762.0 沒有確實做到**:

- 勾了 Include in the Global result 之後,fallback 走的是 `_scoredFallbackItems`
  (跟一般結果一起計分),不是底部那個 fallback 區塊。而**不勾就沒有意義** ——
  它會排在所有結果後面,Enter 不落在我們身上,「打字→Enter」直接不成立。
- `MainListPageResultFactory.Create` 只對底部區塊那條路過濾空標題,而且陣列大小是按
  **未過濾**的 count 算的、寫入時才過濾,尾端因此留 null。旁邊還躺著一個沒人呼叫的
  `GetNonEmptyFallbackItemsCount`,註釋寫 `Empty fallbacks are removed prior to this merge`
  —— 過濾被提前到呼叫端 `GetSearchViewItems` 了,而那是 `main` 分支才有的。
- 佐證版本落差:byte-scan 安裝版的 `Microsoft.CmdPal.UI.exe`,`GetSearchViewItems` 與
  `MainListPageResultFactory` 在,`MainListRanker` / `ClassifyTier` / `FallbackFloor` **不在**。

表現出來就是:**不管打什麼,結果裡永遠多一個點不動的空列**。這不是我們能修的。

**換成頁面之後按鍵數一模一樣。** `n` 空白 想法 Enter —— 唯一的差別是中間會跳一次頁。
換來的是主搜尋框完全乾淨、不再受 CmdPal 版本行為影響,以及一件 fallback 結構上做不到的事:
fallback 只有一列,頁面有一整個清單,所以「記下」底下能直接列出標題相近的既有筆記。

順帶一提,前綴設定跟著一起消失了:alias 就是前綴,而且由 CmdPal 統一管理。

alias 的機制要知道兩件事(`AliasManager.CheckAlias`):

| | |
|---|---|
| indirect alias 存的鍵是「alias + 空白」 | 所以填 `n`,實際觸發的是你打完 `n ` 的那一刻 |
| 觸發時送 `ClearSearchMessage` + `PerformCommandMessage` | 搜尋框被清空、跳進頁面。**所以 alias 觸發的命令拿不到觸發當下那句話** —— 但跳進去之後打的字,是我們自己 `DynamicListPage.UpdateSearchText` 收的,完全掌控。這正是頁面版能成立的原因 |

**哪天 CmdPal 修好了想把 fallback 加回來**:整套實作在 git 歷史裡,
`git log --diff-filter=D -- src/Notelet/QuickCaptureFallbackItem.cs` 找得到。
判準是打一句不帶前綴的話,結果裡不再多出空列。真要加回來記得:alias 比 fallback
早一步處理(`MainListPage.UpdateSearchTextCore` 開頭就 `if (aliases.CheckAlias(newSearch)) return;`),
所以 alias 別跟前綴設成同一個字,否則 alias 會先把搜尋框清掉,fallback 再也看不到那句查詢。

### 標題與內文用分隔符切開

`買咖啡機;;比較過 Breville 跟 Sage` → 標題是「買咖啡機」,`;;` 之後的都進內文。
分隔符是設定項,填什麼就用什麼(長度不限),空著就回到預設的 `;;`。

**為什麼預設是兩個分號。** `;` 在 home row 上、右手小指原位,不用按 Shift,連打兩下最快;
而**連續兩個**分號在自然語句裡幾乎不出現,所以標題可以自由使用單一個分號
(`for (var i = 0; i < 10; i++)`、中文句子的頓隔)。要求連打兩次,誤觸的成本就沒了 ——
一個人不會無意間打出兩個相連的分號。

唯一真的會撞到的是 C 系的無限迴圈寫法 `for (;;)`。常寫那種筆記的話,**建議換成 `,,`**:
鍵位一樣不用 Shift,碰撞比分號更少。

**半形全形算同一個字。** 中文輸入法打出來的是全形,而中英切換的當下打出哪一個並不受控。
折算是逐字元、長度不變的(全形 ASCII U+FF01–U+FF5E 與半形差值固定 `0xFEE0`),
所以 `；；`、`;；` 都切得開,**設定欄位那一頭也走同一個折算** —— 設定填 `;;`、打字打 `；；`
一樣成立。沒有半形對應的中文標點(`、` `。`)不在範圍內,填那些就只認它自己。

只切第一組,後面的分隔符是內文的一部分。只打了分隔符還沒打內文不影響,存的就是標題。

**換掉分隔符之後,舊的那一組就只是普通文字**,不留備援 —— 否則使用者永遠沒辦法把分號
寫進標題,換設定就失去意義。改了之後**當下開著的快速記下頁就會跟上**(提示文字、
切出來的標題與內文都是),不必 Reload:那一頁訂閱 `ICaptureSeparatorStore.CaptureSeparatorChanged`,
理由跟 `Ctrl+D` 那條路一樣,見〈詳細面板寬度〉。頂層那一列的副標刻意寫「分隔符」而不是
「分號」—— 命令陣列只在資料夾變了才重建,它跟不上。

### 記下之後要不要先看一眼

預設是記完就收:存檔 → toast「已記下:標題」→ Command Palette 消失。設定裡的
**「記下後先看一眼」**打開之後對調成:Enter 記下並停在那則筆記的完整 Markdown 上,
再按一次 Enter 才收起 Command Palette。

**兩條路永遠都在**,設定只決定哪一條掛在 Enter 上,另一條落到 `Ctrl+Enter`
(CmdPal 把 `MoreCommands` 的第一個項目當成次要命令)。所以改設定不會讓任何操作消失,
只是換手。預設維持「記完就收」——多按一個 Enter 是每次記下都要付的成本,而切出來的
標題與內文在按下 Enter **之前**清單上就看得到了。

實作上有三件事是被 CmdPal 逼出來的:

**1. 停留就不能發 toast。** toast 是另一個會搶焦點的視窗,而 CmdPal 主視窗一失焦就把自己
藏起來(`MainWindow_Activated` → `EndSession("LostFocus")`,沒有開關)。「記下之後
Command Palette 整個消失」其實是 toast 造成的,不是 `GoHome()` —— 後者的語意明明白白是
「回主頁但**保持開著**」。所以預覽這條路一個 toast 都不發,存檔失敗的訊息直接畫在頁面上。

**2. `CommandResult.GoToPage` 是空殼。** SDK 有那個型別,但 CmdPal 的
`ShellViewModel.UnsafeHandleCommandResult` 那個 switch 裡根本沒有 `GoToPage` 這個
case —— 0.11.11762.0 沒有,連 `main` 都沒有。「存完之後叫 CmdPal 跳到某一頁」用回傳值
做不到。唯一還通的路是讓那一列的命令**本身就是一個頁面**(CmdPal 對 `IPage` 的處理是導覽,
不是 `Invoke`),寫檔因此發生在 `CapturedNotePage.GetContent()` 裡。

「打字打到一半就存檔了」不會發生:清單項的 `CommandViewModel.InitializeProperties` 只讀
Id / Name / Icon,不碰 `GetContent`。內容是使用者真的按下 Enter、CmdPal 建出
`ContentPageViewModel` 時才取的。`GetContent` 本身可能被呼叫很多次(編輯完回來、
`RaiseItemsChanged`),所以存檔那一段有一道只跑一次的旗標 —— 少了它同一則想法會存成好幾個檔。

**3. 命令列要分兩次交出去。** 「編輯」「在預設編輯器開啟」都要拿到存好的 `Note`(檔案路徑、
id)才建得出來,而 CmdPal 讀 `Commands` 的時機比 `GetContent` **早**
(`InitializeProperties` 裡先 `BuildCommandViewModels`,後 `FetchContent`)。所以建構時只掛
「完成」一顆,存檔成功後換掉整個 `Commands` 陣列,靠 `PropChanged` 讓 CmdPal 重讀 ——
`IContentPage` 走的是無條件訂閱那條路,不是 `IDetails` 那種斷掉的(見〈詳細面板寬度〉)。

「完成」回傳的是 `Dismiss()` 而不是 `GoHome()`:使用者記完這則想法就要回去做原本的事,
留一個主搜尋框在畫面上只是多一次 Esc。存檔失敗時它會改成 `GoBack()` —— 剛打的那句話
還在快速記下頁的搜尋框裡,退回去就能重試。

### 貼上多行內容

CmdPal 的搜尋框是單行 `TextBox`,往裡面貼一段多行的 Markdown **只有第一行進得來**,
其餘的無聲消失。那是 CmdPal 的控件,擴展改不了。

所以快速記下頁在偵測到剪貼簿是多行文字時,會多給一列「內文取自剪貼簿(N 行)」——
標題還是用打的,內文直接讀剪貼簿原文,換行、縮排、程式碼區塊通通留著,完全不經過搜尋框。

### 設定頁有兩個入口,而且只有一個會自己更新

同一份設定,CmdPal 讓使用者從兩個地方看到:

| 入口 | CmdPal 怎麼拿 |
|---|---|
| 清單頁 `Ctrl+K` → 設定 | 我們放在 `MoreCommands` 裡的頁面,每次導覽進去都重建 viewmodel |
| 設定 → Extensions → Notelet | `ICommandSettings.SettingsPage`,**整個 CmdPal 生命週期只初始化一次** |

第二條路是這樣寫的(`ProviderSettingsViewModel`):

```csharp
if (_provider.Settings.Initialized)
{
    return _provider.Settings.SettingsPage;   // 永遠是同一個 viewmodel
}

_initializeSettingsTask ??= Task.Run(InitializeSettingsPage);   // 只跑一次
```

那個 viewmodel 只有在頁面發出 `ItemsChanged` 時才會重新 `GetContent()`。toolkit 的
`SettingsContentPage` 確實會轉發 —— 但它轉發的來源是 `Settings.SettingsChanged`,
而**擴展發不出那個事件**:`RaiseSettingsChanged()` 是 `internal`,唯一的呼叫者是
使用者按下 Save 時走的 `SettingsForm.SubmitForm`。

結果就是:清單頁按 `Ctrl+D` 改了寬度、檔案也存了,那一頁卻停在啟動時的值。

修法是**自己實作 `ICommandSettings`**(整個介面只有 `SettingsPage` 一個成員),
把 `NoteletSettingsPage` 交出去,發 `ItemsChanged` 的權力就回到我們手上。
兩個入口共用同一個頁面實例,所以看到的永遠一致。

那個頁面因此**不能跟著 `ProviderState` 重建** —— CmdPal 在 provider 剛連上時就把
`Settings` 讀走了,換了實例它不知道,只會繼續用手上那個。

#### 送出表單之後也要 `Refresh()`,而且是每一次

卡片是**建構時**就把值烤進 `DataJson` 的(`FormContent` 沒有別的傳值管道),而上面那條
「只初始化一次」的路代表 CmdPal 不會因為導覽進頁面就重新 `GetContent()`。
所以只要漏掉一次 `Refresh()`,那張卡片就永遠停在 provider 剛連上時的值。

實際踩到過:分隔符改成 `##`、檔案也存了、快速記下也確實照 `##` 切,可是設定頁**每次打開
都顯示 `;;`**。當時只有 `DetailsWidthChanged` 接到 `Refresh()`,新加的設定沒接上。

**比顯示錯更糟的是它會把值吃回去。** 卡片上壓著的過期值,在下一次送出時會被當成使用者
的輸入寫回設定 —— 只改資料夾按一次儲存,就足以把 `##` 默默還原成 `;;`。

所以 `OnSettingsApplied` 一進來就 `Refresh()`,排在「資料夾沒變就 return」的前面,
不分欄位、不比對新舊。`DetailsWidthChanged` 那條線要留著:`Ctrl+D` 是從設定頁外面改值,
根本不會走到 `Applied`。兩邊都命中時會多重讀一次,無害。

**加新設定項時記得這條** —— 忘了不會有任何錯誤訊息,只會安靜地顯示舊值。

#### 表單也是自己的

頁面的內容不是 toolkit 的 `Settings.ToContent()`,而是自己寫的一張 Adaptive Card
(`NoteletSettingsForm`)。三個理由:

1. **toolkit 的卡片放不下「瀏覽…」按鈕。** 設定項只能一格一格排下去。
2. **欄位名根本不會顯示。** 它把 `Label` 塞進卡片的 `title`,而 `Input.Text` 沒有那個屬性;
   真正會顯示的 `label` 它拿去放 `Description`。結果每個欄位頭上頂著一整句說明,
   看不到「筆記資料夾」這種短名字。
3. **送出之後它固定 `GoHome`**,而按「瀏覽…」時得留在原地。

代價是存檔那條路要自己接:值交給 `SettingsManager.Apply`,由它存檔並發出
`Applied`(provider 拿去比對資料夾)與 `DetailsWidthChanged`(清單頁跟著變寬度)。
toolkit 的 `Settings.RaiseSettingsChanged()` 是 `internal`,本來就叫不動。
標籤、說明、選項仍然只有 `SettingsManager` 那一份,表單只負責畫。

#### 資料夾旁邊的「瀏覽…」

按下去開的是系統的選資料夾對話框(`IFileDialog` + `FOS_PICKFOLDERS`,見 `FolderPicker`)。
擴展是個**沒有視窗**的 out-of-process COM server,所以有兩件事跟一般 app 不一樣:

- **對話框跑在自己的 STA 執行緒上。** `Show` 會擋到使用者關掉對話框為止,而呼叫端那條
  執行緒是 CmdPal 的(`ContentFormViewModel.HandleSubmit` 裡的 `Task.Run`),
  不能讓它在那邊等。`SubmitForm` 因此立刻回 `KeepOpen`,選好之後才用回呼把路徑送回來。
- **選好就直接存,不等使用者再按一次「儲存」。** 對話框一拿到焦點,CmdPal 主視窗就會把
  自己藏起來(`MainWindow` 的 `Deactivated` → `HideWindow`,沒有開關可以關掉),
  表單跟著一起消失 —— 那時候還壓在表單裡的值,使用者既看不到也按不到。

- **對話框掛在一個隱藏的 tool window 底下。** 沒有 owner 的頂層視窗會拿到自己的工作列按鈕,
  而這個進程在工作列上的身分是 MSIX 套件的圖示 —— 目前那還是 Visual Studio 模板留下的
  空白方框,使用者只會看到一個看不懂的東西。掛上 owner(內建的 `STATIC` 類別 +
  `WS_EX_TOOLWINDOW`,從不顯示)就不再是「無主視窗」,工作列不給它按鈕。
  owner 的大小刻意跟當下的前景視窗一樣:對話框以 owner 為中心擺位,給 0×0 會貼到螢幕左上角。
  **不能拿 CmdPal 的視窗當 owner** —— `IFileDialog` 會 `EnableWindow(owner, FALSE)`,
  而那個視窗馬上就要自己藏起來。

還有一個 Windows 本身的限制:只有前景進程開的視窗搶得到焦點,而我們這個 COM server
從頭到尾沒收過使用者的輸入。不管的話對話框會開在 CmdPal 後面,而且現在它連工作列按鈕
都沒有,等於整個消失。`FolderPicker` 因此會去找「屬於自己、而且看得見」的那個頂層視窗
(平常一個都沒有),再 `SetForegroundWindow` 把它拉到前面;拉不動就退回 `BringWindowToTop` /
`SwitchToThisWindow`。

這條路實測過:把 `ForegroundLockTimeout`(這台機器是預設的 200000ms)重新武裝之後
—— 也就是模擬「使用者剛剛才點過東西」—— 對話框仍然被拉到了前景。

#### 表單後面那塊空白是承重牆

設定頁的表單後面掛著一塊**空的** `MarkdownContent`。看起來像忘了刪的東西,
**但刪了焦點就會亂跳**。

`ContentFormControl` 載入後會自動聚焦第一個輸入欄位,但只在自己是頁面上唯一的控件時:

```csharp
element.Loaded -= OnFrameworkElementLoaded;

if (!ViewModel?.OnlyControlOnPage ?? true) return;   // 不是唯一控件就不聚焦
```

`OnlyControlOnPage` 是 `ContentPageViewModel` 按內容數量算的(`newContent.Count == 1`)。
而我們每按一次 `Ctrl+D` 就得叫 CmdPal 重讀表單 —— 重讀等於控件重建、再觸發一次 `Loaded`。
設定視窗開在背景時,那一下就把焦點從主視窗搶了過去。

湊滿兩塊內容,`OnlyControlOnPage` 就是 false,重建也不搶焦點。代價是打開設定頁時
游標不會自動落在第一個欄位。編輯與新增那兩個表單不受影響,它們仍然只有一塊內容。

**為什麼那一塊是空的**,而不是拿來寫句說明:內容區塊之間有大約 32px 收不掉的間距 ——
`ContentPage.xaml` 的 `ItemsRepeater` 用 `StackLayout Spacing="8"`,每塊內容自己又有
`Margin="0,4,4,4"` 與 `Padding="12,8,8,8"`。說明擺前面是一段跟表單斷開的旁白,
擺後面更像掉在半空(兩種都做過)。而且 markdown 那條路**沒有淡色可用**,
CmdPal 的 `MarkdownThemes` 只設定了字級與 inline code。

所以說明文字全部進卡片:卡片裡的 `TextBlock` 有 `isSubtle` 跟 `size: small`,
那才是提示該有的樣子,而且貼得住它說明的那個欄位。空白那一塊排在最後,
那 32px 就落在儲存按鈕底下,看不出來。

內容要是**空字串**,不是空白字元 —— 一個空白也是一行文字,會再多撐出約 20px。
剩下的 32px 是 CmdPal 的版面寫死的,拿不掉。

這一塊完全依賴「CmdPal 不過濾空內容」(`ViewModelFromContent` 只看型別)。
哪天它加了一道 `IsNullOrEmpty`,這塊會**無聲**消失、焦點又開始亂跳 ——
手動驗證清單裡「焦點不會被搶」那一項就是為了接住這種回歸。

### 刪除全部為什麼是一頁

`Notelet:刪除所有筆記` 按下去不會刪任何東西,它進到一個清單頁,把即將被刪的檔案列出來。

原因是這個動作的範圍比它的名字大得多。掃描的是筆記資料夾底下(含子資料夾)**所有的
`.md`**,而且**不分辨檔案是不是 Notelet 寫的** —— 那是列清單時刻意的設計(外來的 `.md`
也要看得到),但放到批次刪除上就變成一把沒有握把的刀:資料夾要是被指到既有的
Obsidian vault、docs 目錄、或任何有 `README.md` 的專案資料夾,一次就全掃走了。
預設的 `%OneDrive%\Notelet` 是專用資料夾,所以預設設定沒有這個問題 —— 風險是改過路徑
之後才出現的。

一個確認框放不下這些。它只有一行標題與一行說明,而使用者真正需要看見的是「到底是哪些檔案」。
所以那一頁長這樣:

| 區塊 | 內容 |
|---|---|
| 動作 | `刪除全部 N 則`(副標是資料夾路徑);有外來檔案時多一列 `只刪 Notelet 建立的 M 則` |
| 不是 Notelet 建立的 | 排在最前面 —— 那正是最需要先看到的一批,圖示也不一樣 |
| Notelet 筆記 | 其餘的,副標是相對於筆記資料夾的路徑,子資料夾一眼看得出來 |

每一列 Enter 都是進預覽頁,唯讀,動手前可以一則一則掃過去。
清單超過 `MaxResults` 被截斷時,最後一列會明講**沒列出來的一樣會被刪**。

「只刪 Notelet 建立的」那一列是這個做法真正換來的東西 —— 命令的形狀下根本放不下第二個動作。

順帶修掉一個小毛病:原本沒有筆記時只能回一個 toast,而 toast 的預設收尾是把整個 CmdPal
關掉,使用者只看到面板一閃就沒了。頁面有 `EmptyContent`,空的情況本來就有地方講。

資源回收筒不是絕對的保險:檔案在網路磁碟、沒有回收筒的裝置上,或大過回收筒配額時,
Windows 會直接永久刪除,而我們設的 `FOF_NOCONFIRMATION` 正好把那個警告框壓掉了。
這件事寫在頁面的詳細窗格裡。

### 確認框的預設按鈕是反過來的

`ConfirmationArgs.IsPrimaryCommandCritical` 聽起來像「把按鈕標成危險色」,但 CmdPal 拿它做的
唯一一件事是:

```csharp
if (vm.IsPrimaryCommandCritical)
{
    dialog.DefaultButton = ContentDialogButton.Close;   // ← 預設落在「取消」
}
```

(`ShellPage.xaml.cs`,那段把紅色按鈕的樣式註解掉了,所以連顏色都沒有。)

也就是說**設了它,Enter 就等於放棄**。所以兩個刪除的用法剛好相反:

| | `IsPrimaryCommandCritical` | 為什麼 |
|---|---|---|
| 刪一則 | **不設** | 有資源回收筒兜底,不值得為此讓每次刪除都多按一次方向鍵 |
| 批次刪除(兩列都是) | **設** | 一次動幾十個檔案就該多花那一下 |

SDK 沒有辦法把預設按鈕指定成「確認」—— CmdPal 只有「設成取消」跟「不設」兩種,
不設時 `ContentDialog.DefaultButton` 是 `None`。

### 命令 Id 為什麼要寫死

`src/Notelet/CommandIds.cs` 裡那幾個字串是對外承諾,跟資料格式一樣不能改。

CmdPal 把使用者對命令做的設定 —— alias、全域快速鍵、釘選、fallback 的顯示規則與排序 ——
全部存在自己的 settings.json 裡,鍵就是命令的 `Id`。而**命令沒有設 `Id` 時 CmdPal 會現場算一個**:
`TopLevelViewModel.GenerateId` 拿 `ProviderId + DisplayTitle + Title + Subtitle` 去做 WyHash64。
也就是說標題變一個字,那個命令對 CmdPal 來說就變成了另一個命令,使用者設過的東西全部對不上。

**現在這件事比以前更要緊**:快速記下唯一的入口就是使用者自己設的 alias,而 alias 存的鍵
就是 `Id`。`Notelet.QuickCapturePage` 改一個字,使用者的 alias 當場失效,而且症狀是
「打 `n ` 沒反應」—— 看不出跟改標題有任何關係。

歷史教訓來自已經移除的 fallback,它的標題本來就跟著使用者打的字一直變:CmdPal 的
settings.json 裡曾經留下兩個 Notelet fallback 條目,把其中一個的雜湊反推回去,正好是標題
`記下:你好` —— 某次重新載入時搜尋框裡剛好是那句話。表現出來就是「改了一次設定,
快速新增就莫名其妙不會出現了,連改回原本的前綴也救不回來」。

(那兩個雜湊條目可能還躺在你的 CmdPal settings.json 裡,無害 —— CmdPal 會忽略對不上的鍵。)

## 設定項

| 設定 | 預設 | 說明 |
|---|---|---|
| 筆記資料夾 | `%OneDrive%\Notelet` | 存放 Markdown 檔的位置。旁邊的「瀏覽…」會開系統的選資料夾對話框,選好就直接存 |
| 快速記下的分隔符 | `;;` | 前面是標題、後面是內文。長度不限,半形全形算同一個,清空就回到 `;;`。改完當下開著的快速記下頁就會跟上,不必 Reload。挑選的理由與 `,,` 的建議見〈標題與內文用分隔符切開〉 |
| 記下後先看一眼 | 關閉 | 開啟後 Enter 記下並停在筆記上、再按一次才收起,`Ctrl+Enter` 則是記完直接收起;關閉時兩者對調。兩條路永遠都在,見〈記下之後要不要先看一眼〉 |
| 詳細面板寬度 | 窄 | 清單頁按 `Ctrl+D` 也能循環,兩邊改的是同一個值 |

只有四項。快速記下沒有前綴設定 —— 它的入口(alias、全域快速鍵)由 CmdPal 那邊管,
不在這份設定裡;進得了那一頁就代表意圖很明確,打什麼就記什麼。

**手改 `settings.json` 要小心格式。** toolkit 的載入是一個沒有逐項 `try/catch` 的迴圈
(`Settings.Update`),某一項解析失敗,例外會一路拋到 `LoadSettings` 的 `catch`,
**排在它後面的設定項連碰都碰不到**,靜靜地退回預設值 —— 沒有任何錯誤訊息。
最容易踩的是「記下後先看一眼」:`ToggleSetting` 存的是**字串** `"true"` / `"false"`
(Adaptive Cards 的 `Input.Toggle` 回傳的就是字串),寫成 JSON 的 `true` 就會炸。
所以它在 `Settings.Add` 裡刻意排最後,寫錯只影響它自己。

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
    QuickCapture      標題/內文切分(分隔符可設定,半形全形視為同一個)
    IFileDeleter      刪除的去向(預設永久刪;UI 層換成資源回收筒)
    NoteletOptions    執行期設定
  Notelet/           CmdPal 擴展(MSIX COM server)
    NoteletExtension / NoteletCommandsProvider / SettingsManager
    CommandIds        頂層命令的固定 Id(改了會清掉使用者的 alias/快速鍵/釘選)
    IDetailsWidthStore / ICaptureSeparatorStore / ICapturePreviewStore
                      「不重建、由現有頁面自己響應」的那幾個設定的窄介面
    RecycleBinFileDeleter  SHFileOperationW,把筆記送進資源回收筒
    FolderPicker      IFileDialog + FOS_PICKFOLDERS,設定頁的「瀏覽…」
    Pages/            快速記下、記下後的預覽、清單、預覽、編輯、新增、刪除全部、設定
tests/
  Notelet.Core.Tests/  xUnit
tools/
  deploy.ps1           build → 註冊 → 驗證
  VerifyRegistration/  查 AppExtension 目錄的探針
  ApiDump/             印出 CmdPal Toolkit 型別的實際簽章
```

分層的重點:`Notelet.Core` 不知道 Command Palette 的存在。所有容易寫錯的邏輯
(front matter 解析、檔名、搜索排序、標題/內文切分)都在那一層,因此都有單元測試涵蓋。
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
