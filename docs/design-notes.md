# Inkling 設計考證

這份文檔收的是「為什麼」—— 每一個看起來繞路的設計背後的查證過程與取捨。
讀者是**未來的維護者**(包括半年後的自己)與**其他 CmdPal 擴展作者**:很多結論
(fallback 的空標題過濾、`IDetails` 的通知斷線、`ToastArgs.Result` 的預設是 `Dismiss`)
對任何 CmdPal 擴展都成立。**也包括推翻掉的那幾條** —— 那些留著不刪，它們是這份文檔
最有用的部分:[toast 不會搶焦點](#toast-does-not-steal-focus)、
[確認框的預設按鈕是有作用的](#confirm-dialog-colors)。

使用者文檔在 [README](../README.md);這裡的每一節 README 都只留兩三行結論加連結。

**所有斷言的對照版本**:CmdPal **0.11.11762.0**(使用者實際安裝的 MSIX)與
PowerToys **main** 的原始碼。從 main 讀到的每一條結論都對安裝版做過 byte-scan 確認才寫進來
—— 兩邊有落差的地方(main 有、安裝版沒有)文中都明著標。掃法與已知的掃法陷阱
(方法名在 UTF-8 的 #Strings heap,**字串常量在 UTF-16 的 #US heap**，只掃一種會得到
假陰性)見 [CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉。

## 捕捉與預覽

<a id="capture-page-not-fallback"></a>

### 快速記下為什麼是頁面，不是 fallback

**這條路試過，而且做完了，最後整個移除。** 別再試一次 —— 下面是為什麼。

「在主搜尋框打字→Enter」這件事本身非 fallback 不可:**只有 fallback 拿得到使用者正在打的字**
(`IFallbackHandler.UpdateQuery`)，頂層命令被叫起來時搜尋框已經清空了。

問題出在**沒命中的時候藏不起來**。內建的 fallback 不需要觸發詞:計算機看查詢算不算得出來、
Run 看是不是一個可執行檔、開網址看像不像 URL，不像就自己藏起來。筆記沒有這種判準 ——
任何一句話都是合法的筆記。所以只能用前綴當意圖判斷，沒命中就把 `Title` 設成空字串隱藏。

而那招要成立，得靠 CmdPal 端把空標題的項目濾掉，**0.11.11762.0 沒有確實做到**:

- 勾了 Include in the Global result 之後，fallback 走的是 `_scoredFallbackItems`
  (跟一般結果一起計分)，不是底部那個 fallback 區塊。而**不勾就沒有意義** ——
  它會排在所有結果後面，Enter 不落在我們身上，「打字→Enter」直接不成立。
- `MainListPageResultFactory.Create` 只對底部區塊那條路過濾空標題，而且陣列大小是按
  **未過濾**的 count 算的、寫入時才過濾，尾端因此留 null。旁邊還躺著一個沒人呼叫的
  `GetNonEmptyFallbackItemsCount`;而 `len2 = scoredFallbackItems?.Count ?? 0;` 上方那行
  註釋 `Empty fallbacks are removed prior to this merge` 點出了去向 ——
  過濾被提前到呼叫端 `GetSearchViewItems` 了，而那是 `main` 分支才有的。
- 佐證版本落差:byte-scan 安裝版的 `Microsoft.CmdPal.UI.exe`,`GetSearchViewItems` 與
  `MainListPageResultFactory` 在，`MainListRanker` / `ClassifyTier` / `FallbackFloor` **不在**。

表現出來就是:**不管打什麼，結果裡永遠多一個點不動的空列**。這不是我們能修的。

**換成頁面之後按鍵數一模一樣。** `!` 空白 想法 Enter —— 唯一的差別是中間會跳一次頁。
換來的是主搜尋框完全乾淨、不再受 CmdPal 版本行為影響，以及一件 fallback 結構上做不到的事:
fallback 只有一列，頁面有一整個清單，所以「記下」底下能直接列出標題相近的既有筆記。

順帶一提，前綴設定跟著一起消失了:alias 就是前綴，而且由 CmdPal 統一管理。

alias 的機制要知道兩件事(`AliasManager.CheckAlias`):

| | |
|---|---|
| indirect alias 存的鍵是「alias + 空白」 | 所以填 `!`，實際觸發的是你打完 `! ` 的那一刻 |
| 觸發時送 `ClearSearchMessage` + `PerformCommandMessage` | 搜尋框被清空、跳進頁面。**所以 alias 觸發的命令拿不到觸發當下那句話** —— 但跳進去之後打的字，是我們自己 `DynamicListPage.UpdateSearchText` 收的，完全掌控。這正是頁面版能成立的原因 |

**哪天 CmdPal 修好了想把 fallback 加回來**:整套實作在 git 歷史裡，移除它的是
`4a49505 refactor!: remove the fallback quick capture path`。
**路徑不能寫成 `src/Inkling/…`** —— 那個檔案存在的時候專案還叫 Notelet,
照現在的路徑查一行輸出都沒有(而「沒有輸出」跟「查錯了」長得一模一樣)。
要自己找的話用 `git log --all --diff-filter=D --oneline -- '*QuickCaptureFallbackItem.cs'`。
判準是打一句不帶前綴的話，結果裡不再多出空列。真要加回來記得:alias 比 fallback
早一步處理(`MainListPage.UpdateSearchTextCore` 開頭就 `if (aliases.CheckAlias(newSearch)) return;`),
所以 alias 別跟前綴設成同一個字，否則 alias 會先把搜尋框清掉，fallback 再也看不到那句查詢。

<a id="separator-split"></a>

### 標題與內文用分隔符切開

`買咖啡機;;比較過 Breville 跟 Sage` → 標題是「買咖啡機」,`;;` 之後的都進內文。
分隔符是設定項，填什麼就用什麼(長度不限)，空著就回到預設的 `;;`。

**為什麼預設是兩個分號。** `;` 在 home row 上、右手小指原位，不用按 Shift，連打兩下最快;
而**連續兩個**分號在自然語句裡幾乎不出現，所以標題可以自由使用單一個分號
(`for (var i = 0; i < 10; i++)`、中文句子的頓隔)。要求連打兩次，誤觸的成本就沒了 ——
一個人不會無意間打出兩個相連的分號。

唯一真的會撞到的是 C 系的無限迴圈寫法 `for (;;)`。常寫那種筆記的話，**建議換成 `,,`**:
鍵位一樣不用 Shift，碰撞比分號更少。

**半形全形算同一個字。** 中文輸入法打出來的是全形，而中英切換的當下打出哪一個並不受控。
折算是逐字元、長度不變的(全形 ASCII U+FF01–U+FF5E 與半形差值固定 `0xFEE0`),
所以 `；；`、`;；` 都切得開，**設定欄位那一頭也走同一個折算** —— 設定填 `;;`、打字打 `；；`
一樣成立。沒有半形對應的中文標點(`、` `。`)不在範圍內，填那些就只認它自己。

只切第一組，後面的分隔符是內文的一部分。只打了分隔符還沒打內文不影響，存的就是標題。

**換掉分隔符之後，舊的那一組就只是普通文字**，不留備援 —— 否則使用者永遠沒辦法把分號
寫進標題，換設定就失去意義。改了之後**當下開著的快速記下頁就會跟上**(提示文字、
切出來的標題與內文都是)，不必 Reload:那一頁訂閱
`ICaptureSeparatorStore.CaptureSeparatorChanged`。

**為什麼要有這條事件線，而不是讓 provider 整組重建就好** —— 因為重建對它根本沒用。
CmdPal 手上握著的是使用者當下開著的那個頁面實例，新建的頁面它不會去拿:實測 log 裡
`BuildState` 跑完之後一次 `GetItems` 都沒有，直到 Reload，而舊實例的項目快取(查詢字串
與 `Version` 都沒變)就這樣把舊值一路留著。硬重建反而更糟，會把還在被使用的 repository
連同 `FileSystemWatcher` 一起 `Dispose` 掉。所以**資料夾以外的設定一律讓現有頁面自己響應**,
「記下後先看一眼」走的是同一個形狀(`ICapturePreviewStore.CapturePreviewChanged`)。
頁面上快取項目的地方，快取鍵也要帶上那個設定值，否則事件收到了、拿到的還是舊結果。

頂層那一列的副標刻意寫「分隔符」而不是「分號」—— 命令陣列只在資料夾變了才重建，它跟不上。

<a id="capture-preview"></a>

### 記下之後要不要先看一眼

**「記下後先看一眼」預設是開的**:Enter 記下並停在那則筆記的完整 Markdown 上，
再按一次 Enter 才收起 Command Palette。關掉之後改成記完就收:存檔 →
toast「已記下：標題」→ Command Palette 消失。

**兩條路的結尾都會跳那個 toast，而且是同一句話。** 開著設定時它跳在「完成」那一下，
關掉時跳在存檔那一下 —— 時機不同是因為兩條路的「這件事做完了」發生在不同時刻，
但使用者拿到的確認一樣。曾經只有關掉設定那條路有:開著設定按完 Enter 什麼都沒有，
同一個記下動作換個設定就少了結尾確認，那是不一致，不是設計。
兩邊共用 `Resources.CaptureSaved`，文案不會漂移。

**同一時間只有一條路在。** 做過「兩條都掛著，設定只決定哪一條在 Enter 上，另一條落到
`Ctrl+Enter`」，拿掉了 —— 沒有人會為了看一眼特地去按 `Ctrl+Enter`，那一列留著只是讓
選單多一項要讀的東西。設定就是設定。

**為什麼預設是看一眼。** 代價是每次記下都多按一個 Enter，但那一下換到的是「東西真的
寫進檔案了」的確認 —— 快速記下的整個前提是想法丟出去就不再回頭看，存檔失敗、標題內文
切錯位置(分隔符打成單一個分號之類)如果當場沒發現，之後也不會有人去發現。記完就收的
那條路上，存檔失敗的回饋只有底部命令列一個狀態訊息(為了留住搜尋框裡還沒存下的那句話，
那條路**不能發 toast**，見下面第 1 點)—— 余光裡成功與失敗幾乎分不出來。
想要極致速度的人再去設定裡關掉。

實作上有三件事是被 CmdPal 逼出來的:

**1. 停留期間 `Result` 一律 `KeepOpen`，離開那一下才 `Dismiss` —— 分清楚這兩段。**

⚠ 這一點以前寫的是「停留期間**一個 toast 都不能發**」，理由是「toast 是另一個會搶焦點的
視窗，而 CmdPal 主視窗一失焦就把自己藏起來(`MainWindow_Activated` →
`EndSession("LostFocus")`)」。**那個理由是假的**，量測見
[〈toast 不會把面板關掉〉](#toast-does-not-steal-focus) —— toast 拿不到前景，
關面板的是 `ToastArgs.Result`(而它的預設剛好是 `Dismiss`，所以現象對得上、歸因錯了)。
複製內文因此已經改成發 toast 配 `KeepOpen`。

真正的規矩是**停留期間的收尾一律 `KeepOpen`**:進頁、存檔那一刻、存檔失敗、複製內文。
存檔失敗的訊息直接畫在頁面上;記完就收那條路的存檔失敗走 `ToastStatusMessage`
= 底部 InfoBadge，配 `KeepOpen`，搜尋框裡那句話留著，修好問題再按一次 Enter 就是重試。

**「完成」那一下剛好相反**:按下去的語意就是收工，面板本來就要關，所以明著回 `Dismiss`,
而 toast 在面板收掉之後還留得住。判斷一條路怎麼收尾，問的不是
「這裡會不會關面板」，而是**「使用者接下來還要不要看著這個面板」** —— 要，就一個都不能發;
不要，那 toast 反而是唯一能在面板消失之後還留在畫面上的通道(InfoBadge 畫在面板上，
面板收了就跟著沒了)。同一個判斷套在隨手草稿的存檔與「捨棄變更」上，結論一樣。

**但「本來就要關」不等於「可以發」還有一個例外:跳到外部程式的那幾條路。**
那時剛拿到焦點的是使用者要用的編輯器或檔案總管，toast 比它晚出現，會把它壓下去 ——
見〈跳出去之後回得到哪一頁〉。

**2. `CommandResult.GoToPage` 是空殼。** SDK 有那個型別，但 CmdPal 的
`ShellViewModel.UnsafeHandleCommandResult` 那個 switch 裡根本沒有 `GoToPage` 這個
case —— 0.11.11762.0 沒有，連 `main` 都沒有。「存完之後叫 CmdPal 跳到某一頁」用回傳值
做不到。唯一還通的路是讓那一列的命令**本身就是一個頁面**(CmdPal 對 `IPage` 的處理是導覽，
不是 `Invoke`)，寫檔因此發生在 `CapturedNotePage.GetContent()` 裡。

「打字打到一半就存檔了」不會發生:清單項的 `CommandViewModel.InitializeProperties` 只讀
Id / Name / Icon，不碰 `GetContent`。內容是使用者真的按下 Enter、CmdPal 建出
`ContentPageViewModel` 時才取的。`GetContent` 本身可能被呼叫很多次(編輯完回來、
`RaiseItemsChanged`)，所以存檔那一段有一道只跑一次的旗標 —— 少了它同一則想法會存成好幾個檔。

**3. 命令列要分兩次交出去。** 「編輯」「在預設編輯器開啟」都要拿到存好的 `Note`(檔案路徑、
id)才建得出來，而 CmdPal 讀 `Commands` 的時機比 `GetContent` **早**
(`InitializeProperties` 裡先 `BuildCommandViewModels`，後 `FetchContent`)。所以建構時只掛
「完成」一顆，存檔成功後換掉整個 `Commands` 陣列，靠 `PropChanged` 讓 CmdPal 重讀 ——
`IContentPage` 走的是無條件訂閱那條路，不是 `IDetails` 那種斷掉的(見〈原始文字模式〉)。
補齊的那幾項跟清單頁、預覽頁共用同一份組裝(`NoteCommands`)，鍵位因此三頁一致。

「完成」回傳的是 `Dismiss()` 而不是 `GoHome()`:使用者記完這則想法就要回去做原本的事，
留一個主搜尋框在畫面上只是多一次 Esc。存檔失敗時它會改成 `GoBack()` —— 剛打的那句話
還在快速記下頁的搜尋框裡，退回去就能重試。

<a id="paste-multiline"></a>

### 貼上多行內容

CmdPal 的搜尋框是單行 `TextBox`，往裡面貼一段多行的 Markdown **只有第一行進得來**,
其餘的無聲消失。那是 CmdPal 的控件，擴展改不了。

所以快速記下頁在偵測到剪貼簿是多行文字時，會多給一列「內文取自剪貼簿(N 行)」——
標題還是用打的，內文直接讀剪貼簿原文，換行、縮排、程式碼區塊通通留著，完全不經過搜尋框。

<a id="preview-line-breaks"></a>

### 預覽的換行處理

標準 Markdown 裡單一換行等於空格，所以打三行會顯示成一行。對一個隨手記想法的工具來說
那不是使用者要的，所以**預覽時**會把單一換行當成真的換行。

只動拿去渲染的那份字串，**磁碟上的 `.md` 一個字都不變** —— 用別的編輯器打開仍然是標準
Markdown。程式碼區塊、表格、縮排程式碼、setext 標題底線這些「換行本來就有意義」的地方
會避開。規則在 `Inkling.Core/NotePreview.cs`，測試在 `NotePreviewTests.cs`。

已知的取捨:貼進來的 Markdown 文件如果有「中途硬換行的段落」，渲染時會固定斷在原本的
折行處，而不是隨視窗寬度重排。

曾經考慮過依內容自動判斷「這則是 Markdown 文件還是隨手記」，只對後者保留換行，結論是
**不做**。判斷的單位只能是整則筆記，而誤判的代價落在最常見的情況上 —— 底下這種一個標題
加幾行隨手記，會因為偵測到 `#` 就被判成 Markdown 文件，兩行 prose 被併成一行:

```markdown
# 想法
今天很累
明天再說
```

而且使用者無從預測:同樣打三行，加了個 `#` 之後渲染就變了，還看不出為什麼。
Obsidian、Bear、Apple Notes、Google Keep 這些做筆記的一律預設保留換行，沒有一個去猜意圖;
Obsidian 把嚴格 CommonMark 做成 Strict line breaks 設定，預設也是關閉。要處理那個 case
的話，顯式開關(front matter 欄位或全域設定)比啟發式判斷可靠 —— 目前沒有需求，先不加。

<a id="source-mode"></a>

### 原始文字模式(`Ctrl+U`)

`Ctrl+U` 在「渲染結果」與「原文」之間切換。用途一開始是選取、複製帶符號的原文
(標題的 `#`、粗體的 `**`、連結的 `[](…)` 渲染完就消失了，但要複製走的往往正是這些符號),
後來冒出一個更硬的理由:**貼進筆記的 HTML / SVG 整段會被渲染器吃掉**，畫面看起來是空白的，
使用者以為筆記壞了 —— 真機上就是這樣發現的(一則內容是 SVG 的筆記，預覽頁只剩標題)。

#### 清單頁換的是詳細窗格

- **切換不會重建清單。** 只換掉每個項目的 `ListItem.Details`,CmdPal 收到屬性變更後
  只重畫右邊那一塊。若改用 `RaiseItemsChanged`,CmdPal 是拿 `IListItem` 的**物件識別**
  當鍵在快取 viewmodel 的，想讓它重讀詳細內容就得換掉整批項目物件，整份清單翻新一次，
  選中項就有機會跑掉 —— 而按下這個鍵的當下正在看某一則筆記，跳走就沒有意義了。
- **原文靠逐字逃脫顯示，不用程式碼區塊。** 反斜線只存在於送給渲染器的字串裡，
  畫面上顯示的、以及選取複製走的，都是還原後的原文，複製保真度不受影響。
  用程式碼區塊雖然連空白都能一字不差，但 CmdPal 會替它畫上外框與底色，
  樣式寫在 CmdPal 自己的資源裡、擴展改不動，在窄窄的詳細窗格裡太搶版面。

已知的取捨:**行首縮排與連續空行會被渲染器正規化**。段落開頭的四個空白在 CommonMark 裡
就是縮排程式碼區塊，留著等於把外框畫回來，所以一律去掉。實測本專案自己的開發筆記
(43 行)，只有 4 行受影響，全是巢狀清單的續行縮排，其餘一字不差。

#### 預覽頁換的是整頁，而且用的是 `PlainTextContent`

詳細窗格只吃 Markdown 字串(`IDetails.Body`)，所以那邊只能逃脫。整頁的預覽沒有這個限制 ——
SDK 有 `IPlainTextContent`，對應 CmdPal 的純文字檢視器:**字串原封不動**，縮排、連續空行、
HTML / SVG 通通照原樣顯示，而且那個檢視器自帶右鍵選單(複製、全選、自動換行、等寬字、縮放)。
擴展這邊能指定的只有三件事:`Text`、`FontFamily`(`UserInterface` / `Monospace`)、`WrapWords`。
Inkling 送的是**等寬 + 自動換行**:看的是縮排對齊，比例字型會讓對齊失去意義;而關掉換行的話
長行要橫向捲動才看得到，這個面板是鍵盤驅動的，橫捲很難按(使用者要關，右鍵選單裡有)。

**這條路在 0.11.11762.0 安裝版是通的**，不是照 `main` 的原始碼寫的 —— byte-scan 對照:

| 掃的東西 | 在哪裡 | 編碼 |
|---|---|---|
| `ContentPlainTextViewModel` / `PlainTextContentViewer` / `IPlainTextContent` / `get_WrapWords` / `PlainTextTemplate` | `Microsoft.CmdPal.UI.exe` | UTF-8 |
| `PlainTextContentTemplate`、`PlainTextContentViewer_WordWrap` / `_MonospaceFont` | `resources.pri` | UTF-16 |

原文只給**內文**，不補標題那一行 `#`:標題已經在頁面標題列與底部命令列上，而這個模式的
承諾是「檔案裡的字元一個不多一個不少」，補一行出來就是騙人的(渲染模式才補，見
`NotePreview.Render`)。

#### 狀態是全域的，而且存進 `settings.json`

三個畫面(清單頁、預覽頁、記下並預覽頁)共用同一個狀態，存在 `Inkling.ShowSource`,
關掉 CmdPal、Reload 擴展都還在。**設定頁上刻意沒有這一項** —— 切換鍵本身就是它的介面，
放進表單只會多一個要維護的雙向同步(詳細面板寬度就是這樣被拿掉的，見〈詳細面板寬度固定在最寬〉)。

那個狀態放在 `ISourceModeStore`，跟 `ICaptureSeparatorStore` / `ICapturePreviewStore`
同一族，但它是唯一**有 setter** 的:另外兩個是設定頁寫、頁面讀，這一個是頁面自己寫。
所以那句「第三項出現時收成泛型 `ISettingValue<T>`」沒有照做 —— 收進去等於讓另外兩個
也長出 setter。

誰訂閱事件、誰不訂閱，是這裡唯一容易寫錯的地方:

- **清單頁與擴展同壽，所以訂閱 `ShowSourceChanged`** —— 那條路連「在預覽頁上切的」也收得到，
  收到就更新選單那一項的字並換掉每一列的 `Details`。它的項目快取鍵也帶上這個值。
- **預覽頁與記下並預覽頁是清單裡每個項目各建一個的短命物件，一律不訂閱** ——
  長壽事件抓著短命物件會一路累積死掉的訂閱者(跟它們不訂閱 `repository.Changed`
  是同一個理由)。改成在 `GetContent()` 當下讀一次狀態，反正 CmdPal 導覽過去就會取內容。
- **刪除頁跟著狀態走，但沒有切換鍵。** 它的詳細窗格要重讀就得重建整份清單，而那一頁的
  第一列是「刪除全部」(見〈「刪除全部」排第一的代價〉)，不值得為了一個切換鍵多一條
  讓焦點跳到那一列的路。

順帶一提，加這一項時第一版把它排在預覽頁命令陣列的第二個，結果把別的命令從 `Ctrl+Enter`
上擠掉了 —— 那個位置的規則見〈兩個位置鍵:預覽頁與記下並預覽頁刻意相反〉。

#### 為什麼不是就地改 `Details.Body`

那樣更省，而且 `Details.Body` 的 setter 確實會發出屬性變更通知 —— 但**跨進程時那條路是斷的**,
實測結果是值改到了、畫面不動，要重新進入清單頁才看得到。

原因在 SDK 的 `IDetails` 沒有宣告成可觀察介面。CmdPal 的 `DetailsViewModel` 因此是全專案唯一
用執行期型別測試(`model is INotifyPropChanged`)決定要不要訂閱的，而那個 QI 過不了
out-of-process 邊界;`BaseObservable.OnPropertyChanged` 又把例外整個吞掉，失敗完全無聲。

`ICommandItem` 相反 —— 它在 IDL 裡就繼承 `INotifyPropChanged`,`CommandItemViewModel`
對它是無條件訂閱。所以要通知 CmdPal「這一項變了」，走 `ListItem` 的屬性一定收得到，
走 `Details` 的屬性則不一定。

<a id="empty-content"></a>

### 清單頁的空白提示有兩種

「資料夾裡真的沒有筆記」與「有筆記但查詢沒命中」是兩件事，空白提示要分開講。
CmdPal 的 `ShowEmptyContent` 只看篩完的項目數是不是零，**不看搜尋框裡有沒有字**
(`ListViewModel`:IsInitialized、FilteredItems.Count 為零、不在載入中)——
所以「資料夾裡有幾百則筆記、打一個搜不到的字」也會走到空白提示。那時候說
「還沒有任何筆記」會讓人以為筆記不見了(真機重現過)。

所以清單頁依查詢就地切換那一列的文案:有查詢字而零命中時說「找不到符合的筆記」,
否則才是「還沒有任何筆記」。就地改 `Title` / `Subtitle` 即時生效 —— `ICommandItem`
是無條件訂閱那條路(見〈原始文字模式〉)，不必重建內容。

那一列的命令直接掛快速記下頁(`IPage`,CmdPal 會導覽過去)，所以空白狀態的 Enter
真的能帶使用者去記下第一則 —— 而不是給了指示卻按下去沒反應。

## 清單與詳細窗格

<a id="section-not-grouping"></a>

### 分節標頭:`Section` 不是分組鍵

`IListItem` 有一個 `Section` 屬性，名字看起來像「這一列屬於哪一組」。**它不是。**

CmdPal 的清單是**扁平**的 —— `ListViewModel.FilteredItems` 是一個
`ObservableCollection<ListItemViewModel>`,`ListItemsView.xaml` 直接 `x:Bind` 過去，
整棵 `src/modules/cmdpal` 裡 `CollectionViewSource` / `GroupStyle` / `IsSourceGrouped`
一次都沒出現過。所謂的分節標頭就是**那個扁平集合裡的一列**，由型別判斷挑出來:

```csharp
// Microsoft.CmdPal.UI.ViewModels/ListItemViewModel.cs
private ListItemType EvaluateType()
{
    return Command.IsSet
        ? ListItemType.Item
        : string.IsNullOrEmpty(Section) ? ListItemType.Separator : ListItemType.SectionHeader;
}
```

`Command.IsSet` 就是 `Model.Unsafe is not null`。也就是說 **`Section` 只有在那一列
沒有命令的時候才會被讀**;有命令的列一律是 `ListItemType.Item`,`Section` 的值
被讀出來之後不做任何事 —— 不參與過濾、不參與排序、不參與搜尋評分。

**在一個有命令的 `ListItem` 上設 `Section`，畫面上什麼都不會發生，而且不會有任何錯誤。**

#### 怎麼認出來

CmdPal 主頁的「結果 / 已釘選 / 命令」就是它自己插進去的 command-less 列。在 UIA 樹裡
兩者長得不一樣，一眼可辨:

```
ListItem: ' ListItemViewModel'          ← 標頭列:名字是空的,而且沒有 Group 子節點
  Text: '結果'
ListItem: 'Inkling'                     ← 普通項目:有 Group
  Group: 'Inkling'
    Text: 'Inkling'
```

**有沒有 `Group:` 子節點，就是「這一列有沒有命令」的外顯特徵。** Inkling 的每一列都有。

#### 我們踩到的

`DeleteNotesPage` 與 `QuickCapturePage` 都在有命令的列上設過 `Section`
(「動作」/「不是 Inkling 建立的」/「記下」/「已經記過的」)，而且**文檔與手動驗證清單
照著寫了斷言**。2026-08-22 的實機驗證發現那些標頭從來沒有出現過 —— UIA 樹裡沒有、
截圖上也沒有。那幾處賦值是死碼。

**六處賦值連同那五條資源字串已經整個刪掉**，而不是留著等哪天做標頭 —— 留下來的話，
下一個人讀到程式碼會以為畫面上有標頭，而那正是這一輪查了很久才發現的誤會。
刪除頁區分「外來 / 自己的」現在只靠排序(外來排前面)與圖示(`Icons.External`),
兩者都是實際看得到的。

#### 要真的做出標頭的話

照 CmdPal 內建計算機擴展的形狀，自己多插一列:

```csharp
new ListItem(new NoOpCommand()) { Title = title, Section = title, Command = null! }
```

三個代價要先算清楚:

- **0.11 的 toolkit 沒有 `Separator` / `Section` 這兩個現成類別**(只有不相關的
  `ISeparatorContextItem` / `ISeparatorFilterItem` 投影)，要自己刻。
- 標頭列是**真的一列** —— 佔一個索引、28px 高、不可選取。`VersionedItemsCache` 的鍵
  與[〈「刪除全部」排第一的代價〉](#delete-all-first)那套「第一列是什麼」的分析都要重算。
- 這條路在 out-of-process 擴展上**沒有實測過**。`IListItem.get_Section` 的 proxy 與 vtable
  兩側在安裝版都掃得到(所以屬性本身跨得過邊界)，但「擴展送一列 command-less 的項目過去、
  CmdPal 把它畫成標頭」這件事本身還沒有在真機上跑過。真要做，先花五分鐘驗掉。

**在沒有做這件事之前，任何文檔都不該斷言 Inkling 的頁面上有分節標頭。**

<a id="details-width"></a>

### 詳細面板寬度固定在最寬

詳細窗格固定是**寬**(清單:詳情 = 1:1)，沒有設定項，也沒有快速鍵。清單那一邊只有
標題與一行摘要，寬一點也不多給什麼資訊;右邊是筆記本文，窄一檔就多折斷幾十行，看原始文字
時特別有感。

**能給的就只有這麼寬。** 寬度來自 `IDetails.Size`，而 CmdPal 只認
`Small / Medium / Large`，對應 3:1 / 2:1 / 1:1(`DetailsSizeToGridLengthConverter`);
自由拖曳它自己也沒做 —— 整個介面裡連一個 `GridSplitter` 都沒有。

**`Size` 一定要明著寫成 `ContentSize.Large`。** 那個列舉的 0 是 `Small`,`new Details()`
不設就是**最窄**那一檔(實測過)。而且它連事後補救的機會都沒有:`Size` 不走屬性變更通知，
是 `DetailsViewModel.InitializeProperties` 經由 `IExtendedAttributesProvider.GetProperties()`
讀一次就定了，只有換上新的 `Details` 物件才會重讀。

**這裡曾經是可調的，後來整個拿掉。** 原本有一個三檔循環的 `Ctrl+D`，選好的檔位存回
`settings.json`，設定頁還有一個對應的下拉選單 —— 兩邊改的是同一個值，所以得雙向同步:
一個 `IDetailsWidthStore` 窄介面、一個 `DetailsWidthChanged` 事件、provider 那條
「寬度變了就叫設定頁重讀」的訂閱，加上手動驗證清單裡整整一節的回歸測試。實際使用永遠
停在最寬，那些程式碼只是在維護一個沒有人用的檔位，於是連同設定項一起移除。
空出來的 `Ctrl+D` 後來給了刪除，中間拿掉過，現在又回到刪除身上 ——
那一段來回見〈清單頁的快速鍵〉。

舊 `settings.json` 裡的 `Inkling.DetailsWidth` 鍵留著不管:`Settings.Update` 只認得
自己註冊過的鍵，多一個孤兒鍵不會有任何影響，不值得為它寫一次遷移。

<a id="list-shortcuts"></a>

### 清單頁的快速鍵

鍵位全部收在 `src/Inkling/Shortcuts.cs`(CmdPal 自己的擴展也是這個形狀 ——
每個擴展一個 `KeyChords.cs`)。原則是**能少一個修飾鍵就少一個**:這幾個動作每天按，
`Ctrl+X` 比 `Ctrl+Shift+X` 順得多。但「哪些 `Ctrl+字母` 可以拿」要先看誰已經佔著。

**一、搜尋框(WinUI `TextBox`)的標準編輯鍵，一個都不能碰。** 清單頁的焦點永遠在搜尋框上，
而 CmdPal 在 `ShellPage_OnPreviewKeyDown` 就把鍵送去比對快速鍵
(`TryCommandKeybindingMessage` → `CheckKeybinding`)—— 那是 **tunneling** 階段，
比 `TextBox` 早。綁走等於從搜尋框拿掉:

| 誰的 | 有哪些 |
|---|---|
| `TextBox` | `Ctrl+A`、`Ctrl+C` / `X` / `V`、`Ctrl+Z` / `Y`、`Ctrl+Backspace`、`Ctrl+Delete`、`Ctrl+方向鍵` / `Home` / `End`、`Delete` |
| CmdPal 自己 | `Ctrl+K`(選單)、`Ctrl+Enter`(次要命令)、`Ctrl+,`(設定)、`Ctrl+I`(它自己攔掉的 —— `TextBox` 會拿它插入 tab)、`Alt+Left` / `Alt+Home` / `Alt+F` |

**二、剩下的字母隨我們挑，對得上動作最好。**

| 動作 | 鍵位 | 為什麼是它 |
|---|---|---|
| 編輯 | `Ctrl+E` | E = Edit |
| 新增筆記 | `Ctrl+N` | N = New，各家編輯器共通的手勢;見下面 |
| 原始文字 | `Ctrl+U` | 見〈原始文字模式〉 |
| 在預設編輯器開啟 | `Ctrl+O` | O = Open;剪貼簿記錄擴展的 `KeyChords.OpenUrl` 也是它 |
| 開啟檔案位置 | `Ctrl+L` | L = Location |
| 複製內文 | `Ctrl+Shift+C` | **唯一還帶 Shift 的**，見下面 |
| 刪除 | `Ctrl+D` | D = Delete |

**`Ctrl+N` 是全清單裡唯一跟「選中的那一則」無關的動作**，卻掛在每一則筆記的 `Ctrl+K` 選單上 ——
因為 CmdPal 的快速鍵只認**當下選中項的命令**(`CommandBarViewModel.CheckKeybinding`),
頁面層級沒有掛鍵的地方。代價是選單多一列;換來的是在瀏覽筆記時不必退回主搜尋框才能新增。
它排在筆記自己的動作後面、刪除前面:前面那幾項講的是「這一則」，它講的是「下一則」,
而刪除永遠留在最後。**清單是空的時候按不到**(那時沒有選中項)，但那個情境的 `Enter`
本來就會帶去快速記下頁。

鍵位本身查證過:`TextBox` 沒有拿 `Ctrl+N` 做任何編輯動作，而 PowerToys `main` 的整個
`cmdpal` 目錄裡搜不到 `VirtualKey.N`,XAML 的 `KeyboardAccelerator` 也沒有 `Key="N"`。

**跟 CmdPal 慣例不一致的兩個，是刻意的。** 內建擴展把「開啟檔案位置」放在 `Ctrl+Shift+E`
(`WellKnownKeyChords.OpenFileLocation`，書籤與檔案索引都用它)、把刪除放在
`Ctrl+Shift+Delete`(書籤、計算機、剪貼簿記錄三個都是)。兩個都做過一版，最後為了少按一個鍵
讓位給 `Ctrl+L` / `Ctrl+D` —— 使用者按得最兇的是自己的筆記，不是跨擴展切換。

**複製為什麼留著 Shift。** `Ctrl+C` 拿不得(搜尋框要拿它複製使用者剛打的字)，所以複製
只剩兩條路:借一個沒人要的字母(`Ctrl+B` = Body 試過一版)，或照 CmdPal 的慣例走
`Ctrl+Shift+C`(`WellKnownKeyChords.CopyFilePath`)。**選了後者** —— 那組鍵跟「複製」的
關聯是手指本來就記得的，借來的字母得靠死記，省下的那一個 Shift 換不到。

真要換成單一個 `Ctrl`,B / G / M / R / T 都還空著，改 `Shortcuts.cs` 一行就行。
`Ctrl+Insert`(Windows 的老牌複製鍵)則刻意不碰:沒查證到 WinUI 的 `TextBox` 吃不吃它，
吃的話就等於又拿走搜尋框的一個複製鍵;而且筆電鍵盤上的 `Insert` 常常要配 `Fn`。

順帶一提:**同一個項目的選單裡撞鍵不會報錯**,CmdPal 用 `TryAdd`，第二個被靜靜丟掉
(只在它自己的 log 留一行 warning，我們看不到)。加新鍵位時自己對一遍上面那兩張表。

<a id="secondary-command"></a>

#### 兩個位置鍵:預覽頁與記下並預覽頁刻意相反

`Enter` 與 `Ctrl+Enter` 跟上面那張表**不是同一種東西**。上面那些是命令自己綁的
`RequestedShortcut`，跨頁一致;這兩個是**位置鍵** —— 底部工具列固定有主命令與次命令兩顆
按鈕，坐上去的是誰只看命令的排序，跟它自己綁了什麼鍵無關。結果之一是同一個命令可能有
兩個鍵能觸發(預覽頁的編輯:`Ctrl+E` 與 `Enter`)，而這也是為什麼底部按鈕上寫的是
`Ctrl+⏎` 而不是那個命令自己的鍵。

**「第幾個」的算法兩種頁面不一樣**，這是最容易寫錯的地方:

| 頁面型別 | 主命令(`Enter`) | 次命令(`Ctrl+Enter`) |
|---|---|---|
| `ListPage` 的一列 | 那一列自己的命令 | **`MoreCommands[0]`** |
| `ContentPage` | **`Commands[0]`** | **`Commands[1]`** |

現在的樣子，決定它的是**使用者進到那一頁時的下一步**:

| 畫面 | `Enter` | `Ctrl+Enter` | 為什麼 |
|---|---|---|---|
| 清單頁的一列 | 預覽 | 編輯 | 先看，要改再改 |
| 預覽頁 | **編輯** | 完成(收起面板) | 是在清單裡**找到了某一則**才進來的，下一步多半是改它 |
| 記下並預覽頁 | **完成(收起面板)** | 編輯 | 剛打完字回頭看一眼，下一步是收工 |

也就是說兩個 `ContentPage` 的前兩項**順序相反**:預覽頁是「編輯、完成」，記下並預覽頁是
「完成、編輯」。看起來不對稱，但兩頁都把 `Enter` 給了自己那條動線上真正的下一步。

##### 中間繞過的那一圈

這一頁的兩顆鍵改過三次，值得留著免得再繞:

1. **`Enter` 編輯 / `Ctrl+Enter` 複製內文** —— `Ctrl+Enter` 不是刻意給複製的，它只是排在
   第二個而已。同一個 `Ctrl+Enter` 在清單頁是編輯、在這一頁是複製，得記兩套。
2. **`Enter` 完成 / `Ctrl+Enter` 編輯** —— 為了讓 `Ctrl+Enter` 三頁同義，把「完成」排到第一個。
   一致性有了，但**預覽頁的 `Enter` 變成「把面板整個收掉」**:使用者剛在清單裡搜到那一則、
   按 `Enter` 進來看，再按一次 `Enter` 東西就沒了，得重新叫出 CmdPal 再搜一次。
   而「進來看一眼之後想改它」是這一頁最常見的下一步，卻要換一個鍵。
3. **`Enter` 編輯 / `Ctrl+Enter` 完成(現在)** —— 第 2 點那個代價比「`Ctrl+Enter` 得記兩套」
   大。改回來之後 `Ctrl+Enter` 坐的是「完成」而不是第 1 點那個順位撿到的複製內文，
   兩顆按鈕仍然是同一組動作的兩個入口。誤按 `Ctrl+Enter` 會收掉面板，但那要多按一個修飾鍵。

順帶的結果:**複製內文只剩 `Ctrl+Shift+C`** —— 它本來就是那個鍵，只是第 1 點那一版在預覽頁上
順帶多了一個 `Ctrl+Enter`。編輯則有三條路:`Enter`、`Ctrl+E`、`Ctrl+K` 選單。

`Inkling：刪除筆記` 那一頁跟這一切無關:它的 `Enter` 是「刪除，先問一次」、`Ctrl+Enter` 是
「直接刪」，那兩個鍵對應的是使用者進到那一頁的兩種心情(見〈刪除頁的兩個鍵位〉),
不是同一個動作的兩種入口。

加新命令時的規矩:**不要插進 `ContentPage` 前兩個位置**，也不要插進清單頁
`MoreCommands` 的第一個。

<a id="ctrl-d-roundtrip"></a>

#### `Ctrl+D` 兜了一圈回來

這一列的歷史值得留著，免得下次又繞一次:

1. **`Ctrl+Delete`** —— 錯的。那是搜尋框的「刪右邊一個詞」，見上面第一條。
2. **`Ctrl+D`** —— 能用，但後來整個拿掉了。當時的理由是「清單頁是拿來找筆記的，把一個
   不可逆的動作綁在搜尋框上按得到的鍵位上，換來的方便配不上誤觸的代價」，刪除因此只留在
   `Ctrl+K` 選單裡，連續清理請去 `Inkling：刪除筆記` 那一頁。
3. **`Ctrl+Shift+Delete`** —— 跟三個內建擴展一致、也難誤按，但每次刪都要按三個鍵。
4. **`Ctrl+D`(現在)** —— 「搜到某一則，順手刪掉」是清單頁上真實存在的動線;為此跑去
   另一頁還得在那裡再搜一次(那一頁只搜標題)，繞得比省下來的多。誤觸的顧慮沒有消失，
   而是靠兩道防線扛:**一定會跳確認框**，而且刪掉的檔案**進資源回收筒**。

`Inkling：刪除筆記` 那一頁**沒有**跟著綁 `Ctrl+D`:那裡的 `Enter` 與 `Ctrl+Enter`
本來就是刪除，再多一個鍵只會讓語意打架 —— 清單頁的 `Ctrl+D` 是「會先問一次」,
那一頁的次要命令卻是「不問」。

## 編輯與表單

<a id="edit-form"></a>

### 編輯表單

表單是 Adaptive Cards，能調的東西比想像中少。下面四件事都是繞出來的:

- **游標落在哪一格，由欄位順序決定。** CmdPal 進表單頁後會聚焦卡片裡第一個可聚焦的控件
  (`ContentFormControl.FindFirstFocusableElement`)，而 Adaptive Cards 既沒有 autofocus
  也沒有 tabIndex。所以**編輯**時內文排在標題前面 —— 進來就是要改內容，標題頁首已經寫著了;
  **新增**時維持標題在前，因為是先想標題。
- **落在那一格的哪個位置，則完全指定不了 —— 一律是開頭。** 這條查過了，不要再試:
  CmdPal 只做 `focusableElement?.Focus(FocusState.Programmatic)`
  (`ContentFormControl.OnFrameworkElementLoaded`)，而 Adaptive Cards 的 `Input.Text`
  沒有任何 caret / selection 屬性。擴展手上只有 `TemplateJson` 與 `DataJson`,
  碰不到底下那個 WinUI `TextBox`，而它被程式化聚焦時游標固定在索引 0。
  想要「一進來就在內文最後」只有兩條路:改 PowerToys 本身，或在表單上另外加一個空的
  「追加」框(空框的開頭就等於結尾，存檔時接到內文尾端)。後者評估過 ——
  不值得為了偶爾的追記，讓每次編輯都多一塊多行輸入框。現在的做法是把 `Ctrl+End` 講出來:
  編輯表單底部有一行淡色提示，新增時不顯示(內文本來就是空的，沒有差別)。
  那行字是 `TextBlock` 不是 `Control`,`FindFirstFocusableElement` 不會選中它，
  所以擺進去不影響焦點還是落在內文框。
- **新增時內文框預填 5 行空白。** 渲染器對多行輸入只設 `AcceptsReturn` 與 `TextWrapping`,
  完全不碰高度，所以空的內文框就是一行高，看起來像只能寫一行。卡片沒有「幾行高」這種屬性，
  唯一撐得開它的就是內容本身。代價是 placeholder 不再顯示(框裡有東西了)，而空行有機會
  被存進檔案，所以新增的存檔路徑會 `Trim()`;編輯時不動，那些空行是使用者自己的排版。
  (`Container` 的 `minHeight` 配上 `height: stretch` 試過，不成立:輸入框連同它的標籤會被
  包進一個 `StackPanel`，多出來的空間留在容器裡，框還是一行。)
- **沒有 `Ctrl+S`，存檔是 Tab 到「儲存」按 Enter。** 表單的輸入值只活在 CmdPal 進程裡的
  `RenderedAdaptiveCard.UserInputs`，擴展唯一的取值管道是 CmdPal 反過來呼叫
  `SubmitForm(inputs)` —— 就算把 `Ctrl+S` 綁到擴展的命令上，手上也沒有使用者剛打的字。
  CmdPal 端唯一的鍵盤提交路徑是 `ContentFormControl.OnFormKeyDown`，只認 Enter、只在單行
  輸入框裡有效，而且 0.11.11762.0 還沒有這段程式碼。真要 `Ctrl+S` 得改 PowerToys 本身。
- **存完不會自己回上一頁 —— `CommandResult.GoBack()` 在安裝版上不動。**
  `NoteFormContent.SubmitForm` 當時有一個 `AfterSave` 屬性，對編輯回傳 `GoBack()`、
  對新增回傳 `GoHome()`(那個屬性後來連同新增那條路一起改掉了，見下一條)。
  2026-08-22 實測:新增存完**確實**回到主頁，編輯存完**停在編輯頁不走**(等五秒也一樣),
  只有底部的 InfoBar「已儲存：<標題>」會出現。同一個 `SubmitForm`、同一個回傳路徑，
  差別只在回傳哪一種 `CommandResult` —— 所以不是我們的程式沒走到那一行，是 `GoBack`
  本身沒有被處理。這跟 `CommandResult.GoToPage` 是同一類的空殼
  (見 [CLAUDE.md](../CLAUDE.md) 硬規則 8)。**能用的只有 `GoHome` / `Dismiss` /
  `KeepOpen` / `Confirm` / `ShowToast`。**
  `main` 的 `ShellViewModel.UnsafeHandleCommandResult` 裡是有 `case CommandResultKind.GoBack`
  的，但那是 `main`;byte-scan 對這個 NativeAOT 影像證否不了
  (見〈確認框的按鈕沒有顏色…〉那節的教訓)，所以結論以實機行為為準。

  **處置(2026-09-03 起):跟新增一樣回 `Dismiss`，存完收面板。**
  `GoBack()` 不動、`GoToPage` 是空殼，擴展手上能讓使用者離開表單的回傳值只剩
  `GoHome` 與 `Dismiss` 兩個，而 `GoHome` 會把人丟回主搜尋框。

  <a id="save-always-dismisses"></a>
  **這一條 2026-09-03 改過，舊版的處置是「留在原地 + 底部 InfoBar」。**
  當時的理由寫著「卡片上還壓著使用者剛打的字，所以絕對不能收面板」—— 但那對**新增**
  一樣成立，而新增選的是收面板。同一個 `SubmitForm` 的兩條路，用同一個理由得到相反的
  結論，那個理由就分不開它們。真正的成因是 `GoBack()` 不動之後**只在「停在原地」與
  「`GoHome()`」之間二選一，沒有把「收面板」放進來比**;於是繞路的結果被寫成了設計。
  現在的判準回到 `Feedback` 型別註解那一句:**使用者接下來還要不要看著這個面板。**
  表單填完按儲存就是收工，改一則既有的跟新建一則沒有差別。

  跟著改掉的:卡片底部那行提示不再講「存檔之後這一頁會留著，按 Esc 回上一頁」
  (三份 `.resx` 的 `FormCaretHint`，現在只剩 `Ctrl+End` 那一句);
  存檔失敗仍然走 `Stay` —— 失敗要留在表單上，使用者剛打的字不能跟著面板一起消失。

  **什麼變了才該重新考慮**:CmdPal 讓 `GoBack()` 真的能用的那一天。那時「存完回到
  上一頁」比「收面板」更好 —— 從清單裡找到一則、改完、回清單繼續看，是最自然的動線。
  **重點是回傳值不要說謊**:留著一個看起來會導頁、實際上不會的 `GoBack()`,
  下一個人只會以為畫面沒回上一頁是別的地方壞了。同一輪把
  `CapturedNotePage` 失敗路徑上那個 `GoBack()` 也拿掉了 —— 那顆按鈕寫著「回上一步」
  而按下去什麼都不會發生，「唯一的重試路徑」其實從來沒通過，現在改成就地「再試一次」
  (放掉一次性旗標 + `RaiseItemsChanged`，讓 `GetContent` 重新寫一次檔)。
- **新增與編輯的成功提示走不同通道，而且非分開不可。**
  兩條路本來都發 `ToastStatusMessage`(底部 InfoBar + InfoBadge)，編輯回 `KeepOpen()`、
  新增回 `GoHome()`。2026-08-23 的全量驗證抓到:**新增那條完全看不到提示** ——
  檔案確實建立了(所以 `Show()` 一定執行過)，但 CmdPal 的 status InfoBar 綁在當下
  那一頁的 view model 上，`GoHome()` 導覽時把它一起拆掉了。Enter 之後
  400 / 900 / 1500 / 2500 ms 四次截圖都沒有徽章;編輯那條(`KeepOpen()`)則看得見。
  也就是說**訊息發得出去，是導覽把它吃掉的**。

  **處置:新增改回 `CommandResult.ShowToast`，編輯維持 `ToastStatusMessage`。**
  判準就是 [CLAUDE.md](../CLAUDE.md) 硬規則 8 的那一句 ——
  **「使用者接下來還要不要看著這個面板」**:填完整張表單按儲存就是收工，不需要，
  而 toast 是唯一能在面板消失之後還留在畫面上的通道;編輯反過來，卡片上還壓著使用者
  剛打的字，而收工那條路的 `Dismiss()` 會把它們連同面板一起收掉。
  (這裡以前寫的是「toast 一搶焦點主視窗就自我隱藏」—— 假的，見
  [〈toast 不會把面板關掉〉](#toast-does-not-steal-focus);要顧的是 `Result`，不是通道。)
  **不要為了「留住徽章」把新增改成 `KeepOpen()`**:那會讓人存完卡在表單上，
  而他下一步是收工，不是繼續改。
- **收工那幾條的 `ToastArgs.Result` 全部是 `Dismiss()`，沒有例外。**
  同一輪順手把 `QuickCaptureCommand` 從 `GoHome()` 一起改過來 —— 它本來是唯一的例外，
  而那正是新增這條路照抄錯的來源。選 `Dismiss()` 的理由是它講得出意圖 ——
  存完就是回去做原本的事，留一個主搜尋框只是多一次 Esc。

  ⚠ 當時還量到「`Dismiss` 與 `GoHome` 在畫面上分不出來」，解釋是「toast 一搶焦點主視窗
  就自我隱藏」。**那個解釋後來被推翻了**([〈toast 不會把面板關掉〉](#toast-does-not-steal-focus)),
  而在設定頁上 `GoHome` 明確是「面板留著、切回主頁」——
  也就是說那個「分不出來」跟現在的模型對不起來，**還沒重測**。
  結論(收工一律 `Dismiss()`)不依賴它，所以沒有跟著改。

  `QuickCaptureCommand` 舊註解寫著 `GoHome()` 是為了「離開快速記下頁，否則搜尋框
  還留著剛打的字」,**那句話把兩個機制講混了**:清空那個搜尋框的是
  `OnCaptured`(接到 `QuickCapturePage.ClearQuery`)，跟回傳值無關。
  現在四條收工路徑(新增表單、快速記下、記下並預覽頁的「完成」、隨手草稿的存檔與
  捨棄變更)一致回 `Dismiss()`，不再有例外可以照抄錯。

<a id="edit-form-enter"></a>

### 編輯表單的 `Enter` 是一顆什麼都不做的命令

`ContentPage` 的底部工具列主命令就是 `Commands[0]`(位置鍵，見〈兩個位置鍵〉)。
編輯頁一度只掛一項「在預設編輯器開啟」，於是 **`Enter` 就是它**:焦點在**單行**的標題欄時
按 Enter 是很自然的「送出」手勢，結果卻是跳去外部編輯器、面板被 `Dismiss` 收掉，
卡片上打過而還沒儲存的字全部消失。實機重現過。

當時的結論寫著「Enter 本身收不回來」,**那句話是錯的**，而且同一個 repo 裡就有三個反例:
`ScratchpadPage` 刻意把無害的「捨棄變更」放在 `Commands[0]`、把跳外部推到 `Commands[1]`;
`NewNotePage` 與 `InklingSettingsPage` 根本不設 `Commands`。`Commands[0]` 一直都是可控的。

現在第一顆是一顆 `AnonymousCommand(() => { })` 配 `CommandResult.KeepOpen()`，名字叫
「繼續編輯」—— 誤按 Enter 的代價變成零。它**不能**是「儲存」:底部工具列走的是無參數的
`ICommand.Invoke()`，拿不到使用者剛打的字(同一件事 `ScratchpadFormContent` 已經記過),
放上去只會是一顆假按鈕。真正的儲存只有卡片裡那顆 `Action.Submit` 一條路。
`tests/Inkling.Tests/PageCommandOrderTests.cs` 有一條斷言把 `Commands[0]` 釘住 ——
這個位置一動，`Enter` 的意思就變了，而那不會有任何編譯或執行期訊號。

<a id="scratchpad-no-autosave"></a>

### 隨手草稿為什麼沒有自動儲存

隨手草稿要的是「打開就接著上次寫，不用管存檔」。前半做得到，後半做不到 —— 而這一節存在的
理由，是免得下一個人再花一輪去試。

**擴展拿不到使用者正在打的字。** 表單的輸入值只活在 CmdPal 進程裡的
`RenderedAdaptiveCard.UserInputs`，唯一的取值管道是 CmdPal 反過來呼叫 `SubmitForm(inputs)`。
Adaptive Cards 沒有任何值變更的回呼，沒有失焦事件，也沒有 `Ctrl+S`(把鍵綁到擴展的命令上，
命令被呼叫時手上一樣沒有那串字)。詳見上面〈編輯表單〉最後那一條。

**唯一收得到即時輸入的地方是搜尋框**(`DynamicListPage.UpdateSearchText`)，快速記下靠的
就是它。但那是**單行**的 —— 隨手草稿不能換行就失去意義，所以這條路對它不成立。順帶查了
安裝版有沒有「擴展把字塞回搜尋框」的路:`set_SearchText` / `get_SearchText` /
`UpdateSearchText` 在 `Microsoft.CmdPal.UI.exe` 都掃得到(UTF-8)，但那不改變「搜尋框是
單行」這件事，所以沒有繼續追下去。

於是取捨是這樣定的:

- **CmdPal 內存檔是明著的動作，而且盡量短**:`Tab` → `Enter`，兩鍵。存完回
  `CommandResult.Dismiss()`,**面板自己收掉** —— 寫下來的東西通常就是那一趟的全部目的，
  留一個面板在畫面上只是多一次 `Esc`。
- **真的要自動儲存就跳出去**:`Ctrl+O`(以及位置鍵 `Ctrl+Enter`)用系統預設編輯器打開
  同一個 `scratchpad.md`，存檔由那個編輯器負責。

幾個跟著來的、看起來像小事但都咬過人的決定:

- **`Ctrl+O` 那個命令一定要 `Dismiss`。** `OpenUrlCommand` 預設是 `KeepOpen`(toolkit 原始碼),
  留著的話使用者在外部編輯器改完回到 CmdPal，那張卡片還停在跳出去之前的舊值，
  再按一次儲存就把外部的修改整個蓋掉。收起來之後下次打開會重新 `GetContent()` 讀檔，
  看到的才是編輯器那一版。**這是這一頁唯一會靜靜吃掉使用者資料的路。**
- **`Name` 要自己換掉。** 底部工具列那兩顆按鈕顯示的是命令的 `Name`(不是選單項的 `Title`),
  而 `OpenUrlCommand` 帶的是 toolkit 自己資源檔的 `"Open"`。隨手草稿把它放在 `Commands[1]`,
  那正是工具列的位置 —— 實機驗證時抓到按鈕上是一個英文的 Open。`ShowFileInFolderCommand`
  早就為了同一件事這樣做。
- **底部工具列放不了「儲存」，不管多想這麼做。** 那兩顆按鈕走的是 `ICommand.Invoke()`,
  沒有參數 —— 跟上面同一條限制。放上去會是一顆存不了東西的假按鈕。`Commands[0]`(`Enter`)
  因此是**「捨棄變更」**:存檔成功本來就會自己關掉面板，所以還會走到那一顆的只剩「不想存」
  那一種情形，名字要照那個講。沒有沿用別頁的「完成」—— 那個字在這裡會被讀成「存檔並結束」,
  而它一個字都不會存。講「變更」而不是「草稿」也是刻意的:丟掉的是**這一趟的編輯**,
  不是檔案裡那份草稿。實務上很難誤按:焦點在文字框裡時 `Enter` 是換行，碰不到工具列，
  而 `Tab` 的第一站是「儲存」。
- **存檔成功時發 `CommandResult.ShowToast`，而不是狀態訊息。** 判準是〈記下之後要不要
  先看一眼〉那一條:**使用者接下來還要不要看著這個面板**。存完就收工，所以不必看 ——
  那正是 toast 唯一合適的時機(它是唯一能在面板消失之後還留在畫面上的通道;
  狀態訊息畫在面板上，面板收了就跟著沒了)。
  「面板消失」單獨拿來當回饋不夠:它說不出存進去的是什麼。
  存檔**失敗**那條路留在原地(`KeepOpen`)，不然使用者會以為存起來了然後把視窗關掉。
- **存檔後刻意不叫 `RaiseItemsChanged`。** 重新取內容會重建整張卡片，而卡片顯示的本來就是
  剛存進去的東西。(存完就 Dismiss 之後這一條更像是防呆，但留著 —— 哪天改回 `KeepOpen`,
  漏掉它就是「使用者接下來打的字被沖掉、游標跳回開頭」。)
- **框的高度只能靠內容撐開。** 渲染器對多行輸入只設 `AcceptsReturn` 與 `TextWrapping`,
  完全不碰高度。隨手草稿整頁只有這一個框，所以讀進來的內容會在尾端補空行補到 12 行;
  存檔路徑 `TrimEnd` 把補的空行去掉，不會累積。因此 `placeholder` 也就永遠不會顯示
  (框裡固定有東西)，乾脆不設。
- **卡片上除了那個框跟一顆按鈕，什麼都沒有。** 「沒有自動儲存」一度寫成卡片底部一行淡色
  提示，拿掉了:存完面板就自己收掉，使用者實際感覺到的是「打完按兩下就結束」,
  不需要一段免責聲明;而底部工具列本來就把「在預設編輯器開啟」跟它的鍵位印在畫面上。
  多一行字的代價是寫字的地方變小，那正是這一頁唯一在賣的東西。
- **換行一定要正規化。** Adaptive Cards 的多行輸入框送回來的換行是**裸 CR**(底下那個
  WinUI `TextBox` 的行為)。原樣落地的話，`Ctrl+O` 打開會看到擠成一行的一大塊字 ——
  而那正是我們拿來替代自動儲存的那條路。`Newlines.ToLf` / `ToCrlf` 是筆記與草稿共用的
  同一份實作(從 `NoteFile` 裡抽出來的)。

**隨手草稿刻意不是一則筆記**:它沒有標題、沒有 id，而且會被整段反覆覆寫 —— 那三件事正好是
`Note` 的全部意義。硬做成筆記的話，清單裡會永遠多一列標題在跳動的半成品，搜索也會一直
撈到它。所以它是筆記資料夾根目錄下的一個固定檔名(`ScratchpadStore.FileName`)，由
`FileSystemNoteRepository` 認得並跳過 —— **而且只認最上層那一個**，子資料夾裡同名的檔案
照常是筆記，規則要講得出口才不會變成無聲吃掉檔案的黑魔法。同一個判斷也用在 watcher 上:
草稿每存一次就寫一次檔，不擋掉的話每一個開著的清單頁都會白重掃一遍。

<a id="settings-two-entries"></a>

### 設定頁有兩個入口，而且只有一個會自己更新

同一份設定，CmdPal 讓使用者從兩個地方看到:

| 入口 | CmdPal 怎麼拿 |
|---|---|
| 主搜尋框在「Inkling」那一列按 `Ctrl+K`(或 `Ctrl+Enter`)→ 設定 | 我們放在**頂層那一列**的 `MoreCommands` 裡的頁面，每次導覽進去都重建 viewmodel |
| 設定 → Extensions → Inkling | `ICommandSettings.SettingsPage`,**整個 CmdPal 生命週期只初始化一次** |

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
而**擴展發不出那個事件**:`RaiseSettingsChanged()` 是 `internal`，唯一的呼叫者是
使用者按下 Save 時走的 `SettingsForm.SubmitForm`。

結果就是:表單送出、檔案也存了，那一頁卻停在啟動時的值。

修法是**自己實作 `ICommandSettings`**(整個介面只有 `SettingsPage` 一個成員),
把 `InklingSettingsPage` 交出去，發 `ItemsChanged` 的權力就回到我們手上。
兩個入口共用同一個頁面實例，所以看到的永遠一致。

那個頁面因此**不能跟著 `ProviderState` 重建** —— CmdPal 在 provider 剛連上時就把
`Settings` 讀走了，換了實例它不知道，只會繼續用手上那個。

#### 送出表單之後也要 `Refresh()`，而且是每一次

卡片是**建構時**就把值烤進 `DataJson` 的(`FormContent` 沒有別的傳值管道)，而上面那條
「只初始化一次」的路代表 CmdPal 不會因為導覽進頁面就重新 `GetContent()`。
所以只要漏掉一次 `Refresh()`，那張卡片就永遠停在 provider 剛連上時的值。

實際踩到過:分隔符改成 `##`、檔案也存了、快速記下也確實照 `##` 切，可是設定頁**每次打開
都顯示 `;;`**。當時 `Refresh()` 只掛在另一個設定的事件線上，新加的分隔符沒接上。

**比顯示錯更糟的是它會把值吃回去。** 卡片上壓著的過期值，在下一次送出時會被當成使用者
的輸入寫回設定 —— 只改資料夾按一次儲存，就足以把 `##` 默默還原成 `;;`。

所以 `OnSettingsApplied` 一進來就 `Refresh()`，排在「資料夾沒變就 return」的前面，
不分欄位、不比對新舊。表單裡按「瀏覽…」選完資料夾那條路也會走到這裡 ——
**而且它只走這裡**。整個 `src/Inkling` 只有一個 `Refresh()` 呼叫點
(`InklingCommandsProvider.cs:186`)，兩條路都靠 `SettingsManager.Apply` → `Applied` 事件
接過去。`InklingSettingsForm.Browse` 以前另外自己叫過一次，同一次挑選把卡片重建兩遍，
**那一次已經拿掉**(理由寫在 `InklingSettingsForm.cs:156-159`:兩邊分開的話，哪天事件
那頭壞了也只會壞掉一半，反而更難查)。

**加新設定項時記得這條** —— 忘了不會有任何錯誤訊息，只會安靜地顯示舊值。

<a id="settings-form-custom"></a>

#### 表單也是自己的

頁面的內容不是 toolkit 的 `Settings.ToContent()`，而是自己寫的一張 Adaptive Card
(`InklingSettingsForm`)。三個理由:

1. **toolkit 的卡片放不下「瀏覽…」按鈕。** 設定項只能一格一格排下去。
2. **欄位名根本不會顯示。** 它把 `Label` 塞進卡片的 `title`，而 `Input.Text` 沒有那個屬性;
   真正會顯示的 `label` 它拿去放 `Description`。結果每個欄位頭上頂著一整句說明，
   看不到「筆記資料夾」這種短名字。
3. **送出之後它固定 `GoHome`**，而我們每一條路都得留在原地(理由見下面那一小節)。

代價是存檔那條路要自己接:值交給 `SettingsManager.Apply`，由它存檔並發出
`Applied`(provider 拿去比對資料夾，順便叫設定頁重讀)、`CaptureSeparatorChanged` 與
`CapturePreviewChanged`(快速記下頁跟著變)。
toolkit 的 `Settings.RaiseSettingsChanged()` 是 `internal`，本來就叫不動。
標籤、說明、選項仍然只有 `SettingsManager` 那一份，表單只負責畫。

`Apply` 對資料夾欄位有兩道防線，回傳值讓表單決定怎麼跟使用者講:
**相對路徑整筆拒絕**(它會對著擴展 COM server 進程的 CWD 解析，筆記落在意想不到的位置),
表單留在原地什麼都不存;**完整但還不存在的路徑照存**(repository 第一次存檔時會建),
但當場提示 —— 打錯一個字就靜靜換了資料夾，看起來會像「舊筆記全部消失」。

<a id="settings-save-feedback"></a>

#### 存成功就帶一則 toast 回主頁，存不成才留在原地

| 送出的結果 | 通道 | 回傳值 | 使用者看到 |
|---|---|---|---|
| 正常存檔 | `ShowToast` | `GoHome()` | toast「設定已儲存」+ 面板切回主搜尋框 |
| 存了但資料夾還不存在 | `ShowToast` | `GoHome()` | toast 帶著那個路徑 + 面板切回主搜尋框 |
| 相對路徑(整筆拒絕) | `ToastStatusMessage` | `KeepOpen()` | 底部 InfoBar + 徽章，表單留著讓你改 |
| 寫不進 `settings.json` | `ToastStatusMessage` | `KeepOpen()` | 同上 |

**兩個通道不能亂配，配錯的代價是訊息根本不會出現。** `ToastStatusMessage` 畫成的
InfoBar 綁在當下這一頁的 view model 上，`GoHome()` 導覽走的時候會連同訊息一起拆掉 ——
成功那兩條以前正是這個組合，所以「設定已儲存」與「已儲存 —— 這個資料夾還不存在…」
**一個字都沒有真的出現在畫面上過**(跟[〈編輯表單〉](#edit-form)最後那條是同一個機制)。
2026-08-23 在同一張卡片上 A/B，同樣 800 毫秒取樣:

| 回傳值 | UIA 樹上讀到 |
|---|---|
| `KeepOpen()`(相對路徑那條) | `StatusBar` +「沒有儲存 —— 筆記資料夾要填完整路徑…」+ 徽章計數 1 |
| `GoHome()`(正常存檔那條) | **一個字都沒有** |

`CommandResult.ShowToast` 是**獨立視窗**，導覽拆不掉它 —— 要跨頁活下來只有它。

<a id="toast-does-not-steal-focus"></a>

#### toast **不會**把面板關掉 —— 硬規則 8 的前提是假的

`CommandResult.ShowToast` 一直被當成「會關面板的那一種」，理由是「toast 搶焦點 →
CmdPal 主視窗一失焦就自我隱藏」。**那個理由是錯的**，而且錯了將近十天，期間長出了
一整套繞路的設計。2026-08-23 分兩輪量掉:先是設定頁，再是清單頁的複製與刪除。

發出提示的當下同時量兩個視窗(`GetForegroundWindow` + `IsWindowVisible`，行程先
`SetProcessDPIAware()`，否則座標會差一個縮放倍率):

| 量的路徑 | 頁面型別 | toast | 主面板 | 收尾 |
|---|---|---|---|---|
| 設定頁存檔 | `ContentPage` | 可見、**前景=False** | 可見、前景、切到主頁 | `GoHome()` |
| 清單頁 複製內文 | `ListPage` | 可見、**前景=False** | 可見、前景、**停在清單頁** | `KeepOpen()` |
| 清單頁 刪除單則(過確認框) | `ListPage` | 可見、**前景=False** | 可見、前景、**停在清單頁** | `KeepOpen()` |

toast 那個視窗是 `WS_EX_TOOLWINDOW | WS_DISABLED`(`WS_DISABLED` 代表它不收輸入),
**它從頭到尾拿不到前景**。對它 `PrintWindow` 印得出訊息那張圖，所以也不是「有視窗但沒畫」。

決定面板去留的是 **`ToastArgs.Result`**，不是 toast 本身:

| `Result` | 面板 | 停在哪 |
|---|---|---|
| `KeepOpen()` | 開著、前景 | 原本那一頁 |
| `GoHome()` | 開著、前景 | 主搜尋框 |
| `Dismiss()` | 收起來 | 下次叫出來是主頁 |

**`ToastArgs` 的預設 `Result` 是 `Dismiss`** —— 把它 `new` 一個出來讀屬性讀到的
(`Result.Kind = Dismiss`)。所以 `CommandResult.ShowToast("訊息")` 那個字串簡寫
**是會收面板的**，而它看起來完全不像。這是這一節裡最容易再踩一次的地雷。

**toast 畫在面板外面，而且在下方。** 同一次量到的幾何:面板 `1320,684` 起 `1200x720`
(底邊 y=1404),toast `1818,2005` 起 `204x75`。**重疊為零** —— 這一點跟底部的
`ToastStatusMessage`(InfoBar 橫在面板底部，會壓住內容)剛好相反，選通道時是個真的差別。

##### 原始證據怎麼會是反的

硬規則 8 的來源是 2026-08-13 的 `0bb731a`(`fix: stop the palette from closing after a delete`)。
翻出那個 commit 的 diff:當時**確實**是 `ShowToast` 配 `Result = KeepOpen()`,
而 commit message 寫著每刪一則面板就關一次。**同一條路 2026-08-23 重測，面板穩穩留著**,
而且 `Microsoft.CommandPalette` 的版本一個字都沒變(前後都是 `0.11.11762.0`)。

也就是說當年那次不是版本差異，是**看到面板關掉就回頭推論「焦點被搶走」** ——
推論沒有量過，而錯的推論被寫成了硬規則，再從硬規則長出 `FlashTag`、
兩個 `ContentPage` 的靜默成功路徑，以及三條「失敗時順手把面板收掉」。
真正的成因至今不明(最可能是當時某處用了字串簡寫、吃到預設的 `Dismiss`,
但那個版本的程式碼已經證實不是，所以只能存疑)。

**方法論上的教訓跟〈已知落差〉那條 `set_DefaultButton` 是同一個**:
從現象反推機制，推出來的東西要當成待驗證的假說，不能直接寫成規則。
差別是那一條的誤判來自 byte-scan 的證否，這一條來自肉眼觀察的歸因 ——
兩個都便宜、都看起來很有說服力、都錯了。

**什麼變了才該重新考慮**:CmdPal 換版本之後，toast 視窗的樣式位元(`WS_DISABLED`)
可能會變 —— 那是整個結論的支點。重驗一次只要
`pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "...|toast"`，那個動作現在會把
兩個視窗的可見、前景與幾何一起印出來。

<a id="feedback-channels"></a>

#### 通道的分工:`Feedback` 的三個方法

上面那段量測把「toast 會不會關面板」這件事從**機制**降級成**選擇** —— toast 拿不到前景，
`ToastArgs.Result` 想給什麼就給什麼。而選擇一多，分工就會漂:2026-08-23 中間有一版
把複製與刪除失敗改成 toast 配 `KeepOpen`，結果同一個情境(留在原地 + 說一句話)
一半走 toast、一半走底部的 `InfoBar`,**分界線講不出來**。

所以規則收成一句話:**通道由面板去留決定**，而且寫進 `src/Inkling/Feedback.cs`:

| 收尾 | 方法 | 畫在哪 |
|---|---|---|
| 留在原來那一頁 | `Feedback.Stay(訊息)` | 面板底部的 `InfoBar` + 計數徽章 |
| 收起面板(收工) | `Feedback.Done(訊息)` | toast |
| 切回主搜尋框 | `Feedback.Home(訊息)` | toast |

理由不是機制而是各自的壽命:**`InfoBar` 跟著頁面走**，導覽或關閉會把它連同那一頁拆掉;
**toast 是獨立視窗，活得比頁面久**。所以「還在這一頁」用前者、「要離開了」用後者，
兩邊都不會出現「訊息發了但看不到」。

**這三個方法之外不准直接碰那兩個 API。**
`grep -rn "ShowToast\|ToastStatusMessage" src/` 應該只命中 `Feedback.cs`。
把「訊息」與「收尾」綁成同一個呼叫，是因為配錯的兩種錯法**都完全靜默**,
而且都真的發生過:

1. **`InfoBar` 配導覽 = 訊息一個字都不會出現。** 新增筆記表單與設定頁存檔各中過一次，
   兩次都撐了很久 —— 檔案存好了、程式碼跑到了、沒有例外，看起來就像「本來就沒有提示」。
2. **`CommandResult.ShowToast("字串")` 附帶收面板。** 它吃 `ToastArgs` 的預設 `Result`,
   而那個預設是 `Dismiss`。刪除失敗三條路中過，而註解還把它合理化成「可以接受」。

`FeedbackTests` 釘住三個方法回傳的 `Kind`。`Home` 那一條同時證明了 `Result` 是**明著給**
的而不是撿到預設值 —— `GoHome` 不是任何東西的預設。

**唯一的例外是跳到外部程式的成功路徑**(在編輯器開啟、開啟檔案位置):那時焦點剛給了
編輯器或檔案總管，**一個字都不說**，跳出來的那個視窗本身就是回饋。失敗才講，而那時
沒有外部視窗跳出來、面板還在前景，所以走 `Stay`。見[〈跳出去之後回得到哪一頁〉](#open-external-return)。

**什麼變了才該重新考慮**:`InfoBar` 開始壓到讓人讀不下去(它橫在面板底部，長訊息會吃掉
兩三列的高度)，或者 CmdPal 讓 toast 可以定位到面板旁邊。那時要動的是這張表，
不是個別的呼叫端。

<a id="settings-no-separator"></a>

### 設定卡片上沒有分隔線，因為畫不出來

三個設定項之間曾經宣告 `"separator": true` 配 `spacing: default`(8px)。
**那條線從來沒有被畫出來。** 2026-08-22 實機截圖逐列掃過，而且是在**淺色**主題下 ——
所以不是「深色背景上的半透明黑看不見」那種解釋。

Adaptive Cards 的分隔線粗細與顏色來自 hostConfig 的 `separator` 區塊，而 **CmdPal 沒有給
擴展任何碰 hostConfig 的入口**:我們手上只有 `TemplateJson` 與 `DataJson`。
查不出為什麼沒渲染，也改不動它。

處置是**把那個宣告刪掉，改用間距**(`spacing: medium`,20px)。留著一個不生效的宣告
跟〈分節標頭〉是同一類問題:下一個人讀到程式碼會以為線是別的地方弄丟的。
`default` 的 8px 是配合線才夠的 —— 少了線，上一項的說明會直接黏著下一項的標籤，
看不出哪句話屬於哪個欄位;`medium` 之後三個區塊在截圖上清楚分開。

**什麼變了才該重新考慮**:CmdPal 開放 hostConfig，或它自己換了一份會畫線的預設值。

<a id="browse-button"></a>

#### 資料夾旁邊的「瀏覽…」

按下去開的是系統的選資料夾對話框(`IFileDialog` + `FOS_PICKFOLDERS`，見 `FolderPicker`)。
擴展是個**沒有視窗**的 out-of-process COM server，所以有三件事跟一般 app 不一樣:

- **對話框跑在自己的 STA 執行緒上。** `Show` 會擋到使用者關掉對話框為止，而呼叫端那條
  執行緒是 CmdPal 的(`ContentFormViewModel.HandleSubmit` 裡的 `Task.Run`),
  不能讓它在那邊等。`SubmitForm` 因此立刻回 `KeepOpen`，選好之後才用回呼把路徑送回來。
- **選好就直接存，不等使用者再按一次「儲存」。** 對話框一拿到焦點，CmdPal 主視窗就會把
  自己藏起來(`MainWindow` 的 `Deactivated` → `HideWindow`，沒有開關可以關掉),
  表單跟著一起消失 —— 那時候還壓在表單裡的值，使用者既看不到也按不到。
- **對話框不掛 owner，所以它在工作列上有自己的按鈕。** 這是刻意的:那顆按鈕是「對話框
  萬一沒被拉到前景」時唯一的退路。**不能拿 CmdPal 的視窗當 owner** —— `IFileDialog` 會
  `EnableWindow(owner, FALSE)`，而那個視窗馬上就要自己藏起來，對話框的下場只能靠運氣。
  (代價:那顆按鈕的圖示固定是套件的 `Square44x44Logo` —— 工作列按鈕的圖示擴展改不了。)

對話框**開不起來**(CoCreateInstance 失敗、`Show` 回傳錯誤、STA 執行緒拋例外)也會
用 InfoBadge 告訴使用者，細節進 DiagnosticLog —— 之前只有 log 留一行字，而它預設是關的，
使用者按「瀏覽…」的體驗就是「什麼都沒發生」。按取消不算失敗。

還有一個 Windows 本身的限制:只有前景進程開的視窗搶得到焦點，而我們這個 COM server
從頭到尾沒收過使用者的輸入。不管的話對話框會開在 CmdPal 後面，使用者只看到工作列閃一下。
`FolderPicker` 因此會去找「屬於自己、而且看得見」的那個頂層視窗(平常一個都沒有),
再 `SetForegroundWindow` 把它拉到前面;拉不動就退回 `BringWindowToTop` /
`SwitchToThisWindow`，再不行才輪到工作列那顆按鈕。

這條路實測過:把 `ForegroundLockTimeout`(這台機器是預設的 200000ms)重新武裝之後
—— 也就是模擬「使用者剛剛才點過東西」—— 對話框仍然被拉到了前景。

<a id="blank-markdown-removed"></a>

#### 表單後面那塊空白已經拿掉了(而且它八成從來沒生效過)

設定頁的表單後面曾經掛著一塊**空的** `MarkdownContent`，用途是擋「背景的設定視窗被拉到
前面來」。`ContentFormControl` 載入後會自動聚焦第一個輸入欄位，而我們每次送出表單都得叫
CmdPal 重讀(上一節)—— 重讀等於控件重建、再觸發一次 `Loaded`。當時的理由是 CmdPal 只在
「頁面上唯一的控件」時才聚焦，湊滿兩塊內容就不會聚焦，也就不會搶焦點。

那段理由是照 CmdPal `main` 的原始碼寫的:

```csharp
element.Loaded -= OnFrameworkElementLoaded;

if (!ViewModel?.OnlyControlOnPage ?? true) return;   // 不是唯一控件就不聚焦
```

**但 `OnlyControlOnPage` 在安裝版裡不存在。** byte-scan 過
`Microsoft.CmdPal.UI.exe`(0.11.11762.0):同一條路上的 `ContentFormControl`、
`OnFrameworkElementLoaded`、`FindFirstFocusableElement`、`ContentPageViewModel` 全都掃得到，
只有 `OnlyControlOnPage` 沒有，`OnlyControl` / `SoleControl` / `SingleControl` 各種變體也都沒有。
也就是說安裝版的自動聚焦沒有那道判斷，湊第二塊內容擋不掉任何東西 ——
這塊空白在使用者實際跑的版本上八成從來沒起過作用。

**這是第二次踩到同一個坑**:照 `main` 的原始碼寫進文檔，而安裝版根本沒有那段程式
(第一次是 fallback 排序，見 [CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉)。
從原始碼得到的結論一定要 byte-scan 對照安裝版再寫。

會觸發的情境本身也沒了。當初每按一次 `Ctrl+D`(那時面板寬度可調)就重讀一次表單，人卻在
主視窗翻筆記，背景視窗因此一直跳。現在 `Refresh()` 只有**一個**呼叫點
(`InklingCommandsProvider.cs:186`)，而走到它的只有兩條路，都源自使用者在設定表單上的
操作(按儲存、或按「瀏覽…」選完資料夾)——人本來就在設定頁上。唯一還構得成
問題的組合是「CmdPal 設定視窗停在 Inkling 那一頁，同時從主搜尋框進設定頁按儲存」,
兩邊共用同一個頁面實例，背景那個會跟著重建。

拿掉之後換回來的是**打開設定頁時游標會自動落在第一個欄位**，不必先點一下或按 Tab ——
那是每次都付得到的好處，而上面那個組合很少見。萬一它真的又開始搶焦點，原因就在這裡;
補救方式是讓 `GetContent()` 多回傳一塊內容，但**先確認安裝版到底有沒有那道判斷**。

**說明文字現在是每個欄位下面各一塊，沒有例外。** 卡片最上面曾經還有獨立的一行提醒
(「換資料夾不會搬動已經寫好的筆記」)，那是這塊 markdown 搬進卡片時留下的位置 ——
但那句話講的只有筆記資料夾一個欄位，結果它變成唯一上下都有說明的欄位。已經併進
`NotesDirectorySetting` 的說明裡。要加類似的話就加在對應設定項的說明上，
不要在卡片頂上再開一塊。

順帶一提，說明文字為什麼全部寫在卡片裡而不是另外一塊 markdown:內容區塊之間有大約 32px
收不掉的間距 —— `ContentPage.xaml` 的 `ItemsRepeater` 用 `StackLayout Spacing="8"`,
每塊內容自己又有 `Margin="0,4,4,4"` 與 `Padding="12,8,8,8"`。說明擺前面是一段跟表單斷開的
旁白，擺後面更像掉在半空(兩種都做過)。而且 markdown 那條路**沒有淡色可用**,
CmdPal 的 `MarkdownThemes` 只設定了字級與 inline code;卡片裡的 `TextBlock` 才有
`isSubtle` 跟 `size: small`，也才貼得住它說明的那個欄位。

## 刪除

<a id="delete-page"></a>

### 刪除為什麼是一頁

`Inkling：刪除筆記` 按下去不會刪任何東西，它進到一個清單頁，把即將被刪的檔案列出來。

原因是這個動作的範圍比它的名字大得多。掃描的是筆記資料夾底下(含子資料夾)**所有的
`.md`**，而且**不分辨檔案是不是 Inkling 寫的** —— 那是列清單時刻意的設計(外來的 `.md`
也要看得到)，但放到批次刪除上就變成一把沒有握把的刀:資料夾要是被指到既有的
Obsidian vault、docs 目錄、或任何有 `README.md` 的專案資料夾，一次就全掃走了。
預設的 `%OneDrive%\Inkling` 是專用資料夾，所以預設設定沒有這個問題 —— 風險是改過路徑
之後才出現的。

一個確認框放不下這些。它只有一行標題與一行說明，而使用者真正需要看見的是「到底是哪些檔案」。
所以那一頁長這樣:

| 區塊 | 內容 |
|---|---|
| 動作 | `刪除全部 N 則`(副標是資料夾路徑);有外來檔案時多一列 `只刪 Inkling 建立的 M 則` |
| 不是 Inkling 建立的 | 排在最前面 —— 那正是最需要先看到的一批，圖示也不一樣 |
| Inkling 筆記 | 其餘的，副標是相對於筆記資料夾的路徑，子資料夾一眼看得出來 |

清單超過 `MaxResults` 被截斷時，最後一列會明講**沒列出來的一樣會被刪**。

「只刪 Inkling 建立的」那一列是這個做法真正換來的東西 —— 命令的形狀下根本放不下第二個動作。

順帶修掉一個小毛病:原本沒有筆記時只能回一個 toast，而 toast 的預設收尾是把整個 CmdPal
關掉，使用者只看到面板一閃就沒了。頁面有 `EmptyContent`，空的情況本來就有地方講。

資源回收筒不是絕對的保險:檔案在網路磁碟、沒有回收筒的裝置上，或大過回收筒配額時，
Windows 會直接永久刪除，而我們設的 `FOF_NOCONFIRMATION` 正好把那個警告框壓掉了。
這件事寫在頁面的詳細窗格裡。

<a id="delete-keys"></a>

### 刪除頁的兩個鍵位

每一則筆記上 `Enter` 是「刪除，但先問一次」,`Ctrl+Enter` 是「直接刪」。同一個動作給兩條路，
是因為使用者進到這一頁時的狀態有兩種:一種是心裡有數要清掉哪幾則(連著按 `Ctrl+Enter` 最快),
另一種是邊看邊決定(每一則都想再確認一次)。底部工具列會把兩條路都寫出來，不必記。

**例外只有一個**:不是 Inkling 建立的檔案，兩條路都跳確認框。那是別的工具寫的、或使用者
自己丟進資料夾的，誤刪的代價跟自己記的筆記不一樣，不給它「跳過確認」這個選項。那一列的
`Ctrl+Enter` 因此照實寫成「刪除」而不是「直接刪除」，副標講明為什麼。

預覽降到選單第二項。這一頁 `ShowDetails` 是開的，右邊詳細窗格本來就在顯示標題與內文，
預覽頁多出來的只有 Markdown 渲染 —— 不值得佔著前面那兩個鍵位。

<a id="delete-all-first"></a>

#### 「刪除全部」排第一的代價

進到這一頁時預設選中的就是它。這個位置以前還多帶一個代價:刪掉一列之後焦點會跳回第一列，
也就是這一列身上 —— 「想刪下一則而順手按 Enter」因此有機會落在「刪除全部」上。

**那條路 2026-08-25 已經堵掉了**，而且成因跟這一節原本寫的不一樣:跳回第一列不是
CmdPal 的既定行為，是我們自己每次重建清單都給一批全新的項目物件造成的。
現在筆記那幾列走 [〈刪掉一則之後，選取落在哪〉](#selection-survives-rebuild)講的槽位分配，
刪完選取落在下一則，根本不會經過這一列。

⚠ 這一節原本寫著「安裝版沒有 sticky selection,`_stickySelectedItem` /
`firstUsefulIndex` / `ensureSelectionVisible` 全都掃不到」。**那個推論是錯的** ——
那三個全是欄位名、區域變數名與參數名，NativeAOT 一律裁掉，掃不到不構成證據。
改掃方法名之後 `TrySetSelectionAfterUpdate` / `PushSelectionToVm` /
`SuppressSelectionChangedScope` 全部命中。詳見同一節。

防線照樣留著，不因為少了一條路就拆:它一定會跳確認框、**確認框的焦點落在「取消」上**、
確認框標題明著寫「刪除全部 N 則筆記?」、刪掉的檔案進資源回收筒。
**而連著按 `Ctrl+Enter` 清理的那條路本來就踩不到它** —— 那一列沒有次要命令，
焦點跳過來時 `Ctrl+Enter` 什麼都不會發生。想連續刪就用 `Ctrl+Enter`，這是它比 `Enter` 更安全
的地方(雖然聽起來反過來)。

第二道防線是 2026-08-22 才確認存在的:那兩列有設 `IsPrimaryCommandCritical`，而這一頁
以前寫著那個旗標在安裝版沒有效果 —— **是錯的，它有效**(見〈確認框的按鈕沒有顏色，
也沒有「危險」樣式〉)。所以「順手按 Enter 落到刪除全部、再順手按一次 Enter」的結果是
**取消**，不是確認。

<a id="selection-survives-rebuild"></a>

#### 刪掉一則之後，選取落在哪

**症狀**:在清單頁刪掉一則筆記，選取跳回最上面那一列。刪除頁更明顯 —— 跳回去的那一列
是「刪除全部」。而且不只刪除:別台機器經 OneDrive 同步下來一則、或使用者拿別的編輯器
改了**任何**一則，正在看的那一列一樣會被踢走(真機實測過)。

**根因有兩層，缺一不可。**

一、`RaiseItemsChanged()` 用的是 toolkit 的預設參數 `totalItems = -1`。CmdPal 的
`ListViewModel.Model_ItemsChanged` 只認一個值:

```csharp
RequestFetch(keepSelection: args.TotalItems == IncrementalRefresh, ...)   // IncrementalRefresh = -2
```

其餘任何值都會走 `forceFirstItem: true`，意思是「更新完清單順便選第一列」。
`-2` 不在 SDK 也不在 toolkit 裡，擴展引用不到，所以我們自己寫了
`CmdPalRefresh.KeepSelection` 並在那裡註明出處。

二、**光改參數沒有用。**「保留選取」保留的是「當下選中的那個 view model 還在不在新集合裡」
(`ListItemsView.TrySetSelectionAfterUpdate`)，而 CmdPal 是拿 `IListItem` 的**參考相等**
去查 view model 快取的(`ListViewModel._vmCache`，比較器叫 `ProxyReferenceEqualityComparer`)。
每次重建清單都 `new` 一批全新的 `ListItem`，等於每次都宣告「整份清單換人了」,
那個判斷必然為假。

**修法:三條分配規則。** 實作在 `NoteItemSlots`，清單頁與刪除頁共用。

| 情境 | 規則 | 結果 |
|---|---|---|
| 別則筆記新增、刪除、重新排序、同步 | 內容沒變的筆記**沿用自己上一輪的項目物件，而且一個屬性都不碰**(身分語意) | 選取跟著**那一則筆記**走，即使它換了位置 |
| 某一則從清單上消失 | 它的物件**讓給後繼者**，後繼者原本的物件變孤兒(位置語意) | 選取留在**原位置**，顯示下一則 —— 跟檔案總管刪檔案同一個手感 |
| **某一則的內容變了** | 給它一列**全新的**物件，舊的丟掉 | 那一列失去物件識別(選取可能跑掉)—— **但這是必要的**，見下面 |

前兩條不會互相干擾:刪除走第二條，其餘走第一條。**只做第一條的話刪除仍然跳第一列;
只做第二條(單純按索引重用)的話，同步進來一則排在前面的筆記，使用者正在看的那一列
會默默換成別則** —— 兩種都做出來實測過，那張表是實測的結果，不是推導。

身分認的是 `FilePath` 不是 `Id`，理由與 `Update` / `Delete` 同一條，
見〈[解析一則筆記認的是路徑，不是 `id`](#identity-is-the-path)〉。

<a id="in-place-update-hits-other-pages"></a>

**第三條是拿一個壞掉的畫面換來的:就地更新會打到使用者當下看的那一頁。**

前兩條上線之後冒出一個新症狀:**在編輯頁按下儲存，表單旁邊多出一塊筆記預覽，
底部工具列變成清單那一列的「預覽 / 編輯」，而人明明還在編輯頁上。**

成因是就地更新本身。CmdPal 已經替那一列建好 view model 了，這時候改它的
`Command` / `MoreCommands` / `Details`(三個一個一個關掉測出來的，**每一個都會**),
CmdPal 就把那一列重新渲染出來 —— 而「內容變了」最常見的來源，正是使用者**正在編輯那一則**,
他這時候不在清單頁上。舊的寫法(每次重建都給全新物件)沒有這個問題:全新的物件對 CmdPal
是「新項目」，走的是建 view model 那條路，不是就地更新那條。

**跟時機無關。** 試過「先自己 `GetItems()` 重建、再 `RaiseItemsChanged`」,
讓就地更新發生在 CmdPal 的 fetch 之外 —— 一樣壞。

所以內容變了就換一列新的。代價很誠實:剛編輯過的那一則會失去物件識別，回到清單頁時
選取可能不在它身上 —— **那是這套機制出現之前的既有行為，不是新的退步**。
換來的還有一個好處:內容沒變的列連一個屬性都不設，跨進程通知整批省掉。

`ListItemIdentityTests.ChangedContent_GetsAFreshItem` 與 `UnchangedNotes_AreNotReboundAtAll`
釘著這一條。**不要為了「省一次配置」把它改回就地更新** —— 那個 bug 完全靜默，
而且只在「使用者剛好離開清單頁」時才看得到。

**什麼變了才該重新考慮**:CmdPal 哪天把「就地更新一個非當前頁的清單項」修好
(症狀是渲染到當前頁上)，第三條就可以拿掉，讓內容變動也沿用物件。
判斷方式是拿掉之後跑一次上面那個編輯存檔的手動驗證。

**往前讓槽有一個限制，而且是必要的。** 被刪的是最後一列時沒有「後繼者」，這時往前讓給
前一列 —— 但那會**搶走一個根本沒被刪的項目的物件**。只少一則時無妨(被搶的是使用者
剛按下刪除那一列的前一列，使用者沒選著它);一次少好幾則就不成立，那是批次刪除或同步，
使用者選著的可能是任何一列。所以往前只在「整份清單只少了這一則」時才允許。
踩過的具體形狀:`[A,B,C,D,E]` 只剩 `[A,E]` 時，`C` 的物件會往前搶走 `A` 的位置，
選著 `A` 的人被丟回第一列。

**代價**:讓槽那條路上，一列的內容是「就地寫進一個上一輪坐著別人的物件」，所以
`ApplyNote` 那幾個方法**每一項都要設一遍**(命令、標題、副標、圖示、詳細窗格、
`Ctrl+K` 選單、標籤)，不能假設誰沒變。漏掉一項的症狀是那一列顯示甲、命令卻還綁著乙，
而且完全靜默 —— 在一個刪檔案的頁面上那是最貴的一種 bug。真機驗過刪一則之後再按
`Ctrl+D`，確認框寫的是**新**那一則的標題。

**打字造成的變動刻意不套用。** 搜尋字改了，結果換了一批，選取本來就該回到最上面 ——
`UpdateSearchText` 維持預設參數。快速記下頁整頁都是這種情況(第一列是「記下這句話」,
那才是使用者要按的)，所以那一頁一行都沒改。

**方法論:這一節推翻了一條寫在 CLAUDE.md〈已知落差〉裡的結論。** 那裡曾經斷定
「安裝版沒有 sticky selection」，依據是 byte-scan 掃不到 `_stickySelectedItem` /
`firstUsefulIndex` / `ensureSelectionVisible`。**那三個全是欄位名、區域變數名與參數名** ——
NativeAOT 保留方法名(給 stack trace 用)但一律裁掉這些，所以掃不到是必然的，
跟那段程式碼在不在完全無關。改掃**方法名**之後 `TrySetSelectionAfterUpdate`、
`RequestFetch`、`PushSelectionToVm`、`SuppressSelectionChangedScope`、`ScrollToItem`、
`ResetScrollToTop` 全部命中，`ProxyReferenceEqualityComparer`(類型名)也命中。
這是〈確認框的按鈕沒有顏色〉那條之後**第二次**踩到同一個陷阱，教訓因此可以說得更精確:
**byte-scan 一個識別名之前，先問它是哪一種** —— 型別名與方法名掃得到才有意義，
欄位、區域變數、參數、`const` 一律不要拿來當證據。

**什麼變了才該重新考慮**:CmdPal 哪天把選取狀態開放給擴展(SDK 現在的 `IListItem`
沒有任何選取相關的屬性)，或者上游把「刪掉選中那一列之後選下一列」做進 `ListItemsView` ——
那時這整套槽位分配就可以拆掉，回到每次重建都給新物件的簡單寫法。
判斷方式是掃 `ListItemsView` 裡有沒有「移除後往下選」的方法名。

<a id="no-multiselect"></a>

#### 為什麼沒有多選

CmdPal **沒有多選**:SDK 的 `IListItem` 沒有任何選取狀態的屬性
(`dotnet run --project tools\ApiDump -- ListItem` 只有 `Tags` / `Details` / `Section` /
`TextToSuggest`)，主清單的 ListView 也沒開多選 —— `SelectionMode` 在整份 cmdpal
原始碼裡出現的地方全是 `Single` 或 `None`，沒有任何 `Multiple` / `Extended`。

自己畫一套是做得到的(存一組 id，標記時只換那一列的 `ListItem.Tags`;那條路在安裝版上
確實通，byte-scan 掃得到 `UpdateTags` / `VisibleTags` / `TagViewModel`)，而且**實際做完過**:
`Enter` 標記、`Ctrl+Enter` 刪掉挑好的那批。最後整個移除 —— 換來的東西配不上代價。
連著按 `Ctrl+Enter` 一則一則刪，鍵數跟「挑三則再刪」幾乎一樣，而多選那條路要多帶一組狀態:
標記在頁面上活著、沒有導覽事件可以清、搜尋過濾之後看不見卻還算數(所以確認框非得把標題
一則一則列出來不可)、每一列的命令名要隨挑了幾則而變。實作在 git 歷史裡。

<a id="delete-feedback"></a>

### 刪除完會講一句話，而且留在原地

四條路(單則刪除、刪除全部、只刪 Inkling 建立的、以及三條失敗路徑)現在**全部**走
`Feedback.Stay`:訊息畫在面板底部，面板停在刪除頁或清單頁上。

| 情況 | 訊息 |
|---|---|
| 刪掉一則 | 「已刪除:<標題>」 |
| 刪除全部 / 只刪 Inkling 建立的 | 「已刪除 N 則」 |
| 有幾個檔案刪不掉 | 「已刪除 N 則，M 則刪不掉(檔案可能被其他程式開著)」 |
| 整個操作丟例外 | 「刪除失敗:… —— 請到設定檢查筆記資料夾」 |

**措辭刻意不提資源回收筒。** 網路磁碟與沒有回收筒的裝置上 Windows 是直接刪除
(`SHFileOperationW` 的 `FOF_ALLOWUNDO` 在那裡不生效)，那句話會變成假的。
刪除頁的詳細窗格已經把這個差別講清楚了，確認框也會講 —— 那兩個地方講得起，
因為它們是在動手**之前**。

#### 這一節整個翻過來了，舊版的理由是假規則的遺產

三個刪除命令原本都是回 `ShowToast` 配 `KeepOpen`，註釋寫著「留在清單頁，使用者當場
看到清單真的空了」，而 2026-08-13 觀察到的是刪一則面板就關一次(`0bb731a`)。
當時的結論是「那兩件事湊不到一起，toast 會搶焦點」，於是成功路徑改成**完全靜默**,
並把它合理化成優點:「那一列當場從清單上消失，比什麼訊息都直接」。

**那個歸因是假的**(2026-08-23 在同一條路上量掉了，見
[〈toast 不會把面板關掉〉](#toast-does-not-steal-focus))，而理由跟著垮:

- **刪除是唯一不可逆的動作，卻是唯一不出聲的成功路徑。** 複製、存檔、快速記下都會講。
- **刪完焦點會跳到第一列** —— 視覺錨點沒了，「剛才刪掉的是哪一則」只剩訊息講得出來。
  當時實測刪除頁刪一則，選取直接落到「刪除全部」那一列上。
  (⚠ **這一條後來修掉了**，見〈[刪掉一則之後，選取落在哪](#selection-survives-rebuild)〉:
  焦點現在落在下一則。**但那不構成把訊息拿掉的理由** —— 下面三條都還成立，
  而且「刪掉的是哪一則」本來就只有訊息講得出來，選取落在下一則不會說出被刪那則的標題。)
- **成功靜默、部分失敗才講話**，等於讓「沒聲音」承擔「全部成功」的意思 ——
  那正是這個 repo 在別處剛消滅掉的歧義。
- 「清單會空掉」對**刪除全部**成立，對**只刪 Inkling 建立的**不成立:外來檔案還留在
  清單上，畫面看起來跟「刪到一半失敗」分不出來。

失敗那三條路是另一半，而且錯得更明顯:它們用 `CommandResult.ShowToast("訊息")`
那個字串簡寫，而它吃的是 `ToastArgs` 的預設 `Result`，也就是 `Dismiss`。
於是「使用者看到清單還剩東西，要能立刻知道那不是沒生效」這句寫在註解裡的目的
**自相矛盾**:面板關了，清單根本看不到。

⚠ **`CommandResult.ShowToast(字串)` 是這一節的地雷**:它看起來只是「發個提示」,
實際上附帶收面板。這正是它被關進 `Feedback.cs` 的原因，
見[〈通道的分工〉](#feedback-channels)。

**什麼變了才該重新考慮**:連刪好幾則時底部那條 InfoBar 會一直出現、徽章一路累加，
而它壓著約 80px 的清單。真的礙到掃清單的話，可以考慮只在**單則**刪除時講
(批次那條清單本來就會大幅改變)—— 但別回到「全部靜默」，那是上面整段推翻掉的東西。

<a id="toast-status-message"></a>

#### `ToastStatusMessage` 不是那個 toast

名字很像，**但它不開視窗，也不會關掉面板**。上面那條規矩只管 `CommandResult.ShowToast`。

| 用法 | 實際發生什麼 | 面板 |
|---|---|---|
| `CommandResult.ShowToast(字串)` | 送出 `ShowToastMessage`,CmdPal 開一個獨立的 `ToastWindow`，收尾吃 `ToastArgs` 的**預設 `Dismiss`** | **會關掉** |
| `CommandResult.ShowToast(new ToastArgs { Result = KeepOpen() })` | 同一個視窗，收尾自己指定 | **不會** |
| `CopyTextCommand` 的預設 `Result` | 就是第一種 | **會關掉** |
| `new ToastStatusMessage(…).Show()` | 呼叫 `IExtensionHost.ShowStatus`，由 CmdPal 收進 `StatusMessages` | 不會 |

**關面板的是 `Result`，不是 toast。**「`ShowToast` 會關掉面板」這句話只對字串那個簡寫成立，
而它剛好是最順手的寫法 —— 詳見[〈toast 不會把面板關掉〉](#toast-does-not-steal-focus)。

`ToastStatusMessage.Show()` 在 toolkit 裡做的事只有一件:
`ExtensionHost.ShowStatus(Message, StatusContext.Extension)`，然後隔 2500 ms 再 `HideStatus`
(`Duration` 的預設值 2500 是 `new` 一個出來讀到的，ApiDump 只印得出簽章)。
**擴展跑在自己的 COM 進程裡，本來就開不了 CmdPal 的視窗** —— 它能做的只有呼叫 host。

**而那個 `ExtensionHost` 是靜態的，要先接到 host 才有對象可呼叫。** 這一條漏過:
`CommandProvider.InitializeWithHost(IExtensionHost)` 是 CmdPal 把自己交過來的地方，
我們沒有覆寫它，於是 `ExtensionHost.Host` 一直是 `null`,`ShowStatus` 靜靜地什麼都不做 ——
不丟例外、不留痕跡。也就是說**這整條「看得見的失敗提示」曾經一句都沒有真的送到畫面上**,
而文檔(包括這一節)一直把它寫成通的。現在 `InklingCommandsProvider.InitializeWithHost`
會呼叫 `ExtensionHost.Initialize(host)`。

畫出來的樣子也跟原本寫的不一樣。實機截圖(0.11.11762.0):訊息是**一條橫跨面板底部、
壓在內容上方的 `InfoBar`**(左邊一個 ⓘ、右邊一個關閉的 ✕)，外加底部命令列**左下角
頁面標題旁邊的一個計數 `InfoBadge`**(顯示「1」)。不是「一個 InfoBadge，點開才是 InfoBar」。
`ListPage` 與 `ContentPage` 兩種頁面都會出現(清單頁與預覽頁各驗過一次)。

**剛 Reload 完的那幾秒不算數。** 實測那時 `InitializeWithHost` 會被呼叫四次
(CmdPal 對套件安裝事件沒有去重，見〈重新註冊後有時會出現兩個 Inkling〉),
靜態的 `ExtensionHost.Host` 指到最後接上的那一個，而畫面上開著的頁面可能屬於別的實例 ——
訊息因此落在別人的 `StatusMessages` 上，畫面什麼都不會出現。查「提示沒出來」之前，
先讓 CmdPal 靜置幾秒。

安裝版 0.11.11762.0 對得上:`ProcessStatusMessage` 在 `Microsoft.CmdPal.UI.exe` 裡，
`StatusMessagesButton` / `StatusMessagesFlyout` / `MessagesDropdown` 在 `resources.pri`(UTF-16)。
反過來 `main` 那套 toast 改寫(`TransparentWindow` / `TransientSurface` / `ToastPosition`)
安裝版**一個都掃不到** —— 又一個不能照 `main` 寫文檔的例子。

所以存檔提示照樣用它(`NoteFormContent`、`InklingSettingsForm`)。

~~而 `docs/manual-test-checklist.md` 那條「跳出『已儲存』的 toast **並回到上一頁**」
本身就是證據:面板要是被關掉，那一項當初不會通過。~~ **這句推理是錯的，留著當警告。**
`ExtensionHost` 從來沒接到 host(git 全歷史 `-S ExtensionHost.Initialize` 只有修掉它的
那一個 commit)，所以那句「已儲存」根本沒出現過 —— 而「回到上一頁」是
`CommandResult.GoHome()` 做的，跟提示無關。那條驗證項當初會通過，是因為它真正檢查的
只有後半段。**「沒有反例」不能拿來當「有作用」的證據**，尤其是在一個失敗完全靜默的通道上。

<a id="copy-feedback"></a>

## 複製

### 複製完留在原地，回饋是底部那條訊息

刪除的回饋是那一列消失，**複製沒有任何看得見的結果** —— 剪貼簿是隱形的，
所以這條路非講一句話不可。

三個畫面(清單頁、預覽頁、記下並預覽頁)走同一條:`Feedback.Stay`,
訊息是 **「已複製:<筆記標題>」**，畫在面板底部的 `InfoBar` 加一個計數徽章，
面板停在原地。走 `Stay` 而不是 toast 是因為[通道的分工](#feedback-channels)只看
「面板去留」—— 留在原地就是 `Stay`，沒有例外可以挑。

**標題不是裝飾。** 清單頁以前靠「標籤掛在哪一列」講「複製到的是哪一則」,
底部那條訊息沒有位置感，那個資訊只能寫進訊息裡。

沒有內文的筆記講的是「沒有內文可以複製」，而且**不碰剪貼簿** ——
`ClipboardHelper.SetText` 會先 `EmptyClipboard()`，對空筆記按下去等於把使用者剛複製的
東西清掉。那條路不提標題:「已複製:X」講的是「X 進了剪貼簿」，而這裡什麼都沒進去。

#### 這一節兩天內改了三版，值得記著中間那一版錯在哪

原本的做法是在清單那一列右邊打一個 `已複製` 的標籤(`FlashTag`),2.5 秒後自己收掉，
連著一個計時器與兩個欄位;**而預覽頁與記下並預覽頁的成功路徑是完全靜默的**,
理由寫著「那一頁整頁顯示的就是剛複製走的內容，自己會說話」。

兩件事都建立在同一條假規則上:「想留在畫面上就一個 toast 都不能發」
(見[〈toast 不會把面板關掉〉](#toast-does-not-steal-focus))。規則倒了之後兩件事都站不住:

- **靜默那兩頁是真的缺陷。** 頁面顯示什麼跟剪貼簿有沒有寫成功無關 —— 按下去畫面一個像素
  都不變，跟快速鍵壞掉分不出來。那正是當初修「空內文」那條時用的判準，只是成功路徑被漏掉了。
- **標籤本身沒壞，但代價比看起來高。** 實測它佔掉那一列約四分之一的寬度:`rime` 那一列的
  副標從「當前我認為Rime(ht…」被吃到「當前我認為…」。標題不受影響，而詳細窗格照樣顯示
  全文，所以不算嚴重 —— 但它換來的東西(位置感)寫進訊息裡就補得回來。

中間那一版把三頁統一成 **toast 配 `KeepOpen`**。它解掉了上面兩點，但**開了一個更貴的洞**:
「toast」與「留在原地」從此可以任意組合，通道的選擇就沒有規則可循了 ——
同一個情境(留在原地 + 說一句話)一半走 toast、一半走 InfoBar，分界線講不出來。
一天之內就發現那是退步，收斂成三個方法，見[〈通道的分工〉](#feedback-channels)。

**留下來的是「三頁都要講話」與「訊息要帶標題」這兩件事** —— 它們跟通道無關，
是那一輪真正的收穫。`FlashTag` 沒有回來:`ListItem.Tags` 現在只用在雲端硬碟的衝突副本上，
那是持續性的狀態而不是回饋。那條路跨進程仍然是通的(跟 `Details` 正好相反，見
〈為什麼不是就地改 `Details.Body`〉),`ICommandItem` 在 IDL 裡就繼承
`INotifyPropChanged`,CmdPal 對它無條件訂閱，安裝版的 `UpdateTags` / `VisibleTags` /
`TagViewModel` 也都掃得到 —— **這個知識沒有作廢**，下一次需要「不關面板、不重整清單、
就地改一列的狀態」時它仍然是答案。

<a id="confirm-dialog-colors"></a>

### 確認框的按鈕沒有顏色，也沒有「危險」樣式

`ConfirmationArgs` 的全部內容就是 `Title` / `Description` / `PrimaryCommand` /
`IsPrimaryCommandCritical` 四個屬性(`dotnet run --project tools\ApiDump -- ConfirmationArgs`),
**沒有任何樣式或顏色的開口**。那個對話框是 CmdPal 自己 `new` 的 WinUI `ContentDialog`,
擴展只提供文字跟要跑的命令。

`IsPrimaryCommandCritical` 聽起來像「把按鈕標成危險色」，但上游拿它做的唯一一件事是:

```csharp
if (vm.IsPrimaryCommandCritical)
{
    dialog.DefaultButton = ContentDialogButton.Close;   // ← 預設落在「取消」

    // TODO: Maybe we need to style the primary button to be red?
    // dialog.PrimaryButtonStyle = new Style(typeof(Button)) { ... }
}
```

紅色按鈕在 `ShellPage.xaml.cs` 裡是**註解掉的 TODO**，微軟自己也還沒做。所以「刪除」按鈕
沒有紅色、也沒有強調色，這是 CmdPal 目前就長這樣，不是我們漏設什麼 —— 兩個按鈕都是預設樣式。

這一節講的只有**確認框**。`Ctrl+K` 選單裡的那一列是另一回事 —— 那裡有一個真的會變紅的
開關，見下一節〈刪除的紅色只有一個地方碰得到〉。

**但「預設按鈕」那一半是有作用的 —— 這裡以前寫反了。**

這一節曾經斷言「0.11 安裝版連上面那個 `if` 都沒有，整個套件掃不到 `set_DefaultButton`,
所以 `IsPrimaryCommandCritical` 設不設畫面上完全一樣」。**2026-08-22 的實機驗證推翻了它。**
三種確認框各開一次，讀 UIA 樹上的 `[FOCUS]`:

| 確認框 | `IsPrimaryCommandCritical` | 焦點落在 |
|---|---|---|
| 清單頁 `Ctrl+D` 單則 | false | **刪除** |
| 刪除頁 · Inkling 建立的 | false | **刪除** |
| 刪除頁 · 外來檔案(`DeleteNotesPage.cs:197`) | true | **取消** |
| 刪除頁 · 刪除全部 / 只刪 Inkling 建立的(`:271`、`:299`) | true | **取消** |

也就是說旗標**設了就生效**,`DefaultButton` 確實被設成了 `Close`。

**為什麼 byte-scan 會誤判，這件事比結論本身重要。** `Microsoft.CmdPal.UI.exe` 是
**NativeAOT** 影像(`PEHeaders.CorHeader` 是 null、整包沒有 `hostfxr` / `hostpolicy` /
`coreclr`)，裡面的識別名來自被裁過的 AOT metadata。所以
[CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉那套 `#Strings` / `#US` 模型在這個 exe 上
只有一半成立:**命中是硬證據，沒命中不是。** `set_PrimaryButtonText` 掃得到而
`set_DefaultButton` 掃不到，只代表前者被保留、後者被裁掉，不代表那段程式碼不存在。
CLAUDE.md 的〈已知落差〉刻意把這一條留著當反例。

旗標照語意設的規則不變:

| | `IsPrimaryCommandCritical` | 為什麼 |
|---|---|---|
| 刪一則(Inkling 建立的) | **不設** | 有資源回收筒兜底，不值得為此讓每次刪除都多按一次方向鍵 |
| 刪一則(外來檔案，刪除頁) | **設** | 那是別的工具寫的檔案，誤刪的代價不一樣，值得多那一下 |
| 批次刪除(兩列都是) | **設** | 一次動幾十個檔案就該多花那一下 |

**而且既然它真的生效，那張表就不只是「等 CmdPal 更新上來」的宣告，而是現在就成立的行為。**
順帶讓〈「刪除全部」排第一的代價〉那一節的風險降了一級:順手按 Enter 落在「刪除全部」時，
確認框的焦點在**取消**上，再按一次 Enter 是取消而不是確認。

已知的口徑不一:清單頁的 `Ctrl+D` 對外來檔案**不設**(那條路本來就一律先跳確認框),
刪除頁對外來檔案設 —— 兩頁對同一種檔案的 critical 標記不一致，而**現在這個差異是使用者
看得到的**(焦點落在不同的按鈕上)，值得統一。

要注意批次刪除**現在沒有這道防線**(0.11 上 Enter 一樣是確認)，真正的防線是刪除全部那一頁
本身會先列出會刪掉哪些檔案。SDK 也沒有辦法把預設按鈕指定成「確認」—— 上游只有「設成取消」
跟「不設」兩種。

#### 藍色也不行，而且整個對話框我們只碰得到一個字串

紅色是註解掉的 TODO，那**藍色(強調色)呢**?WinUI 的 `ContentDialog` 只有一個機制會把某顆
按鈕變成強調色:`DefaultButton`。而 CmdPal **從來沒把它設成 `Primary`** —— `main` 唯一
設它的地方是 `IsPrimaryCommandCritical` 時設成 `Close`(那會讓「取消」變藍，不是「刪除」),
而 0.11 安裝版連那一行都沒有。也就是說那個對話框裡**兩顆按鈕永遠都是預設樣式**,
紅、藍、任何顏色都不是我們沒設，是那條路整個不存在。`PrimaryButtonStyle` 在整個套件裡
只出現在 `Microsoft.ui.xaml.dll`(框架本身),CmdPal 一次都沒用過。

那個對話框裡**唯一由擴展決定的畫素是主要按鈕上的字**:

```csharp
var name = string.IsNullOrEmpty(vm.PrimaryCommand.Name) ? confirmText : vm.PrimaryCommand.Name;
ContentDialog dialog = new() { Title = vm.Title, Content = vm.Description, PrimaryButtonText = name, ... };
```

`PrimaryButtonText` 就是我們 `PrimaryCommand.Name`。所以真要在那顆按鈕上見到顏色，
只剩一招:把 emoji 放進命令名(`Name = "🗑️ 刪除"`),emoji 字型本身是彩色的。
**現在沒有這樣做** —— 那是一顆彩色圖示，不是一顆紅色按鈕，而且跟整個介面的 Segoe Fluent
單色圖示放在一起會很突兀。要試的話改一行就行，回頭也只是改回來。

**自己畫一個確認畫面也換不到紅色按鈕。** Adaptive Cards 那條路試算過:CmdPal 的 host config
裡 `"attention"` 是 `#FF5555`(安裝版的 exe 裡掃得到 —— 注意掃法:它是 C# 字串常量，
存在 metadata 的 **#US heap,UTF-16**，跟方法名所在的 UTF-8 #Strings heap 不同堆;
照型別名那套 UTF-8 掃法會得到 False，掃字串常量要兩種編碼都掃)，所以**卡片上的文字可以
是紅的**;但按鈕不行 —— AdaptiveCards 的 `Action.Style = "destructive"` 是靠宿主提供
`Adaptive.Action.Destructive` 這個資源鍵去查樣式的(那兩個字串在
`AdaptiveCards.Rendering.WinUI3.dll` 裡)，而 CmdPal 的 `resources.pri` 裡**只定義了
`Adaptive.TextBlock`**，查不到就退回預設按鈕。換句話說:多做一頁、失去「Enter 直接確認」的
手感，只換到一行紅字。不做。

<a id="critical-red"></a>

### 刪除的紅色只有一個地方碰得到

上一節講的是確認框:那裡沒有任何顏色的開口。但**選單裡的那一列有** ——
`CommandContextItem.IsCritical`,SDK 的 IDL 對它的註解就一句話:

```idl
Boolean IsCritical { get; };   // READ: "make this red"
```

CmdPal 拿它做的事是換一整個 `DataTemplate`(`ContextItemTemplateSelector` 挑
`CriticalContextMenuViewModelTemplate`)，圖示、標題、右邊那個鍵位字串三個都套
`SystemFillColorCriticalBrush`。**這條路在 0.11.11762.0 安裝版上是通的**,
不是只有 `main` 有(byte-scan 對照過，兩邊都掃得到):

```powershell
$d = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe"
# Microsoft.CmdPal.UI.exe → ContextItemTemplateSelector / get_IsCritical
# resources.pri(UTF-16)  → CriticalContextMenuViewModelTemplate /
#                           ContextItemTitleTextBlockCriticalStyle
```

設在兩個地方:清單頁的「刪除」，以及刪除頁每一列選單裡的「直接刪除 / 刪除」。

**別跟 `IsPrimaryCommandCritical` 搞混。** 名字像，是兩件事:

| | 屬性在誰身上 | 做什麼 | 0.11 安裝版 |
|---|---|---|---|
| `IsCritical` | `CommandContextItem` | 選單那一列變紅 | **有效** |
| `IsPrimaryCommandCritical` | `ConfirmationArgs` | 把確認框的預設按鈕設成「取消」 | **有效**(2026-08-22 實機，見上一節) |

碰不到的地方，一次講完:

| 哪裡 | 為什麼不行 |
|---|---|
| 底部工具列的按鈕(`Enter` / `Ctrl+Enter` 那兩顆) | `CommandBar.xaml` 裡兩顆都寫死 `SubtleButtonStyle`，沒有 critical 變體。所以刪除頁上那一列的「刪除 ⏎」是白的，同一個命令在 `Ctrl+K` 選單裡卻是紅的 |
| 確認框的兩顆按鈕 | `ConfirmationArgs` 只有四個屬性，紅色在上游是註解掉的 TODO(見上一節) |
| 清單列本身(「刪除全部 N 則」那兩列的圖示) | `ListItem` 沒有 `IsCritical`,glyph 圖示跟著主題前景色走。真要紅只能改成自備的圖檔(`IconHelpers.FromRelativePath` 吃 `.svg` / `.png`,CmdPal 自己就這樣用)，為了兩列多帶一份資產與淺色/深色兩張圖，現在不做 |

## 跳出面板

<a id="open-external-return"></a>

### 跳出去之後回得到哪一頁

`Ctrl+O`(在預設編輯器開啟)與 `Ctrl+L`(開啟檔案位置)都會讓面板從畫面上消失，
但**成因是兩件不同的事**，而它們的後果不一樣:

| | 怎麼消失的 | 面板叫回來之後 |
|---|---|---|
| `KeepOpen` + 外部視窗搶焦點 | 主視窗自我隱藏(`HideWindow`) | **還停在原本那一頁** |
| `Dismiss` | CmdPal 主動收尾 | **回到主頁，搜尋框清空** |

上游 `main` 的 `ShellViewModel.UnsafeHandleCommandResult` 裡，`Dismiss` 那個 case 做的是
`GoHome(withAnimation: false, focusSearch: false)` 再送 `DismissMessage`，而失焦那條路
(`MainWindow_Activated` → `HideWindow`)只隱藏、不 `GoHome`。

**但這件事不能照 `main` 寫。** byte-scan 安裝版 0.11.11762.0:`EndSession` 掃得到，
同一個方法在 `main` 裡呼叫的 `LogSessionDuration` 與同一段的 `preventHideWhenDeactivated`
**兩種編碼都掃不到** —— 也就是說安裝版的 `EndSession` 根本不是 `main` 那個 telemetry-only
的版本，拿 `main` 的控制流去推論會落空。所以結論是**實機量出來的**:

1. 讓面板停在某個子頁(隨便哪個都行)，把焦點交給別的視窗讓它自我隱藏，再按熱鍵叫回來
   → UIA 樹裡 `Button: 'Back'` 還在、placeholder 還是子頁那一句。**導覽堆疊完整保留。**
2. 在筆記預覽頁按 `Ctrl+L`(當時走的是 toolkit 預設的 `Dismiss`)，檔案總管開起來之後
   再叫回面板 → placeholder 變回「搜尋應用程式、檔案和命令...」、`value` 是空的。
   **回到主頁，而且字沒了。**

所以 `Ctrl+O` 與 `Ctrl+L` 現在都是 `KeepOpen`:按這兩個鍵的人是去外面做事，回來多半
還想著同一則筆記，留著那一頁就省掉重新搜尋一次。

⚠ **那個保留有時限。** 同一次驗證裡還撞到另一件事:面板隱藏著擱了幾分鐘再叫回來，
它自己回到了主頁 —— 沒有精確量過門檻，也沒查是哪一段程式碼做的，但足以說明
上面那張表講的是**馬上回來**的情形。這不影響結論(跳出去做事再回來本來就是幾秒到幾十秒
的節奏)，但驗證時中間別插一堆別的操作，不然會看到「`KeepOpen` 也回主頁」而以為改壞了。

這件事以前是**沒人決定過的** —— `OpenUrlCommand` 的預設是 `KeepOpen`,
`ShowFileInFolderCommand` 的預設是 `Dismiss`(`tools/ApiDump` 那條路問不出預設值，是拿
0.11.260520004 的 toolkit 實際 `new` 一個出來讀 `Result.Kind` 讀到的)，於是同一個
`Ctrl+K` 選單裡兩個「跳出去」的鍵行為相反。現在兩邊都顯式指定，見
`OpenNoteFileCommand` 與 `ShowNoteInFolderCommand`。

**例外的判準是「畫面上有沒有一份使用者還能按儲存的副本」，不是頁面名單。**
有的話一定要 `Dismiss`:面板留著，使用者從外部編輯器改完回到 CmdPal 再按一次儲存，
就把外部的修改整個蓋掉(卡片的值是 `GetContent()` 當下烤進 `DataJson` 的，
CmdPal 不會因為視窗重新出現就重新取一次)。收起來之後下次打開才會重讀檔案。

目前符合這個判準的有**兩頁**:隨手草稿，以及**筆記的編輯表單**
(`NoteEditPage`,`85d1dfc` 之後也改成 `Dismiss`)。筆記的預覽頁與清單頁不符合 ——
它們顯示的是唯讀的預覽，沒有這個問題，所以維持 `KeepOpen`。
寫判準而不是列頁面:名單會漂，判準不會。

<a id="open-external-silent"></a>

### 開不起來的時候，以前是完全靜默的

toolkit 那兩個命令**都會吞掉失敗**:

- `OpenUrlCommand.Invoke()` 呼叫 `ShellHelpers.OpenInShell`，那個函式裡有
  `catch (Win32Exception) { return false; }` —— 但 `Invoke` **把回傳值丟掉**,
  無論成敗都回傳自己的 `Result`。
- `ShowFileInFolderCommand.Invoke()` 是 `if (Path.Exists(_path))`，不成立就整段跳過，
  連 `explorer.exe` 都不會叫;裡面那個 `Process.Start` 另外還包著一個空的 `catch`。

實機重現過，製造方式是**把筆記檔在 Inkling 以外改名**(預覽頁持有進入當下那個路徑，
不會跟著 `repository.Changed` 更新，所以那一頁的路徑就失效了)。改之前:按 `Ctrl+O`
UIA 樹**原封不動停在預覽頁**,`toast 視窗:可見=False`，沒有任何程式起來，也沒有任何
訊息 —— 使用者按下去，什麼都沒有發生。`Ctrl+L` 更糟一點:它當時還走 toolkit 預設的
`Dismiss`，所以是「面板關掉了、檔案總管沒開」，看起來跟成功幾乎一樣。

改之後同一個情境:面板留在預覽頁，底部展開一條 InfoBar 寫著「找不到這個檔案 ——
可能在 Inkling 以外被改名或移走了」，左下角一個 InfoBadge，而 `toast` 步驟依然是
`可見=False`(確認走的不是會關面板的那種)。`Ctrl+O` 與 `Ctrl+L` 兩條路都驗過。

**「沒有可以開啟 `.md` 的程式」那條路沒有在真機上重現過**，只從原始碼確認。查證時踩過
一個坑值得記下來:`assoc .md` 回 "File association not found" **不代表開不起來** ——
那個舊命令只看 `HKCR\.md` 的預設值，而 `OpenWithProgids` 底下還有候選程式，
`ShellExecute` 照樣開得起來。實測就是這樣:`assoc` 說沒有關聯，`Ctrl+O` 按下去
VS Code 照開，`DiagnosticLog` 記的是「已交給 shell」。**用 `assoc` 判斷「這台機器沒有
關聯」會得到假的結論**，要看的是 `HKCR\.md\OpenWithProgids` 與 `UserChoice`。

**這條路是整個擴展裡少數「提示真的看得見」的地方**，原因剛好是失敗本身:
沒有任何外部視窗跳出來，面板因此還在前景，`ToastStatusMessage`(底部命令列的
`InfoBadge`)看得見也留得住。所以現在失敗會說話，而且分成兩句 ——「檔案不在了」
跟「沒有程式能開 `.md`」的下一步完全不同。

成功那條路相反，**一個字都不說**:編輯器或檔案總管一起來，面板就被蓋掉了，那時發什麼
都是白費(同一個道理見〈隨手草稿存完就把面板收掉〉那段註解)。跳出來的那個視窗本身
就是最好的回饋。

失敗時一律 `KeepOpen`，連隨手草稿也不例外 —— 面板收掉的話，那則訊息連同
「什麼都沒發生」會一起消失，使用者只會以為編輯器在背景開好了。

## 資料完整性

這一節收的是「使用者的檔案在什麼情況下會被弄壞，以及我們決定怎麼不弄壞它」。
每一條都在真機上重現過壞掉的樣子，再改的。

<a id="identity-is-the-path"></a>

### 解析一則筆記認的是路徑，不是 `id`

`id` 仍然是**筆記的身分**(改標題不重新命名檔案，那條沒有變)。但「清單上這一列
對應磁碟上哪一個檔案」是另一個問題，而那個問題的答案是 <b>`FilePath`</b>。

**為什麼**:`id` 在磁碟上不保證唯一。雲端硬碟的衝突副本是**整檔複製** ——
OneDrive 產生的 `<檔名>-<電腦名>.md` 連 front matter 都一模一樣，同一個 `id`
就這樣出現在兩個檔案上。而「把筆記放進 OneDrive、同步交給它」正是這個擴展的賣點。

以前 `Update` / `Delete` 都經由 `GetById`(=「`GetAll` 裡第一個 id 相符的」)解析目標，
於是:清單列出兩列，**兩列都指向同一份檔案**。實機重現(2026-08-22):選中第二列按
`Ctrl+E`，頁面標題寫著第二列的標題，欄位帶出來的卻是**第一份**的內容;改一下按儲存，
寫進第一份，第二份一個位元組都沒動。刪除同理。

現在:

- `INoteRepository.Update` / `Delete` 直接吃 `Note`，用它的 `FilePath` 定位
  (內部仍會重新查一次磁碟上的最新內容，所以傳一份舊快照進來也安全)。
- `GetById` **從介面上拿掉了**，只留成 `FileSystemNoteRepository` 的 private ——
  它唯一的用途是 `Create` 時的 id 碰撞偵測。留在介面上遲早會有人拿它去解析編輯 /
  刪除的目標，那正是修掉的這個 bug。要重新取內容走 `GetByPath`。
- 預覽頁、記下並預覽頁、編輯頁留的是**路徑**而不是 id。(當時清單頁的 `FlashTag`
  也跟著改成用路徑找那一列;那段程式碼 2026-08-23 已經整組移除，見
  [〈複製完留在原地〉](#copy-feedback)。)

**而且要講出來。** 兩份的標題與副標可能一模一樣，不標記的話使用者根本不會發現多了一份。
`Load()` 掃完之後按 `id` 分組，一組多於一筆就把那幾則標上 `Note.HasDuplicateId`,
清單頁掛一個 `ListItem.Tags` 的「衝突副本」標籤 —— 那條路跨進程是通的
(見[〈複製完留在原地〉](#copy-feedback))。README 的〈同步〉那段跟著改掉了:
以前寫「請在檔案總管裡處理」，那是繞過去;現在是「兩份各自獨立，自己挑一份留下」。

<a id="external-id-shape"></a>

### 「不是 Inkling 建立的」判準是 `id` 的**形狀**

`Note.IsExternal` 以前是 `parsed.Id is null` —— 只要 front matter 裡有 `id:` 這個 key,
就算成 Inkling 建立的。

**而 `id:` 在 Obsidian / Zettelkasten / Hugo 生態裡到處都是。** 實機重現:一個
`id: 202401051200` 的 zettel 被算成我們的，刪除頁顯示「只刪 Inkling 建立的 **2** 則」/
「保留 **1** 則不是 Inkling 建立的」—— 那句「保留」是假的，按下去會刪掉使用者自己的東西
(進資源回收筒救得回來，但畫面上的承諾不成立)。

判準改成 `NoteFileName.IsGeneratedId`:`yyyyMMdd-HHmmss-xxxx`，八位日期、六位時間、
四位**小寫**十六進位。只看形狀，不驗日期真偽 —— 目的是把別人的 id 擋在外面，
而誤判的方向(當成外來檔案)那一邊是安全的。修完同一個資料夾顯示的是 4 / 2。

**外來檔案第一次在 Inkling 裡編輯時會被「接手」，但只在它本來沒有 id 的時候。**
沒有 id 的檔案，它的 `id` 是我們拿路徑算的(`file-<FNV1a>`)—— 那個東西跟著檔名跑，
不是身分，不能寫進檔案。所以 `Update` 會替它產一個真的 id，從此它算我們的。
front matter 裡**已經有別人的 id** 就原樣留著，那個檔案永遠算外來的 ——
覆蓋它等於毀掉使用者的 metadata，而「不認得的東西不要動」比「讓它變成我們的」重要得多。
兩條路都有實機驗證與單元測試。

<a id="strict-utf8"></a>

### 非 UTF-8 的檔案整個跳過，不讀成亂碼

`File.ReadAllText` 的預設解碼器把無效位元組**默默換成 U+FFFD**。於是一個 Big5 / GBK /
Latin-1 的 `.md` 會被讀成一串 �，清單上長成亂碼標題;而使用者一旦在 Inkling 裡編輯它，
那些 � 就被寫回檔案 —— **原始位元組永久消失**，沒有備份、沒有提示，資源回收筒裡什麼都沒有
(實機驗過，前後項目數相同)。順帶還替它塞進了 front matter,`title` 就是那串亂碼。

現在讀檔走 `new UTF8Encoding(false, throwOnInvalidBytes: true)`，失敗就回 null、
計進 `SkippedFileCount`。那條路早就有畫面 —— 清單最後那一列「有 N 個檔案讀不出來」,
訊息也已經是「檔案還在資料夾裡」的口徑，正好對得上(副標補上了「或者編碼不是 UTF-8」)。

**有 BOM 的檔案不受影響**:`File.ReadAllText(path, encoding)` 底下的 `StreamReader` 仍然
先照 BOM 判編碼，UTF-8 / UTF-16 LE / BE 都認得，這個編碼只是「沒有 BOM 時的假設」。
測試裡兩種 BOM 各釘一條。

**取捨**:那個檔案從清單上消失了，而 README 承諾「外來的 `.md` 也要列得出來」。
兩害相權 —— 列出一則永久性地會被自己毀掉的亂碼筆記，比暫時不列它糟得多，
而「有 N 個檔案讀不出來」那一列讓它至少不是無聲消失。

<a id="unreadable-dates"></a>

### 讀不懂的日期原樣留著，而且只認 ISO 8601

`created` / `updated` 以前是 `DateTimeOffset.TryParse` + `InvariantCulture`,
讀不出來就回 null、改用檔案系統時間，而**原始那一行連 `ExtraFrontMatter` 都進不去**
(它在認得的 switch 分支裡就被消化掉了)—— 下一次在 Inkling 裡編輯就把原字串永久覆蓋。

兩種觸發，第二種更糟因為完全無聲:

- `created: 2024-01-05 (approx)` → 編輯一次 → 變成檔案建立時間，原字串消失。
- `created: 05/01/2024`(dd/MM，多數非美式工具的寫法)→ InvariantCulture 讀成 **5 月 1 日**
  → 寫回 `2024-05-01T…`。**日期被默默改掉，而且是永久的。**

現在兩件事一起做:

1. **只認 ISO 8601 的起手式**(開頭是 `yyyy-MM-dd`)。`05/01/2024` 因此讀不出來 ——
   與其猜錯，不如認不出來就別動它。我們自己寫出去的一律是 `yyyy-MM-ddTHH:mm:sszzz`,
   擋不到自己。
2. **讀不出來就把整行原文留在 `Note.CreatedRaw` / `UpdatedRaw`,`Serialize` 原樣寫回去**,
   取代我們自己產的那一行。**不能丟進 `ExtraFrontMatter`** —— 那樣寫出去會變成兩個
   `created:`，而且我們自己的 Parse 下一輪又會把它讀成 null，每編輯一次多一份殘骸。

`UpdatedRaw` 在 `Update` 時會被清掉:`updated` 的語意就是「最後改動時間」，而我們正在改它。
`CreatedRaw` 永遠留著。

<a id="settings-quarantine"></a>

### `settings.json` 壞掉時把它搬走，否則設定會永久性、無聲地卡住

toolkit 的 `JsonSettingsManager` 兩頭都吞例外:

- **讀**:`LoadSettings` 失敗 → 四項設定全部退回預設 → **筆記資料夾變回 `%OneDrive%\Inkling`**,
  使用者的清單換成別的內容。
- **寫**:`SaveSettings` 內部要先 `JsonNode.Parse` 舊內容再合併，解析失敗就走 else 分支
  ——**完全不寫檔，也不丟例外**。於是 `SettingsManager.Save` 回 true、設定頁走成功路徑、
  檔案一個位元組都沒變，而 `ApplyResult.SaveFailed` 那條路**永遠到不了**。

加起來就是「設定頁怎麼改都沒有用，重啟又還原」，而使用者**在 app 裡修不好它** ——
唯一的解是手動去刪那個檔案，而他不會知道要去做。觸發也不需要手改:toolkit 走的是
`File.WriteAllText`,**不是** atomic write(我們自己寫筆記時走 `AtomicFile`，設定檔沒有
這個保護)，寫到一半斷電就會留下半個檔案。

現在三件事:

1. `SettingsManager` 建構子在 `LoadSettings()` **之前**先 `JsonNode.Parse` 試一次，
   失敗就把檔案搬成 `settings.json.corrupt-<時間戳>`。**搬走而不是刪掉** ——
   裡面是使用者設過的東西，壞的可能只有一個字元，手工救得回來。
2. `Save` 寫完**讀回來對一次**(比對筆記資料夾那一項)。對不上就回 false,
   `ApplyResult.SaveFailed` 那條路才不是死的。
3. 隔離掉的話，設定卡片**最上面**多一塊 `attention` 色的警告，寫出被搬走的檔名。
   那是卡片頂上唯一允許出現的區塊(其他說明一律掛在各自欄位下面，見〈設定頁的表單〉)——
   它不是說明而是錯誤，絕大多數時候不存在，而且使用者會來這一頁正是因為「筆記全部不見了」,
   那句解釋必須在他看到資料夾欄位**之前**就讀到。

**那一句也要進 CmdPal 的共用 log**，但發不出去:隔離發生在 `SettingsManager` 建構子裡，
而 `ExtensionHost` 要到 `InitializeWithHost` 才接到 host，那之前 `LogMessage` 靜靜地什麼都不做
(實測確認)。所以 `InitializeWithHost` 在 `ExtensionHost.Initialize(host)` 之後補發一次。

<a id="log-two-channels"></a>

### 診斷 log 有兩個通道，隱私等級不一樣

`DiagnosticLog.Write` 只寫本機的 `diagnostic.log`，而且**預設是關的**(使用者要自己建一個
`diagnostic.on` 才會開始記)。`DiagnosticLog.Failure` 另外送一份給 CmdPal 自己的 log
——那份**永遠開著、所有擴展共用**，而且 **PowerToys 的 Bug Report Tool 會把整個
`%LOCALAPPDATA%\Microsoft\PowerToys\` 打包**，使用者拿去貼在 `microsoft/PowerToys` 的
**公開** issue 上，完全不會經過我們自己的 issue 範本與那裡的遮蔽提醒。

以前十四個 `Failure` 呼叫點有四個直接把**筆記檔案的完整路徑**送進去，另外八個把例外全文
(裡面也有路徑)送進去。那個路徑形如 `<筆記資料夾>\<時間戳>-<標題 slug>.md`，同時帶著
**筆記標題**與(經 `%OneDrive%` 或 `Documents`)**Windows 使用者名字**。

現在簽章是 `Failure(string summary, string? detail = null)`:

- `summary` 進兩個通道 —— **去識別化的失敗種類**，例外只放 `ex.GetType().Name`。
- `detail` 只進本機那一份 —— 路徑、例外全文這些查起來才有用、但不能公開的東西。

實機對照(2026-08-22):共用 log 拿到
`[Inkling] settings.json was not valid JSON; it was moved aside and defaults are in use`,
本機那份同一筆後面接著 ` — <完整路徑>`。

**訊息一律英文。** 這是 log(見 CLAUDE.md〈慣例〉)，而共用那一份會被 PowerToys 的維護者
拿去 triage 別人的 bug —— `[Inkling]` 前綴認得出是誰寫的，訊息本身除了我們沒人讀得懂
就白寫了。十四條 `Failure` 加上其餘的 `Write` 這一輪全部改成英文。

`diagnostic.log` 另外有 **2 MB 的上限**，超過就搬成 `.log.1` 重新開始。清單頁每次重建
(≈每個按鍵)就寫一行，裡面有使用者打過的每一個查詢字串;使用者照排錯指示建了
`diagnostic.on` 之後多半不會記得刪，沒有上限的話那個檔案會一直長，附進 bug report
等於交出搜尋歷史。留一代而不是直接砍:失敗現場前面那幾行往往才是線索。

## 身分與介面

<a id="command-ids"></a>

### 命令 Id 為什麼要寫死

`src/Inkling/CommandIds.cs` 裡那七個字串是對外承諾，跟資料格式一樣不能改。
**承諾從第一個公開版本起算** —— 在那之前它們動過一次(前綴從改名前的 `Notelet.` 換成
`Inkling.`)，那是這一節最後那兩段在講的事。

CmdPal 把使用者對命令做的設定 —— alias、全域快速鍵、釘選、fallback 的顯示規則與排序 ——
全部存在自己的 settings.json 裡，鍵就是命令的 `Id`。而**命令沒有設 `Id` 時 CmdPal 會現場算一個**:
`TopLevelViewModel.GenerateId` 拿 `ProviderId + DisplayTitle + Title + Subtitle` 去做 WyHash64。
也就是說標題變一個字，那個命令對 CmdPal 來說就變成了另一個命令，使用者設過的東西全部對不上。

**現在這件事比以前更要緊**:快速記下唯一的入口就是使用者自己設的 alias，而 alias 存的鍵
就是 `Id`。`Inkling.QuickCapturePage` 改一個字，使用者的 alias 當場失效，而且症狀是
「打 `! ` 沒反應」—— 看不出跟改標題有任何關係。

歷史教訓來自已經移除的 fallback，它的標題本來就跟著使用者打的字一直變:CmdPal 的
settings.json 裡曾經留下兩個 Inkling fallback 條目，把其中一個的雜湊反推回去，正好是標題
`記下:你好` —— 某次重新載入時搜尋框裡剛好是那句話。表現出來就是「改了一次設定，
快速新增就莫名其妙不會出現了，連改回原本的前綴也救不回來」。

(那兩個雜湊條目可能還躺在你的 CmdPal settings.json 裡，無害 —— CmdPal 會忽略對不上的鍵。)

#### 前綴換過一次，而且只有那一次

**2026-08-20 改名(Notelet → Inkling)那一輪，`CommandIds.cs` 當時的那六個字串刻意一個
都沒改**(`Scratchpad` 是隔天才加的，所以那時是六個，現在是七個)。理由與量測如下，
**這段是史料，結論在最後兩段被推翻了**。

改名前實際打開 CmdPal 的 settings.json 看過，那裡面有兩種鍵:

```json
"Aliases": {
  "! ": { "CommandId": "Notelet.QuickCapturePage", "Alias": "!", "IsDirect": false }
},
"ProviderSettings": {
  "Notelet_bf0n0751x5hse!App!Notelet": { "IsEnabled": true }
}
```

`Aliases` 的鍵是**純命令 Id** —— 條目裡沒有 PFN、沒有 provider 參照。帶 PFN 的只有
`ProviderSettings` 與 `PinnedCommands`。所以換套件身分時，只要這些字串不動，
使用者設過的 alias 就跟著新名字走;動了它們，alias 當場全部失效。

當時算的代價只有「新來的人看到 `Notelet.List` 會困惑」，而那用一段註解就解決了。
使用者永遠看不到這些字串 —— 它們不是介面文字，是設定檔的鍵。

**改完之後實地驗過，不是只從設定檔推論的。** 換完套件身分(PFN 從
`Notelet_bf0n0751x5hse` 變成 `Inkling_bf0n0751x5hse`)並重新註冊，三個 alias 全部還在:
`!` 進得了快速記下頁(placeholder 是「打字記下想法，`;;` 後面接內文…」)、
`@` 進得了新增筆記、`#` 進得了清單頁而且列得出筆記。CmdPal 主搜尋框裡那三列右邊
也照樣掛著 `#` `!` `@` 的徽章。`ProviderSettings` 的鍵帶 PFN，那一項確實跟著失效
(擴展被當成新的，預設啟用，所以看不出差別);`Aliases` 不帶，所以活下來了。

#### 然後在發版前把它推翻了(2026-08-22)

**上面那個決定只撐到第一個公開版本之前。** 前綴換成 `Inkling.`,`Publisher` 也從
`CN=Notelet Development` 換掉，舊名字整個從 repo 消失。(隔天上架時 `Publisher` 又換成
Partner Center 指派的 `CN=<GUID>` —— 中間那個值沒發出去過，見
[〈套件身分凍結在 Partner Center 指派的那一組〉](#package-identity)。)

翻案的理由不是「看起來一致」，是**那個保證保的東西當時等於零**:安裝基數是作者一台機器，
一版都還沒發出去。實際盤點過 CmdPal 的 settings.json，會被清掉的只有三個 alias
(`#` / `!` / `@`)、一個**早就指向舊 Notelet 套件、本來就是死的**釘選，以及擴展的啟用狀態
(重新註冊後預設就是啟用)。快速鍵一個都沒設。也就是說整筆代價是「重設三個 alias」。

對面那一邊則是**每一個之後讀到這個 repo 的人**都要在 `CommandIds.cs`、`CLAUDE.md`、
這一節、發版清單與 `cmdpal-ui.ps1` 的過濾式裡各被解釋一次，而且那個過濾式**已經
因為前綴對不上而靜靜壞過一次**(見 `CHANGELOG.md` 那條 `Inkling*` 的修正)。
一個只保護一個人、卻讓五個檔案長期說謊的承諾，在**唯一還能反悔的時刻**應該反悔。

**什麼變了才該重新考慮:沒有。** 這一格用掉了。第一個公開版本一上架，
`CommandIds.cs` 那七個字串與四個 manifest 身分字串就是永久承諾 ——
那時被清掉的是別人的設定，而他們沒有同意過。`CommandIdTests` 逐字釘住那七個字串，
發版 runbook 第 0 部分還會再對一次 `git diff`。

> **順帶一個新的絆腳石:`CommandIds.Provider` 現在是 `"Inkling"`，而
> `Package.appxmanifest` 的 `uap3:AppExtension Id` 也是 `"Inkling"` —— 兩個字串一模一樣，
> 但毫無關係。** CmdPal 的 `ProviderSettings` / `PinnedCommands` 鍵是
> `<PFN>!<Application Id>!<AppExtension Id>`，第三段來自 manifest,**改
> `CommandIds.Provider` 不會動到那個鍵，反之亦然**。改名前這兩個值長得不一樣
> (`Notelet` vs `Inkling`)，陷阱是看得見的;現在它們重疊了，所以這句話要寫死在文檔裡。

<a id="package-identity"></a>

### 套件身分凍結在 Partner Center 指派的那一組

`Package.appxmanifest` 帶的是 2026-08-23 從 Partner Center 的**產品管理 → 產品標識**
抄回來的值，**四個字串一個都不能再動**:

| Partner Center 的值 | 去哪裡 |
|---|---|
| `CPPt.InklingNotes` | `Identity/@Name` |
| `CN=CCDB8684-D6F1-4A3A-BF5C-F31F3FE830E9` | `Identity/@Publisher` |
| `CPPt` | `Properties/PublisherDisplayName` |
| 保留的名字 `Inkling Notes` | `Properties/DisplayName` 與 `uap:VisualElements/@DisplayName` |

對不上的話 `makeappx pack` 與 CI **都不會報錯**，只有上傳 Partner Center 那一刻才被退。
Store 的 product ID 是 `9NDGWN4JTXHH`,listing 在
<https://apps.microsoft.com/detail/9NDGWN4JTXHH>。

**Store 上的名字是「Inkling Notes」,CmdPal 裡看到的仍然是「Inkling」** ——
後者走 `.resx`，跟保留名稱無關。`Inkling` 在 Store 被商標擋下(Inkling Systems /
inkling.com)，而且那條路連 `reportapp@microsoft.com` 都救不了(那是給持有商標的人用的),
所以上架名加了 Notes;命令標題沒有跟著加，短的比較好按。

#### 為什麼只能定一次

`Name` + `Publisher` 決定 package family name(PFN，目前是
`CPPt.InklingNotes_fsn608qftpbpp`)。**後綴那串雜湊只由 `Publisher` 決定，而且算不出來** ——
只能註冊一次之後用 `(Get-AppxPackage '*Inkling*').PackageFamilyName` 量
(實測與產品標識頁預告的 PFN 一字不差)。Windows 按 PFN 隔離
`%LOCALAPPDATA%\Packages\<PFN>\LocalState\`,PFN 一變，擴展自己的 `settings.json`
就變成孤兒 —— 筆記資料夾、分隔符與預覽開關全部退回預設，而檔案都還在，清單卻是空的。
CmdPal 端的 `ProviderSettings` 與 `PinnedCommands` 用
`<PFN>!<Application Id>!<AppExtension Id>` 當鍵，一樣孤兒化;只有 `Aliases` 不帶 PFN,
所以 alias 撐得過換身分(見上一節)。

#### 換過三次，全部在第一個公開版本之前

- **2026-08-20 改名 Notelet → Inkling**:只動 `Identity/@Name`,`Publisher` 刻意不動,
  所以 PFN 的雜湊後綴沒變(`Notelet_bf0n0751x5hse` → `Inkling_bf0n0751x5hse`)。
- **2026-08-22 把舊名字整個清掉**:`Publisher` 換成 `CN=Inkling Development`,命令 Id
  前綴換成 `Inkling.`。PFN 變成 `Inkling_b83qevkfx7m2r` —— **那是過渡值，隔天就被取代了,
  不要拿它去對任何東西。**
- **2026-08-23 換成 Partner Center 指派的那一組**:最後一次。擴展的 `settings.json`
  又孤兒化一次，alias 沒事(命令 Id 沒動)。

⚠ **換 `Publisher` 之後第一次部署要先顯式移除舊套件**:
`Get-AppxPackage '*Inkling*' | Remove-AppxPackage -PreserveApplicationData`。
`deploy.ps1` 自己的移除分支**只在 `InstallLocation` 不同時才觸發**，而換身分時佈局路徑沒變,
那個分支會被跳過。⚠ 換完之後**主搜尋框可能變成十列，兩組五列** —— 那是 CmdPal 在套件
安裝事件上沒有去重(CLAUDE.md 第 6 條的第一種)，不是真的裝了兩個;停掉
`Microsoft.CmdPal.UI` 讓它重啟就好。

#### 憑證:走 Store 代簽，repo 裡不會有任何憑證

Store 在通過認證之後用微軟的憑證重簽，本機部署走 `Add-AppxPackage -Register`
(開發者模式的 loose-file 註冊)，兩條路都不需要我們自己簽。**只有要給人側載、
或要上 winget-pkgs 才需要買憑證** —— 公開信任的 OV 程式碼簽章憑證約每年 USD 70–400,
而且 `Publisher` 要改成憑證的完整 DN,**那等於再換一次身分**,所以現在已經不可能了。
EV 能立刻取得 SmartScreen 信譽但貴得多;OV 要慢慢累積。`release.yml` 的簽章步驟只在
repo secret `SIGNING_CERT_BASE64` 存在時才啟用。

**什麼變了才該重新考慮**:要正式走 winget-pkgs 或直接散佈 msix 的話會重新評估買憑證,
但**`Identity/@Publisher` 仍然不能動** —— 那會洗掉所有使用者的設定。

#### PFN 不要再寫死進文檔

那個字串以前硬編碼在八處(五個檔案)，換身分後全部靜靜失效 —— 讀不到檔案不會報錯,
只會讓驗證失明。現在文檔一律寫 `%LOCALAPPDATA%\Packages\<PFN>\LocalState`,腳本內插
`(Get-AppxPackage '*Inkling*').PackageFamilyName`。唯一留著字面值的就是這一節,
因為這裡的重點正是那個字串本身。

<a id="app-list-entry"></a>

### 套件刻意不出現在開始功能表

`Package.appxmanifest` 的 `uap:VisualElements` 上有一個 `AppListEntry="none"`,
**不要拿掉**。少了它，CmdPal 的結果裡會多出第六列「Inkling / Capture thoughts in
seconds, right in Command Palette」，按 Enter 完全沒有反應。

成因跟擴展沒有關係:這個套件對 Windows 來說是一個正常的已安裝應用程式，於是進了
開始功能表的應用程式清單，而 CmdPal 內建的應用程式搜索把清單裡的東西也列進結果。
按下去它就去啟動 `Inkling.exe` —— 而那支 exe 是純 COM server,`Program.cs` 沒收到
`-RegisterProcessAsComServer` 就只 `Console.WriteLine` 一行然後結束，擴展進程又沒有
主控台，所以畫面上什麼都不會發生。

**這一列跟〈為什麼 Reload 之後有時會冒出兩個 Inkling〉是兩回事**，查的時候別搞混:

| | 多出來的那一列 |
|---|---|
| 應用程式清單項 | 副標是 manifest 的 `Description`(英文)，圖示是 Windows 從 `Square44x44Logo` 挑的，按 Enter 沒反應 |
| 重複的 provider | 副標是我們自己的資源字串(跟著介面語言)，五個命令整組重複 |

驗法:`Get-StartApps | Where-Object { $_.Name -like '*Inkling*' }`，有東西就是前者。

微軟自己的〈[Packaging a CLI Executable as MSIX](https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/guides/packaging-cli)〉
對同樣形狀的套件(exe 不是給人點的)開的就是這個處方。屬性在基底 `uap` 命名空間裡，
最低版本 Windows 10 1511，我們的 `MinVersion` 是 19041，不用多加命名空間。

代價與不是代價的:Inkling 不再出現在開始功能表 —— 反正點了也沒用。**解安裝不受影響**,
設定 → 應用程式 → 已安裝的應用程式 照樣列得到，`Remove-AppxPackage` 也照樣能用。
**擴展的探索也不受影響**:CmdPal 走的是 `AppExtensionCatalog`，認的是
`windows.appExtension` 註冊，跟應用程式清單可見性無關 —— 加上這一行之後重新部署，
`tools/VerifyRegistration` 照樣列得到 Inkling，五個命令也照樣在。

<a id="ui-language"></a>

### 介面語言跟著 Windows 走

介面有英文、繁體中文、簡體中文三種，**沒有設定項** —— 看到哪一種由 Windows 的顯示語言決定。

字串全部在 `src/Inkling/Properties/` 的三份 `.resx` 裡，程式碼一律經由產生出來的
`Resources.<鍵>` 取用，語言選擇是 `ResourceManager` 照 `CultureInfo.CurrentUICulture`
自己處理的，我們沒有寫任何偵測。中性(fallback)那一份是**英文**:系統語言不在這三種裡面時
(法文、日文……)拿到的就是它。

| 檔案 | 對應 | 誰會拿到 |
|---|---|---|
| `Resources.resx` | 中性，英文 | 上面兩列以外的所有語言 |
| `Resources.zh-Hant.resx` | 繁體中文 | zh-TW / zh-HK / zh-MO |
| `Resources.zh-Hans.resx` | 簡體中文 | zh-CN / zh-SG |

`zh-Hant` 一份就夠是因為 .NET 的文化回落:`zh-TW` 的 parent 就是 `zh-Hant`。
不必為每個地區各放一份。

**這件事能成立的前提是命令 Id 已經寫死**(上一節)。CmdPal 沒設 `Id` 時是拿標題去算雜湊當
身分的 —— 那樣的話光是換一種語言，使用者的 alias、快速鍵、釘選就會全部對不上。
`CommandIds.cs` 在，所以標題可以自由翻譯。

實測過的四件事(都在這台機器上，Windows 顯示語言 `zh-TW`):

- **擴展進程拿得到使用者的顯示語言。** 它是 CmdPal 用 COM 拉起來的獨立進程，不是 CmdPal 的
  子視窗，所以這件事不能想當然。`diagnostic.log` 印的是 `UI 語言:zh-TW 抽樣='設定'`。
- **trimming 不會砍掉附屬組件。** Release 是 trimmed publish,`zh-Hant\Inkling.resources.dll`
  與 `zh-Hans\Inkling.resources.dll` 都完整進到 MSIX 佈局裡(套件大小沒有可見變化)。
- **回落是乾淨的。** 強制 `fr-FR` 拿到英文，不是空字串也不是例外。
- **CmdPal 自己沒有語言覆寫。** PowerToys 有些模組會照設定裡的 `language` 去套
  `ManagedCommon.Language.LoadLanguage()`，但 0.11.11762.0 的整個 CmdPal 套件 byte-scan
  `LoadLanguage` 掃不到，`main` 的原始碼裡設 `CurrentUICulture` 的也只有單元測試。
  所以擴展與 CmdPal 本體看到的是同一個語言，不會一半中文一半英文。

**為什麼不加一個語言設定項。** 想要「Windows 是英文、但 Inkling 顯示中文」的話得自己選語言，
而那會踩到〈設定頁有兩個入口〉那一節講的限制:CmdPal 手上握著的是使用者當下開著的頁面實例，
換語言等於每一頁的 `Title` / `Name` / `PlaceholderText` 與每一塊快取都要自己重算，
`ICaptureSeparatorStore` 那個形狀要再複製一遍。跟隨系統零成本，而真的需要換語言的人
去改 Windows 的顯示語言本來就要重新登入 —— 那時候擴展進程也一起重啟了。

**改語言之後沒有立刻變**是預期行為:擴展進程被 CmdPal 拉起來之後就常駐，
Reload 或重新登入才會重讀。

**改字串的規矩:三份一起改。** `Resources.resx` 是翻譯的來源，註解(`<comment>`)只寫在
它裡面 —— 佔位符 `{0}` 是什麼意思都寫在那裡。`ResourceParityTests` 會擋住只改一份、
佔位符數目對不上、值是空的，以及「英文那份混進中文」。

<a id="icons"></a>

## 圖示

原始檔是 `assets/icon/` 底下的九個 SVG(八個進套件，加一個 GitHub social preview),`src/Inkling/Assets/*.png` 全部由
`tools/render-icons.ps1` 產生 —— **不要手改那些 PNG**，改圖示請改 SVG 再跑一次腳本。

構圖是「一道有壓感的下筆 + 一顆句點」:起筆重、收筆輕，最後點一下收尾。
取自 inkling 的字面 —— 一點墨水、一個還沒成形的念頭。句點是整張圖唯一的彩色元素。

**刻意避開的東西**:捲角便條紙、鉛筆加紙、記事本。那三個是 Notepad / Sticky Notes /
OneNote 的符號，而「在 CmdPal 清單裡長得像它們」正是當初要改名換圖示的理由之一。

三個取捨都是實際渲染到 20px 看過才定的:

| 決定 | 為什麼 |
|---|---|
| 筆畫直立，不斜 | 第一版起筆帶橫向，縮到 24px 讀成數字「7」 |
| 句點在右下，不在筆畫正上方 | 放上方會讀成小寫字母 `i` |
| 滿版圓角磚，不是去背形狀 | `BackgroundColor` 是 `transparent`，連 plated 那一版也沒有系統底板 —— 單色去背的形狀在深色主題會直接消失 |

原始檔的分工:

| 原始檔 | 用在 | 差別 |
|---|---|---|
| `inkling-tile.svg` | 150×150 以上 | 標準比例 |
| `inkling-tile-small.svg` | 88px 以下(工作列、CmdPal 清單) | 筆畫放大約 8%、粗細差拉開，句點從 `r=18` 放大到 `r=25` |
| `inkling-wide.svg` | 寬磚與啟動畫面 | 標記縮到 70% 置中在寬底板上 |
| `inkling-social.svg` | GitHub social preview(`assets/social-preview.png`,1280×640) | 標記放大 1.25 倍置中;不進套件，也不進 README(GitHub 自己拿去畫分享卡片) |
| `inkling-cmd-list.svg` 等五個 | 五個頂層命令 | 24 格線單色，見下 |

精細版的收筆端在 24px 只剩不到一像素，會直接斷掉;句點縮下去也剩不到兩像素。
小尺寸版因此重畫過 —— 這是圖示設計的常規做法(optical sizing),
兩份的顏色與圓角比例一致，並排看得出是同一個。

#### `Square44x44Logo` 的兩條候選階梯要各自補齊

Windows 用 MRT 從檔名的限定詞挑圖，而 `Square44x44Logo` 有**兩條分開挑的**階梯:
沒帶 `altform` 的請求走 `.scale-*`，要 unplated 的地方(應用程式清單、工作列按鈕)
走 `.targetsize-*_altform-unplated`。**一條裡有大圖救不了另一條。**

Visual Studio 模板只給兩張:`scale-200`(88px)與 `targetsize-24_altform-unplated`(24px)。
於是要 unplated 的地方永遠只有 24px 可挑 —— 這台是 150% DPI，清單列上要 30px,
它就把 24 放大，看起來是糊的。**同一個畫面上四個命令的圖示卻很銳利**，因為那些的
來源是 48px 往下縮。兩張圖並排就看得出來，而這不是渲染器或 SVG 的問題。

所以兩條都補齊:`scale-100/125/150/200/400` 與
`targetsize-16/24/32/48/256_altform-unplated`，全部由 `render-icons.ps1` 產生。
沒有再另外出「plated」(不帶 `altform`)的 `targetsize` 變體 —— `BackgroundColor` 是
`transparent`,Windows 不會畫底板，兩者長得一樣，而沒帶 `altform` 的請求本來就落在
`scale-*` 那條上。

(套件加了 `AppListEntry="none"` 之後應用程式清單那條路不會再被走到，
但工作列按鈕還在 —— 設定頁的「瀏覽…」對話框刻意不掛 owner，靠的就是那顆按鈕。)

### 頂層命令用自訂圖示，Ctrl+K 選單維持字形

`Icons.TopLevelList` / `TopLevelCapture` / `TopLevelNew` / `TopLevelScratchpad` /
`TopLevelDelete` 是自己畫的 PNG，其餘全部維持 Segoe Fluent。界線是這樣劃的:

- **頂層命令**出現在 CmdPal 主搜尋框的結果裡，要一眼看得出是同一個產品 —— 走自訂。
- **`Ctrl+K` 選單與頁面內**跟 CmdPal 內建命令混在一起，字形反而更協調;
  而且 Segoe 在 16/20px 有專業 hinting，手畫的比不上 —— 走字形。

代價講明白:**刪除那一個變弱了。** 垃圾桶(`0xE74D`)比「筆畫＋叉」一望即知，
而刪除是這一組裡最需要一眼認得的。這是為了家族一致刻意付的，不是疏忽 ——
覺得誤刪風險比較重要的話，把 `Icons.TopLevelDelete` 改回 `Glyph(0xE74D)` 就好。

**一個命令要兩張 PNG。** 字形是以文字繪製的，前景色自動跟主題走;PNG 不會。
所以每個命令備了淺色主題(深色前景)與深色主題(白色前景)兩張，交給
`IconHelpers.FromRelativePaths(light, dark)` 去挑。少了這一層，深色主題下圖示會整片看不見。
`render-icons.ps1` 用同一份 SVG 渲染兩次，差別只在注入的 `color`。
那一行的 `!important` 拿不掉:SVG 檔案自己帶 `style="color:..."`(方便單獨開起來看),
而行內樣式優先權高過選擇器 —— 少了它兩張 PNG 會長得一模一樣，而且不會報錯。

**修飾符的挖空用 `mask`，不是拿底色填。** 右下角那個徽章要跟筆畫分開，
第一版是畫一個白色圓形蓋上去 —— 那在這裡是錯的:這幾張輸出成去背 PNG 疊在
CmdPal 的清單列上，而那個背景是 Mica、半透明的，填死的白圓在深色主題會變成一塊亮斑。
`mask` 才是真的把筆畫挖穿。

渲染器用的是 Chromium(Chrome 或 Edge，哪個在就用哪個):這台機器沒有 ImageMagick /
Inkscape / rsvg，而 .NET 不會解 SVG。重點是它**以目標尺寸直接向量渲染**，不是先畫大張
再縮圖，所以 24px 的邊緣是乾淨的。腳本裡有一個容易踩的地方:svg 的 CSS 尺寸要寫死成
目標像素，不能用 `100vw`/`100vh` —— headless 的版面視窗寬度不等於 `--window-size`,
用相對單位截出來會是偏移又放大的半張圖。

### `Assets` 一定要 `CopyToOutputDirectory`

`Inkling.csproj` 把圖示收成 `Content` **並且**設了 `CopyToOutputDirectory`。少了後者，
那些 PNG 不會進建置輸出，而我們是以 loose file 註冊建置輸出當套件的 ——
`AppxManifest.xml` 裡每一個 `Logo` 都會指向不存在的檔案。**症狀很騙人**:套件照樣註冊
成功、擴展照樣能用，只是所有圖示變成 Windows 的預設灰方塊，看起來像「圖示做壞了」。
`IconHelpers.FromRelativePath` 也一樣讀不到 —— 它組的是 `BaseDirectory` 底下的實體路徑，
不走 MRT，所以 CmdPal 清單裡那一列也會是空的。

<a id="deferred"></a>

## 評估過但沒有做

這一節收的是**查過、量過，然後決定不做**的東西。它們不是待辦 —— 沒有寫下來的話，
每隔一陣子就會有人(包括半年後的自己)重新想到同一個點子，再把同樣的路走一遍。
每一條都寫了「什麼變了才該重新考慮」;前提沒變就不要動。

<a id="no-isloading"></a>

### 清單第一次開啟沒有載入指示

`ListPage.IsLoading` 在 `src/` 底下零命中。清單頁第一次 `GetItems()` 會走到
`FileSystemNoteRepository.Load()`，同步掃完整個資料夾(含子資料夾)再讀完每一個 `.md`。

**本機 SSD 上量過:3000 則、Release 組態，冷掃 108–125 ms。** 那個數字撐不起一個載入指示。

真正的風險在別處:**OneDrive Files On-Demand 的 dehydrated 檔案，`File.ReadAllText`
會觸發雲端下載** —— 而雲端資料夾正是 README 主打的用法(README 自己在同步那一節警告過)。
那時使用者看到的是一個空白、沒有任何進度的面板。

**還是沒做，因為那條路的延遲從來沒被量過。** 手上沒有 dehydrated 狀態的測試資料夾，
「會觸發下載」是從 Files On-Demand 的機制推的，不是實測。而且修法不是加一個旗標就好:
`IsLoading` 要有意義，載入本身得先變成非同步的，那是把 `GetItems` 那條同步路整個翻掉。
**要動之前先量**:弄一個真的 dehydrated 的資料夾，量冷開啟。量出來還是一百多毫秒就別做。

<a id="no-dock-band"></a>

### 沒有實作 dock band

`GetDockBands` / `DockViewModel` / `DockWindow` 在安裝版的 `Microsoft.CmdPal.UI.exe` 裡
byte-scan 都掃得到，SDK 的 winmd 也有 `ICommandProvider2` —— **技術上做得到**,
而「快速記下」確實是最適合放上 dock 的動作。

**沒做。** dock 是常駐在畫面上的東西，而 Inkling 的整個立論是「叫出面板 → 打字 → Enter」,
那條路的鍵數已經是最低的了 —— dock 換來的是滑鼠可及性，不是更少的按鍵。
而且**沒有實跑驗過**:byte-scan 只證明那些型別存在，不證明擴展掛上去長什麼樣、
也不證明使用者沒開 dock 時不會白佔一個註冊。

要重新考慮的前提:有人真的提出需求，或 dock 變成 CmdPal 的主要入口。
真要做的時候不必從頭查:CmdPal 的「建立擴展」功能產生出來的專案裡
(`.github/skills/add-dock-band/`)就附了操作指南，用那個產生器重新出一份模板就找得到 ——
**沒做不等於沒查過**。(這份 repo 曾經把那份指南連同其餘模板文件一起搬進
`.claude/skills/`,後來認定那是原封不動的上游文件、不該進一個原創作品的 repo，
已經刪掉，見 `.claude/skills/README.md` 的說明。)

<a id="two-translations"></a>

### 只有兩個翻譯

`src/Inkling/Properties/` 底下是中性(英文)加 zh-Hant、zh-Hans。有一人維護的社群擴展做到五種語言。

**基礎建設不是障礙** —— resx 那一套加上 `ResourceParityTests` 已經比它們完備，
加一個語言就是加一個檔案，漏掉的鍵與對不上的佔位符會被測試擋下。

**沒加，因為沒有人要。** 成本不在第一次，在往後**每一次改字串都要多改一份** ——
而這個 repo 的介面字串還在動。**收到請求再加**，那時至少知道加的是對的語言，
而不是照使用人數表猜一個出來。

<a id="no-dispose-test"></a>

### 「Dispose 之後不准再掛 watcher」沒有測試

`EnsureWatcher` 開頭那個 `_disposed` 守衛是修過的 bug:換筆記資料夾時 provider 會釋放舊
repository，但 CmdPal 手上的舊頁面還活著，它一呼叫 `GetAll` 就會生出一個沒有人會再去釋放的
`FileSystemWatcher` —— 換幾次資料夾就漏幾個，每個都還盯著舊目錄發事件。

**沒有測試，而不是忘了寫。** `_watcher` 是 **private**;`Inkling.Core` 確實開了
`InternalsVisibleTo` 給 `Inkling.Core.Tests`，但那只放行 `internal`，碰不到 private 欄位。
兩條路都不划算:為了測試把欄位放寬成 `internal`，是讓測試改變它要測的那個型別的形狀;
用反射去掏，則是這個測試專案裡唯一一處反射(13 個測試檔目前一個 `BindingFlags` 都沒有)。

從公開 API 唯一觀察得到的形狀是「Dispose 之後寫一個檔案，`Changed` 不應該再響」——
那是在測一個**不會發生的事件**，只能靠等，而等多久都不構成證明，還會偶爾紅。
寧可留那段講清楚為什麼的註解。

<a id="toggle-uia-name"></a>

### 設定頁那個核取方塊的 UIA `Name` 是錯的，三種修法都量過了

「記下後先看一眼」那個 `Input.Toggle`，在 UI Automation 樹上沒有名字 ——
**而且第二次之後會撿到別人的名字**:

| 什麼時候 | UIA 樹 |
|---|---|
| 剛導覽進設定頁(卡片第一次渲染) | `CheckBox: ''` |
| **按過一次「儲存」之後**(卡片被 `Refresh()` 重建) | `CheckBox: '瀏覽…'` |

第二列那個名字是卡片上面那顆「瀏覽…」按鈕的。成因:Adaptive Cards 的渲染器是拿
`label` 去設 `AutomationProperties.Name` 的(隔壁兩個 `Input.Text` 的 `Name` 正是它們的
`label`)，而這個 toggle 只有 `title` 沒有 `label` —— 名字從來沒被設過，
重建時就撿到了範圍內最後一個設過的。讀螢幕軟體因此在存過一次檔之後會把這一格念成
「瀏覽… 核取方塊」;第一次打開時名字是空的，多半會退回去唸方塊旁邊那行字(那是對的)。

**這一頁每次存檔都會重建卡片**(`InklingSettingsPage.Refresh`，見上面〈設定頁有兩個入口〉),
所以第二列不是罕見情形，是改過設定之後的常態。

**2026-08-23 把三種寫法都實機跑過**(每一種都重新部署 + Reload，讀 UIA 樹並截圖):

| 寫法 | UIA `Name` | 畫面 |
|---|---|---|
| 只有 `title`(**現在這樣**) | 空的 → 重建後變「瀏覽…」❌ | 對:`☑ 記下後先看一眼` |
| `label` + `title` | 「記下後先看一眼」✅ | 同一句話印兩次(標題列一次、方塊旁邊一次) |
| **只有 `label`** | —— | **整張卡片渲染不出來，設定頁一片全白** ⚠ |

第三種特別值得記:它不是排版走樣而是**完全空白**，連「儲存」都沒有 —— 也就是說
`Input.Toggle` 在這個渲染器上**沒有 `title` 就不合法**，而它不會報錯、不會退回預設樣子，
就是不畫。改這張卡片時撞到全白畫面，先想到這一條。

**維持第一種。** 能修好名字的兩種寫法都要動到看得見的版面，那是設計決定而不是修 bug,
而目前沒有人回報過這件事(對照〈只有兩個翻譯〉同一個判準:收到請求再做)。

**什麼變了才該重新考慮**:有人實際用讀螢幕軟體回報這一格念錯;或是 CmdPal 換掉
Adaptive Cards 的渲染器版本、`Input.Toggle` 開始接受沒有 `title` 的寫法
(那時第三種就變成零代價的正解，重跑上面那張表確認)。

**還沒試過的第四種**:`label` 放欄位名、`title` 放一個短字(例如「開啟」),
畫面變成「標題列 + `☑ 開啟` + 說明」—— 那是 Windows 設定裡常見的形狀，名字也會對。
代價是多一個資源鍵要翻三份，而且動到版面，同樣屬於設計決定。

<a id="dev-notes"></a>

## 開發考證

### 為什麼 Reload 之後有時會冒出兩個 Inkling

CmdPal 那邊的問題，不是擴展的。重新註冊套件會讓 Windows 的套件目錄發出**安裝**事件
(套件版本從頭到尾都是 0.1.0.0，所以它算是重裝而不是升級 —— 升級走的是「先移除再安裝」,
反而不會出事)。CmdPal 收到安裝事件後會替同一個擴展再建一個 `CommandProviderWrapper`,
而 `TopLevelCommandManager.RegisterAndLoadCommandsAsync` 是直接 `AddRange`，不去重。

手動 Reload 如果搶在那個非同步事件之前跑完，被清掉的是舊清單，事件補進來的就成了第二個。
所以 `deploy.ps1 -Reload` 會先等幾秒再送重新載入。已經看到兩個的話，**再 Reload 一次不一定收得回去** ——
實測兩次重新部署都出現重複，兩次都是 Reload 之後照樣是兩個，把 `Microsoft.CmdPal.UI`
進程停掉讓它重啟才乾淨。PowerToys 本身不用重開。

同一個根源還有一個更會騙人的症狀:**Reload / 重新部署之後，之前開著的設定頁是綁在
舊擴展實例上的死物件**，按 Save 靜靜地什麼都不做 —— 不寫檔、不重建、不報錯。
查「改設定沒反應」之前，先把設定頁關掉重開([development.md 的排錯](development.md#troubleshooting)也有這條)。

### 改了 manifest 之後部署會撞 `0x80073CFB`

`Add-AppxPackage -Register` 在「同一個位置、同一個版本、但 `AppxManifest.xml` 的內容
變了」時會失敗:`0x80073CFB —— 已安裝過提供的套件,不允許重新安裝`。
`deploy.ps1` 本來只處理「位置不同」(Debug ↔ Release 互換)那一種，所以第一次在
manifest 上加屬性時就撞上了。

錯誤訊息建議「遞增要安裝之套件的版本號碼」—— **不要照做**，版本號是對外的東西，
不該為了本機部署動它。移除註冊再登錄一次就好，`-PreserveApplicationData` 保住
`LocalState` 裡的設定。`deploy.ps1` 現在會接住這個 HRESULT 自己重試一次。

代價是這條路一定會經過「移除 → 重新安裝」，也就是上面那個重複 provider 的觸發條件，
所以改完 manifest 的那一次部署要多留意有沒有變成兩個。

### 查 SDK 的實際簽章

Microsoft Learn 上的 Command Palette API 參考有些頁面是 2025 年初寫的，跟 0.11 的實際
簽章對不上(至少 `FallbackCommandItem` 的建構子與 `KeyChordHelpers.FromModifiers` 的
參數個數都不一樣)。與其靠編譯錯誤一次次試，直接問組件:

```powershell
dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult ListItem
dotnet run --project tools\ApiDump -- --paths     # 設定檔存在哪
```

<a id="perf-rules"></a>

### 效能上的規矩

需求裡有一條「擴展不能拖慢 Command Palette」，對應到程式碼是三件事:

- `TopLevelCommands()` 絕不碰磁碟。CmdPal 一啟動就會呼叫它。
- `GetItems()` 每按一鍵就會被呼叫一次，所以筆記有記憶體快取，搜索是純字串比對不用 regex,
  同一個查詢字串不重建項目(快取的形狀三個清單頁共用，見 `VersionedItemsCache`)。
- 清單一次最多送 200 則(每個項目都要跨進程 COM 封送)。被截斷時清單最後會明講
  還有幾則，不會默默少東西。

`tests/Inkling.Core.Tests/PerformanceTests.cs` 是這幾條的防退化警戒線。
