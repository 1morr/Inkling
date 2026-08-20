# Inkling 設計考證

這份文檔收的是「為什麼」—— 每一個看起來繞路的設計背後的查證過程與取捨。
讀者是**未來的維護者**(包括半年後的自己)與**其他 CmdPal 擴展作者**:很多結論
(fallback 的空標題過濾、`IDetails` 的通知斷線、toast 搶焦點)對任何 CmdPal 擴展都成立。

使用者文檔在 [README](../README.md);這裡的每一節 README 都只留兩三行結論加連結。

**所有斷言的對照版本**:CmdPal **0.11.11762.0**(使用者實際安裝的 MSIX)與
PowerToys **main** 的原始碼。從 main 讀到的每一條結論都對安裝版做過 byte-scan 確認才寫進來
—— 兩邊有落差的地方(main 有、安裝版沒有)文中都明著標。掃法與已知的掃法陷阱
(方法名在 UTF-8 的 #Strings heap,**字串常量在 UTF-16 的 #US heap**,只掃一種會得到
假陰性)見 [CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉。

## 捕捉與預覽

<a id="capture-page-not-fallback"></a>

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
  `GetNonEmptyFallbackItemsCount`;而 `len2 = scoredFallbackItems?.Count ?? 0;` 上方那行
  註釋 `Empty fallbacks are removed prior to this merge` 點出了去向 ——
  過濾被提前到呼叫端 `GetSearchViewItems` 了,而那是 `main` 分支才有的。
- 佐證版本落差:byte-scan 安裝版的 `Microsoft.CmdPal.UI.exe`,`GetSearchViewItems` 與
  `MainListPageResultFactory` 在,`MainListRanker` / `ClassifyTier` / `FallbackFloor` **不在**。

表現出來就是:**不管打什麼,結果裡永遠多一個點不動的空列**。這不是我們能修的。

**換成頁面之後按鍵數一模一樣。** `!` 空白 想法 Enter —— 唯一的差別是中間會跳一次頁。
換來的是主搜尋框完全乾淨、不再受 CmdPal 版本行為影響,以及一件 fallback 結構上做不到的事:
fallback 只有一列,頁面有一整個清單,所以「記下」底下能直接列出標題相近的既有筆記。

順帶一提,前綴設定跟著一起消失了:alias 就是前綴,而且由 CmdPal 統一管理。

alias 的機制要知道兩件事(`AliasManager.CheckAlias`):

| | |
|---|---|
| indirect alias 存的鍵是「alias + 空白」 | 所以填 `!`,實際觸發的是你打完 `! ` 的那一刻 |
| 觸發時送 `ClearSearchMessage` + `PerformCommandMessage` | 搜尋框被清空、跳進頁面。**所以 alias 觸發的命令拿不到觸發當下那句話** —— 但跳進去之後打的字,是我們自己 `DynamicListPage.UpdateSearchText` 收的,完全掌控。這正是頁面版能成立的原因 |

**哪天 CmdPal 修好了想把 fallback 加回來**:整套實作在 git 歷史裡,
`git log --diff-filter=D -- src/Inkling/QuickCaptureFallbackItem.cs` 找得到。
判準是打一句不帶前綴的話,結果裡不再多出空列。真要加回來記得:alias 比 fallback
早一步處理(`MainListPage.UpdateSearchTextCore` 開頭就 `if (aliases.CheckAlias(newSearch)) return;`),
所以 alias 別跟前綴設成同一個字,否則 alias 會先把搜尋框清掉,fallback 再也看不到那句查詢。

<a id="separator-split"></a>

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
切出來的標題與內文都是),不必 Reload:那一頁訂閱
`ICaptureSeparatorStore.CaptureSeparatorChanged`。

**為什麼要有這條事件線,而不是讓 provider 整組重建就好** —— 因為重建對它根本沒用。
CmdPal 手上握著的是使用者當下開著的那個頁面實例,新建的頁面它不會去拿:實測 log 裡
`BuildState` 跑完之後一次 `GetItems` 都沒有,直到 Reload,而舊實例的項目快取(查詢字串
與 `Version` 都沒變)就這樣把舊值一路留著。硬重建反而更糟,會把還在被使用的 repository
連同 `FileSystemWatcher` 一起 `Dispose` 掉。所以**資料夾以外的設定一律讓現有頁面自己響應**,
「記下後先看一眼」走的是同一個形狀(`ICapturePreviewStore.CapturePreviewChanged`)。
頁面上快取項目的地方,快取鍵也要帶上那個設定值,否則事件收到了、拿到的還是舊結果。

頂層那一列的副標刻意寫「分隔符」而不是「分號」—— 命令陣列只在資料夾變了才重建,它跟不上。

<a id="capture-preview"></a>

### 記下之後要不要先看一眼

**「記下後先看一眼」預設是開的**:Enter 記下並停在那則筆記的完整 Markdown 上,
再按一次 Enter 才收起 Command Palette。關掉之後改成記完就收:存檔 →
toast「已記下：標題」→ Command Palette 消失。

**同一時間只有一條路在。** 做過「兩條都掛著,設定只決定哪一條在 Enter 上,另一條落到
`Ctrl+Enter`」,拿掉了 —— 沒有人會為了看一眼特地去按 `Ctrl+Enter`,那一列留著只是讓
選單多一項要讀的東西。設定就是設定。

**為什麼預設是看一眼。** 代價是每次記下都多按一個 Enter,但那一下換到的是「東西真的
寫進檔案了」的確認 —— 快速記下的整個前提是想法丟出去就不再回頭看,存檔失敗、標題內文
切錯位置(分隔符打成單一個分號之類)如果當場沒發現,之後也不會有人去發現。記完就收的
那條路上,存檔失敗的回饋只有底部命令列一個狀態訊息(為了留住搜尋框裡還沒存下的那句話,
那條路**不能發 toast**,見下面第 1 點)—— 余光裡成功與失敗幾乎分不出來。
想要極致速度的人再去設定裡關掉。

實作上有三件事是被 CmdPal 逼出來的:

**1. 停留就不能發 toast。** toast 是另一個會搶焦點的視窗,而 CmdPal 主視窗一失焦就把自己
藏起來(`MainWindow_Activated` → `EndSession("LostFocus")`,沒有開關)。「記下之後
Command Palette 整個消失」其實是 toast 造成的,不是 `GoHome()` —— 後者的語意明明白白是
「回主頁但**保持開著**」。所以預覽這條路一個 toast 都不發,存檔失敗的訊息直接畫在頁面上;
記完就收那條路的存檔失敗走 `ToastStatusMessage`(底部 InfoBadge,不開視窗)+ `KeepOpen`,
搜尋框裡那句話留著,修好問題之後再按一次 Enter 就是重試。

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
`IContentPage` 走的是無條件訂閱那條路,不是 `IDetails` 那種斷掉的(見〈原始文字模式〉)。
補齊的那幾項跟清單頁、預覽頁共用同一份組裝(`NoteCommands`),鍵位因此三頁一致。

「完成」回傳的是 `Dismiss()` 而不是 `GoHome()`:使用者記完這則想法就要回去做原本的事,
留一個主搜尋框在畫面上只是多一次 Esc。存檔失敗時它會改成 `GoBack()` —— 剛打的那句話
還在快速記下頁的搜尋框裡,退回去就能重試。

<a id="paste-multiline"></a>

### 貼上多行內容

CmdPal 的搜尋框是單行 `TextBox`,往裡面貼一段多行的 Markdown **只有第一行進得來**,
其餘的無聲消失。那是 CmdPal 的控件,擴展改不了。

所以快速記下頁在偵測到剪貼簿是多行文字時,會多給一列「內文取自剪貼簿(N 行)」——
標題還是用打的,內文直接讀剪貼簿原文,換行、縮排、程式碼區塊通通留著,完全不經過搜尋框。

<a id="preview-line-breaks"></a>

### 預覽的換行處理

標準 Markdown 裡單一換行等於空格,所以打三行會顯示成一行。對一個隨手記想法的工具來說
那不是使用者要的,所以**預覽時**會把單一換行當成真的換行。

只動拿去渲染的那份字串,**磁碟上的 `.md` 一個字都不變** —— 用別的編輯器打開仍然是標準
Markdown。程式碼區塊、表格、縮排程式碼、setext 標題底線這些「換行本來就有意義」的地方
會避開。規則在 `Inkling.Core/NotePreview.cs`,測試在 `NotePreviewTests.cs`。

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

<a id="source-mode"></a>

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

<a id="empty-content"></a>

### 清單頁的空白提示有兩種

「資料夾裡真的沒有筆記」與「有筆記但查詢沒命中」是兩件事,空白提示要分開講。
CmdPal 的 `ShowEmptyContent` 只看篩完的項目數是不是零,**不看搜尋框裡有沒有字**
(`ListViewModel`:IsInitialized、FilteredItems.Count 為零、不在載入中)——
所以「資料夾裡有幾百則筆記、打一個搜不到的字」也會走到空白提示。那時候說
「還沒有任何筆記」會讓人以為筆記不見了(真機重現過)。

所以清單頁依查詢就地切換那一列的文案:有查詢字而零命中時說「找不到符合的筆記」,
否則才是「還沒有任何筆記」。就地改 `Title` / `Subtitle` 即時生效 —— `ICommandItem`
是無條件訂閱那條路(見〈原始文字模式〉),不必重建內容。

那一列的命令直接掛快速記下頁(`IPage`,CmdPal 會導覽過去),所以空白狀態的 Enter
真的能帶使用者去記下第一則 —— 而不是給了指示卻按下去沒反應。

## 清單與詳細窗格

<a id="details-width"></a>

### 詳細面板寬度固定在最寬

詳細窗格固定是**寬**(清單:詳情 = 1:1),沒有設定項,也沒有快速鍵。清單那一邊只有
標題與一行摘要,寬一點也不多給什麼資訊;右邊是筆記本文,窄一檔就多折斷幾十行,看原始文字
時特別有感。

**能給的就只有這麼寬。** 寬度來自 `IDetails.Size`,而 CmdPal 只認
`Small / Medium / Large`,對應 3:1 / 2:1 / 1:1(`DetailsSizeToGridLengthConverter`);
自由拖曳它自己也沒做 —— 整個介面裡連一個 `GridSplitter` 都沒有。

**`Size` 一定要明著寫成 `ContentSize.Large`。** 那個列舉的 0 是 `Small`,`new Details()`
不設就是**最窄**那一檔(實測過)。而且它連事後補救的機會都沒有:`Size` 不走屬性變更通知,
是 `DetailsViewModel.InitializeProperties` 經由 `IExtendedAttributesProvider.GetProperties()`
讀一次就定了,只有換上新的 `Details` 物件才會重讀。

**這裡曾經是可調的,後來整個拿掉。** 原本有一個三檔循環的 `Ctrl+D`,選好的檔位存回
`settings.json`,設定頁還有一個對應的下拉選單 —— 兩邊改的是同一個值,所以得雙向同步:
一個 `IDetailsWidthStore` 窄介面、一個 `DetailsWidthChanged` 事件、provider 那條
「寬度變了就叫設定頁重讀」的訂閱,加上手動驗證清單裡整整一節的回歸測試。實際使用永遠
停在最寬,那些程式碼只是在維護一個沒有人用的檔位,於是連同設定項一起移除。
空出來的 `Ctrl+D` 後來給了刪除,中間拿掉過,現在又回到刪除身上 ——
那一段來回見〈清單頁的快速鍵〉。

舊 `settings.json` 裡的 `Inkling.DetailsWidth` 鍵留著不管:`Settings.Update` 只認得
自己註冊過的鍵,多一個孤兒鍵不會有任何影響,不值得為它寫一次遷移。

<a id="list-shortcuts"></a>

### 清單頁的快速鍵

鍵位全部收在 `src/Inkling/Shortcuts.cs`(CmdPal 自己的擴展也是這個形狀 ——
每個擴展一個 `KeyChords.cs`)。原則是**能少一個修飾鍵就少一個**:這幾個動作每天按,
`Ctrl+X` 比 `Ctrl+Shift+X` 順得多。但「哪些 `Ctrl+字母` 可以拿」要先看誰已經佔著。

**一、搜尋框(WinUI `TextBox`)的標準編輯鍵,一個都不能碰。** 清單頁的焦點永遠在搜尋框上,
而 CmdPal 在 `ShellPage_OnPreviewKeyDown` 就把鍵送去比對快速鍵
(`TryCommandKeybindingMessage` → `CheckKeybinding`)—— 那是 **tunneling** 階段,
比 `TextBox` 早。綁走等於從搜尋框拿掉:

| 誰的 | 有哪些 |
|---|---|
| `TextBox` | `Ctrl+A`、`Ctrl+C` / `X` / `V`、`Ctrl+Z` / `Y`、`Ctrl+Backspace`、`Ctrl+Delete`、`Ctrl+方向鍵` / `Home` / `End`、`Delete` |
| CmdPal 自己 | `Ctrl+K`(選單)、`Ctrl+Enter`(次要命令)、`Ctrl+,`(設定)、`Ctrl+I`(它自己攔掉的 —— `TextBox` 會拿它插入 tab)、`Alt+Left` / `Alt+Home` / `Alt+F` |

**二、剩下的字母隨我們挑,對得上動作最好。**

| 動作 | 鍵位 | 為什麼是它 |
|---|---|---|
| 編輯 | `Ctrl+E` | E = Edit |
| 原始文字 | `Ctrl+U` | 見〈原始文字模式〉 |
| 在預設編輯器開啟 | `Ctrl+O` | O = Open;剪貼簿記錄擴展的 `KeyChords.OpenUrl` 也是它 |
| 開啟檔案位置 | `Ctrl+L` | L = Location |
| 複製內文 | `Ctrl+Shift+C` | **唯一還帶 Shift 的**,見下面 |
| 刪除 | `Ctrl+D` | D = Delete |

**跟 CmdPal 慣例不一致的兩個,是刻意的。** 內建擴展把「開啟檔案位置」放在 `Ctrl+Shift+E`
(`WellKnownKeyChords.OpenFileLocation`,書籤與檔案索引都用它)、把刪除放在
`Ctrl+Shift+Delete`(書籤、計算機、剪貼簿記錄三個都是)。兩個都做過一版,最後為了少按一個鍵
讓位給 `Ctrl+L` / `Ctrl+D` —— 使用者按得最兇的是自己的筆記,不是跨擴展切換。

**複製為什麼留著 Shift。** `Ctrl+C` 拿不得(搜尋框要拿它複製使用者剛打的字),所以複製
只剩兩條路:借一個沒人要的字母(`Ctrl+B` = Body 試過一版),或照 CmdPal 的慣例走
`Ctrl+Shift+C`(`WellKnownKeyChords.CopyFilePath`)。**選了後者** —— 那組鍵跟「複製」的
關聯是手指本來就記得的,借來的字母得靠死記,省下的那一個 Shift 換不到。

真要換成單一個 `Ctrl`,B / G / M / R / T 都還空著,改 `Shortcuts.cs` 一行就行。
`Ctrl+Insert`(Windows 的老牌複製鍵)則刻意不碰:沒查證到 WinUI 的 `TextBox` 吃不吃它,
吃的話就等於又拿走搜尋框的一個複製鍵;而且筆電鍵盤上的 `Insert` 常常要配 `Fn`。

順帶一提:**同一個項目的選單裡撞鍵不會報錯**,CmdPal 用 `TryAdd`,第二個被靜靜丟掉
(只在它自己的 log 留一行 warning,我們看不到)。加新鍵位時自己對一遍上面那兩張表。

<a id="ctrl-d-roundtrip"></a>

#### `Ctrl+D` 兜了一圈回來

這一列的歷史值得留著,免得下次又繞一次:

1. **`Ctrl+Delete`** —— 錯的。那是搜尋框的「刪右邊一個詞」,見上面第一條。
2. **`Ctrl+D`** —— 能用,但後來整個拿掉了。當時的理由是「清單頁是拿來找筆記的,把一個
   不可逆的動作綁在搜尋框上按得到的鍵位上,換來的方便配不上誤觸的代價」,刪除因此只留在
   `Ctrl+K` 選單裡,連續清理請去 `Inkling：刪除筆記` 那一頁。
3. **`Ctrl+Shift+Delete`** —— 跟三個內建擴展一致、也難誤按,但每次刪都要按三個鍵。
4. **`Ctrl+D`(現在)** —— 「搜到某一則,順手刪掉」是清單頁上真實存在的動線;為此跑去
   另一頁還得在那裡再搜一次(那一頁只搜標題),繞得比省下來的多。誤觸的顧慮沒有消失,
   而是靠兩道防線扛:**一定會跳確認框**,而且刪掉的檔案**進資源回收筒**。

`Inkling：刪除筆記` 那一頁**沒有**跟著綁 `Ctrl+D`:那裡的 `Enter` 與 `Ctrl+Enter`
本來就是刪除,再多一個鍵只會讓語意打架 —— 清單頁的 `Ctrl+D` 是「會先問一次」,
那一頁的次要命令卻是「不問」。

## 編輯與表單

<a id="edit-form"></a>

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

<a id="settings-two-entries"></a>

### 設定頁有兩個入口,而且只有一個會自己更新

同一份設定,CmdPal 讓使用者從兩個地方看到:

| 入口 | CmdPal 怎麼拿 |
|---|---|
| 清單頁 `Ctrl+K` → 設定 | 我們放在 `MoreCommands` 裡的頁面,每次導覽進去都重建 viewmodel |
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
而**擴展發不出那個事件**:`RaiseSettingsChanged()` 是 `internal`,唯一的呼叫者是
使用者按下 Save 時走的 `SettingsForm.SubmitForm`。

結果就是:表單送出、檔案也存了,那一頁卻停在啟動時的值。

修法是**自己實作 `ICommandSettings`**(整個介面只有 `SettingsPage` 一個成員),
把 `InklingSettingsPage` 交出去,發 `ItemsChanged` 的權力就回到我們手上。
兩個入口共用同一個頁面實例,所以看到的永遠一致。

那個頁面因此**不能跟著 `ProviderState` 重建** —— CmdPal 在 provider 剛連上時就把
`Settings` 讀走了,換了實例它不知道,只會繼續用手上那個。

#### 送出表單之後也要 `Refresh()`,而且是每一次

卡片是**建構時**就把值烤進 `DataJson` 的(`FormContent` 沒有別的傳值管道),而上面那條
「只初始化一次」的路代表 CmdPal 不會因為導覽進頁面就重新 `GetContent()`。
所以只要漏掉一次 `Refresh()`,那張卡片就永遠停在 provider 剛連上時的值。

實際踩到過:分隔符改成 `##`、檔案也存了、快速記下也確實照 `##` 切,可是設定頁**每次打開
都顯示 `;;`**。當時 `Refresh()` 只掛在另一個設定的事件線上,新加的分隔符沒接上。

**比顯示錯更糟的是它會把值吃回去。** 卡片上壓著的過期值,在下一次送出時會被當成使用者
的輸入寫回設定 —— 只改資料夾按一次儲存,就足以把 `##` 默默還原成 `;;`。

所以 `OnSettingsApplied` 一進來就 `Refresh()`,排在「資料夾沒變就 return」的前面,
不分欄位、不比對新舊。表單裡按「瀏覽…」選完資料夾那條路也會走到這裡(它自己另外還會
呼叫一次 `Refresh()`,多重讀一次無害)。

**加新設定項時記得這條** —— 忘了不會有任何錯誤訊息,只會安靜地顯示舊值。

<a id="settings-form-custom"></a>

#### 表單也是自己的

頁面的內容不是 toolkit 的 `Settings.ToContent()`,而是自己寫的一張 Adaptive Card
(`InklingSettingsForm`)。三個理由:

1. **toolkit 的卡片放不下「瀏覽…」按鈕。** 設定項只能一格一格排下去。
2. **欄位名根本不會顯示。** 它把 `Label` 塞進卡片的 `title`,而 `Input.Text` 沒有那個屬性;
   真正會顯示的 `label` 它拿去放 `Description`。結果每個欄位頭上頂著一整句說明,
   看不到「筆記資料夾」這種短名字。
3. **送出之後它固定 `GoHome`**,而按「瀏覽…」時得留在原地。

代價是存檔那條路要自己接:值交給 `SettingsManager.Apply`,由它存檔並發出
`Applied`(provider 拿去比對資料夾,順便叫設定頁重讀)、`CaptureSeparatorChanged` 與
`CapturePreviewChanged`(快速記下頁跟著變)。
toolkit 的 `Settings.RaiseSettingsChanged()` 是 `internal`,本來就叫不動。
標籤、說明、選項仍然只有 `SettingsManager` 那一份,表單只負責畫。

`Apply` 對資料夾欄位有兩道防線,回傳值讓表單決定怎麼跟使用者講:
**相對路徑整筆拒絕**(它會對著擴展 COM server 進程的 CWD 解析,筆記落在意想不到的位置),
表單留在原地什麼都不存;**完整但還不存在的路徑照存**(repository 第一次存檔時會建),
但當場提示 —— 打錯一個字就靜靜換了資料夾,看起來會像「舊筆記全部消失」。

<a id="browse-button"></a>

#### 資料夾旁邊的「瀏覽…」

按下去開的是系統的選資料夾對話框(`IFileDialog` + `FOS_PICKFOLDERS`,見 `FolderPicker`)。
擴展是個**沒有視窗**的 out-of-process COM server,所以有三件事跟一般 app 不一樣:

- **對話框跑在自己的 STA 執行緒上。** `Show` 會擋到使用者關掉對話框為止,而呼叫端那條
  執行緒是 CmdPal 的(`ContentFormViewModel.HandleSubmit` 裡的 `Task.Run`),
  不能讓它在那邊等。`SubmitForm` 因此立刻回 `KeepOpen`,選好之後才用回呼把路徑送回來。
- **選好就直接存,不等使用者再按一次「儲存」。** 對話框一拿到焦點,CmdPal 主視窗就會把
  自己藏起來(`MainWindow` 的 `Deactivated` → `HideWindow`,沒有開關可以關掉),
  表單跟著一起消失 —— 那時候還壓在表單裡的值,使用者既看不到也按不到。
- **對話框不掛 owner,所以它在工作列上有自己的按鈕。** 這是刻意的:那顆按鈕是「對話框
  萬一沒被拉到前景」時唯一的退路。**不能拿 CmdPal 的視窗當 owner** —— `IFileDialog` 會
  `EnableWindow(owner, FALSE)`,而那個視窗馬上就要自己藏起來,對話框的下場只能靠運氣。
  (代價:那顆按鈕的圖示固定是套件的 `Square44x44Logo` —— 工作列按鈕的圖示擴展改不了。)

對話框**開不起來**(CoCreateInstance 失敗、`Show` 回傳錯誤、STA 執行緒拋例外)也會
用 InfoBadge 告訴使用者,細節進 DiagnosticLog —— 之前只有 log 留一行字,而它預設是關的,
使用者按「瀏覽…」的體驗就是「什麼都沒發生」。按取消不算失敗。

還有一個 Windows 本身的限制:只有前景進程開的視窗搶得到焦點,而我們這個 COM server
從頭到尾沒收過使用者的輸入。不管的話對話框會開在 CmdPal 後面,使用者只看到工作列閃一下。
`FolderPicker` 因此會去找「屬於自己、而且看得見」的那個頂層視窗(平常一個都沒有),
再 `SetForegroundWindow` 把它拉到前面;拉不動就退回 `BringWindowToTop` /
`SwitchToThisWindow`,再不行才輪到工作列那顆按鈕。

這條路實測過:把 `ForegroundLockTimeout`(這台機器是預設的 200000ms)重新武裝之後
—— 也就是模擬「使用者剛剛才點過東西」—— 對話框仍然被拉到了前景。

<a id="blank-markdown-removed"></a>

#### 表單後面那塊空白已經拿掉了(而且它八成從來沒生效過)

設定頁的表單後面曾經掛著一塊**空的** `MarkdownContent`,用途是擋「背景的設定視窗被拉到
前面來」。`ContentFormControl` 載入後會自動聚焦第一個輸入欄位,而我們每次送出表單都得叫
CmdPal 重讀(上一節)—— 重讀等於控件重建、再觸發一次 `Loaded`。當時的理由是 CmdPal 只在
「頁面上唯一的控件」時才聚焦,湊滿兩塊內容就不會聚焦,也就不會搶焦點。

那段理由是照 CmdPal `main` 的原始碼寫的:

```csharp
element.Loaded -= OnFrameworkElementLoaded;

if (!ViewModel?.OnlyControlOnPage ?? true) return;   // 不是唯一控件就不聚焦
```

**但 `OnlyControlOnPage` 在安裝版裡不存在。** byte-scan 過
`Microsoft.CmdPal.UI.exe`(0.11.11762.0):同一條路上的 `ContentFormControl`、
`OnFrameworkElementLoaded`、`FindFirstFocusableElement`、`ContentPageViewModel` 全都掃得到,
只有 `OnlyControlOnPage` 沒有,`OnlyControl` / `SoleControl` / `SingleControl` 各種變體也都沒有。
也就是說安裝版的自動聚焦沒有那道判斷,湊第二塊內容擋不掉任何東西 ——
這塊空白在使用者實際跑的版本上八成從來沒起過作用。

**這是第二次踩到同一個坑**:照 `main` 的原始碼寫進文檔,而安裝版根本沒有那段程式
(第一次是 fallback 排序,見 [CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉)。
從原始碼得到的結論一定要 byte-scan 對照安裝版再寫。

會觸發的情境本身也沒了。當初每按一次 `Ctrl+D`(那時面板寬度可調)就重讀一次表單,人卻在
主視窗翻筆記,背景視窗因此一直跳。現在 `Refresh()` 只有兩個呼叫點,都源自使用者在設定
表單上的操作(按儲存、或按「瀏覽…」選完資料夾)——人本來就在設定頁上。唯一還構得成
問題的組合是「CmdPal 設定視窗停在 Inkling 那一頁,同時從主搜尋框進設定頁按儲存」,
兩邊共用同一個頁面實例,背景那個會跟著重建。

拿掉之後換回來的是**打開設定頁時游標會自動落在第一個欄位**,不必先點一下或按 Tab ——
那是每次都付得到的好處,而上面那個組合很少見。萬一它真的又開始搶焦點,原因就在這裡;
補救方式是讓 `GetContent()` 多回傳一塊內容,但**先確認安裝版到底有沒有那道判斷**。

**說明文字現在是每個欄位下面各一塊,沒有例外。** 卡片最上面曾經還有獨立的一行提醒
(「換資料夾不會搬動已經寫好的筆記」),那是這塊 markdown 搬進卡片時留下的位置 ——
但那句話講的只有筆記資料夾一個欄位,結果它變成唯一上下都有說明的欄位。已經併進
`NotesDirectorySetting` 的說明裡。要加類似的話就加在對應設定項的說明上,
不要在卡片頂上再開一塊。

順帶一提,說明文字為什麼全部寫在卡片裡而不是另外一塊 markdown:內容區塊之間有大約 32px
收不掉的間距 —— `ContentPage.xaml` 的 `ItemsRepeater` 用 `StackLayout Spacing="8"`,
每塊內容自己又有 `Margin="0,4,4,4"` 與 `Padding="12,8,8,8"`。說明擺前面是一段跟表單斷開的
旁白,擺後面更像掉在半空(兩種都做過)。而且 markdown 那條路**沒有淡色可用**,
CmdPal 的 `MarkdownThemes` 只設定了字級與 inline code;卡片裡的 `TextBlock` 才有
`isSubtle` 跟 `size: small`,也才貼得住它說明的那個欄位。

## 刪除

<a id="delete-page"></a>

### 刪除為什麼是一頁

`Inkling：刪除筆記` 按下去不會刪任何東西,它進到一個清單頁,把即將被刪的檔案列出來。

原因是這個動作的範圍比它的名字大得多。掃描的是筆記資料夾底下(含子資料夾)**所有的
`.md`**,而且**不分辨檔案是不是 Inkling 寫的** —— 那是列清單時刻意的設計(外來的 `.md`
也要看得到),但放到批次刪除上就變成一把沒有握把的刀:資料夾要是被指到既有的
Obsidian vault、docs 目錄、或任何有 `README.md` 的專案資料夾,一次就全掃走了。
預設的 `%OneDrive%\Inkling` 是專用資料夾,所以預設設定沒有這個問題 —— 風險是改過路徑
之後才出現的。

一個確認框放不下這些。它只有一行標題與一行說明,而使用者真正需要看見的是「到底是哪些檔案」。
所以那一頁長這樣:

| 區塊 | 內容 |
|---|---|
| 動作 | `刪除全部 N 則`(副標是資料夾路徑);有外來檔案時多一列 `只刪 Inkling 建立的 M 則` |
| 不是 Inkling 建立的 | 排在最前面 —— 那正是最需要先看到的一批,圖示也不一樣 |
| Inkling 筆記 | 其餘的,副標是相對於筆記資料夾的路徑,子資料夾一眼看得出來 |

清單超過 `MaxResults` 被截斷時,最後一列會明講**沒列出來的一樣會被刪**。

「只刪 Inkling 建立的」那一列是這個做法真正換來的東西 —— 命令的形狀下根本放不下第二個動作。

順帶修掉一個小毛病:原本沒有筆記時只能回一個 toast,而 toast 的預設收尾是把整個 CmdPal
關掉,使用者只看到面板一閃就沒了。頁面有 `EmptyContent`,空的情況本來就有地方講。

資源回收筒不是絕對的保險:檔案在網路磁碟、沒有回收筒的裝置上,或大過回收筒配額時,
Windows 會直接永久刪除,而我們設的 `FOF_NOCONFIRMATION` 正好把那個警告框壓掉了。
這件事寫在頁面的詳細窗格裡。

<a id="delete-keys"></a>

### 刪除頁的兩個鍵位

每一則筆記上 `Enter` 是「刪除,但先問一次」,`Ctrl+Enter` 是「直接刪」。同一個動作給兩條路,
是因為使用者進到這一頁時的狀態有兩種:一種是心裡有數要清掉哪幾則(連著按 `Ctrl+Enter` 最快),
另一種是邊看邊決定(每一則都想再確認一次)。底部工具列會把兩條路都寫出來,不必記。

**例外只有一個**:不是 Inkling 建立的檔案,兩條路都跳確認框。那是別的工具寫的、或使用者
自己丟進資料夾的,誤刪的代價跟自己記的筆記不一樣,不給它「跳過確認」這個選項。那一列的
`Ctrl+Enter` 因此照實寫成「刪除」而不是「直接刪除」,副標講明為什麼。

預覽降到選單第二項。這一頁 `ShowDetails` 是開的,右邊詳細窗格本來就在顯示標題與內文,
預覽頁多出來的只有 Markdown 渲染 —— 不值得佔著前面那兩個鍵位。

<a id="delete-all-first"></a>

#### 「刪除全部」排第一的代價

進到這一頁時預設選中的就是它,而 0.11 刪掉一列之後焦點很可能也跳回第一列 —— `main` 有
一整套 sticky selection(留在原處,留不住才選第一個可選項),但**安裝版一個都掃不到**
(`_stickySelectedItem` / `firstUsefulIndex` / `ensureSelectionVisible` 全是 `False`,
這是第四個「`main` 有、安裝版沒有」的落差,見 [CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉)。
也就是說「想刪下一則而順手按 Enter」有機會落在這一列上。

三道防線:它一定會跳確認框、確認框標題明著寫「刪除全部 N 則筆記?」、刪掉的檔案進資源回收筒。
**而連著按 `Ctrl+Enter` 清理的那條路完全踩不到它** —— 那一列沒有次要命令,
焦點跳過來時 `Ctrl+Enter` 什麼都不會發生。想連續刪就用 `Ctrl+Enter`,這是它比 `Enter` 更安全
的地方(雖然聽起來反過來)。

<a id="no-multiselect"></a>

#### 為什麼沒有多選

CmdPal **沒有多選**:SDK 的 `IListItem` 沒有任何選取狀態的屬性
(`dotnet run --project tools\ApiDump -- ListItem` 只有 `Tags` / `Details` / `Section` /
`TextToSuggest`),主清單的 ListView 也沒開多選 —— `SelectionMode` 在整份 cmdpal
原始碼裡出現的地方全是 `Single` 或 `None`,沒有任何 `Multiple` / `Extended`。

自己畫一套是做得到的(存一組 id,標記時只換那一列的 `ListItem.Tags`;那條路在安裝版上
確實通,byte-scan 掃得到 `UpdateTags` / `VisibleTags` / `TagViewModel`),而且**實際做完過**:
`Enter` 標記、`Ctrl+Enter` 刪掉挑好的那批。最後整個移除 —— 換來的東西配不上代價。
連著按 `Ctrl+Enter` 一則一則刪,鍵數跟「挑三則再刪」幾乎一樣,而多選那條路要多帶一組狀態:
標記在頁面上活著、沒有導覽事件可以清、搜尋過濾之後看不見卻還算數(所以確認框非得把標題
一則一則列出來不可)、每一列的命令名要隨挑了幾則而變。實作在 git 歷史裡。

<a id="delete-no-toast"></a>

### 刪除成功時一個 toast 都不發

三個刪除命令(單則、批次、刪除全部)原本都是回 `ShowToast` 配 `KeepOpen`,註釋還寫著
「留在清單頁,使用者當場看到清單真的空了」。**那兩件事湊不到一起**:toast 是另一個會搶焦點
的視窗,而 CmdPal 主視窗一失焦就自我隱藏(同一個機制見〈記下之後要不要先看一眼〉)——
寫著「留在清單頁」的程式碼,實際效果是刪一則就把整個面板關掉一次。

現在成功時直接回 `KeepOpen`,不發任何 toast。回饋本來就不需要它:那一列(或那一批)
當場從清單上消失,比什麼訊息都直接。**只有例外路徑還發** —— 部分檔案刪不掉、或整個
操作丟例外的時候,使用者看到清單還剩東西,要能立刻知道那不是沒生效;那種時候面板被
toast 關掉,也比默默少刪好。

<a id="toast-status-message"></a>

#### `ToastStatusMessage` 不是那個 toast

名字很像,**但它不開視窗,也不會關掉面板**。上面那條規矩只管 `CommandResult.ShowToast`。

| 用法 | 實際發生什麼 | 面板 |
|---|---|---|
| `CommandResult.ShowToast(…)` | 送出 `ShowToastMessage`,CmdPal 開一個獨立的 `ToastWindow` | **會關掉** |
| `CopyTextCommand` 的預設 `Result` | 就是上面那個 | **會關掉** |
| `new ToastStatusMessage(…).Show()` | 呼叫 `IExtensionHost.ShowStatus`,由 CmdPal 收進 `StatusMessages` | 不會 |

`ToastStatusMessage.Show()` 在 toolkit 裡做的事只有一件:
`ExtensionHost.ShowStatus(Message, StatusContext.Extension)`,然後隔 2500 ms 再 `HideStatus`。
**擴展跑在自己的 COM 進程裡,本來就開不了 CmdPal 的視窗** —— 它能做的只有呼叫 host。
CmdPal 那一頭把訊息畫成底部命令列左邊的一個 `InfoBadge`,點開是 flyout 裡的 `InfoBar`。

安裝版 0.11.11762.0 對得上:`ProcessStatusMessage` 在 `Microsoft.CmdPal.UI.exe` 裡,
`StatusMessagesButton` / `StatusMessagesFlyout` / `MessagesDropdown` 在 `resources.pri`(UTF-16)。
反過來 `main` 那套 toast 改寫(`TransparentWindow` / `TransientSurface` / `ToastPosition`)
安裝版**一個都掃不到** —— 又一個不能照 `main` 寫文檔的例子。

所以存檔提示照樣用它(`NoteFormContent`、`InklingSettingsForm`),
而 `docs/manual-test-checklist.md` 那條「跳出『已儲存』的 toast **並回到上一頁**」
本身就是證據:面板要是被關掉,那一項當初不會通過。

<a id="copy-feedback"></a>

## 複製

### 複製完留在原地,回饋是那一列上的標籤

「複製完不要關掉面板」跟刪除踩的是同一顆地雷,但難一點:刪除的回饋是那一列消失,
**複製沒有任何看得見的結果** —— 剪貼簿是隱形的。

toolkit 的 `CopyTextCommand` 預設回 `ShowToast`,而 `ToastArgs.Result` 的預設又是
`Dismiss`,兩件事疊起來就是「複製一次關一次」。而且**光把 `ToastArgs.Result` 改成
`KeepOpen` 沒有用**:toast 是另一個會搶焦點的視窗,主視窗一失焦就自我隱藏。
想留在畫面上,就一個 toast 都不能發。

所以回饋改成在那一列右邊打一個 **`已複製`** 的標籤,2.5 秒後自己收掉
(跟 CmdPal 自己的 toast 同一個時長,`ToastWindow.VisibleDuration`)。沒有內文的筆記
打的是 `沒有內文`,而且**不碰剪貼簿** —— `ClipboardHelper.SetText` 會先 `EmptyClipboard()`,
對空筆記按下去等於把使用者剛複製的東西清掉。

走 `ListItem.Tags` 是因為**這條路跨進程是通的**,跟 `Details` 正好相反(見
〈為什麼不是就地改 `Details.Body`〉):`ICommandItem` 在 IDL 裡就繼承 `INotifyPropChanged`,
CmdPal 對它無條件訂閱,而且安裝版的 `UpdateTags` / `VisibleTags` / `TagViewModel` 都掃得到。
這裡也刻意不呼叫 `RaiseItemsChanged`:整份清單翻新一次選中項就有機會跑掉,而剛複製完
使用者通常還想留在同一列上。

預覽頁沒有清單列可以掛標籤,所以那裡的複製是**靜靜完成**的 —— 那一頁整頁顯示的就是
剛複製走的內容,自己會說話。

<a id="confirm-dialog-colors"></a>

### 確認框的按鈕沒有顏色,也沒有「危險」樣式

`ConfirmationArgs` 的全部內容就是 `Title` / `Description` / `PrimaryCommand` /
`IsPrimaryCommandCritical` 四個屬性(`dotnet run --project tools\ApiDump -- ConfirmationArgs`),
**沒有任何樣式或顏色的開口**。那個對話框是 CmdPal 自己 `new` 的 WinUI `ContentDialog`,
擴展只提供文字跟要跑的命令。

`IsPrimaryCommandCritical` 聽起來像「把按鈕標成危險色」,但上游拿它做的唯一一件事是:

```csharp
if (vm.IsPrimaryCommandCritical)
{
    dialog.DefaultButton = ContentDialogButton.Close;   // ← 預設落在「取消」

    // TODO: Maybe we need to style the primary button to be red?
    // dialog.PrimaryButtonStyle = new Style(typeof(Button)) { ... }
}
```

紅色按鈕在 `ShellPage.xaml.cs` 裡是**註解掉的 TODO**,微軟自己也還沒做。所以「刪除」按鈕
沒有紅色、也沒有強調色,這是 CmdPal 目前就長這樣,不是我們漏設什麼 —— 兩個按鈕都是預設樣式,
開啟時焦點落在主要按鈕(截圖上那圈黑框),Enter 就是確認。

這一節講的只有**確認框**。`Ctrl+K` 選單裡的那一列是另一回事 —— 那裡有一個真的會變紅的
開關,見下一節〈刪除的紅色只有一個地方碰得到〉。

**而且 0.11 安裝版連上面那個 `if` 都沒有。** 整個套件掃不到 `set_DefaultButton`,
同一段程式碼的 `set_PrimaryButtonText` / `set_CloseButtonText` / `set_XamlRoot` 卻都掃得到,
所以不是掃描失準(這是第三個「原始碼有、安裝版沒有」的落差,見
[CLAUDE.md](../CLAUDE.md)〈查證 CmdPal 的行為〉):

```powershell
$exe = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe\Microsoft.CmdPal.UI.exe"
$u8 = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($exe))
$u8.Contains('set_PrimaryButtonText')   # True
$u8.Contains('DefaultButton')           # False ← 一次都沒設過
```

也就是說在使用者手上的版本裡,`IsPrimaryCommandCritical` 設不設**畫面上完全一樣**,
`DefaultButton` 永遠是 `None`。旗標還是照語意設,等 CmdPal 更新上來就會生效:

| | `IsPrimaryCommandCritical` | 為什麼 |
|---|---|---|
| 刪一則(Inkling 建立的) | **不設** | 有資源回收筒兜底,不值得為此讓每次刪除都多按一次方向鍵 |
| 刪一則(外來檔案,刪除頁) | **設** | 那是別的工具寫的檔案,誤刪的代價不一樣,值得多那一下 |
| 批次刪除(兩列都是) | **設** | 一次動幾十個檔案就該多花那一下 |

已知的口徑不一:清單頁的 `Ctrl+D` 對外來檔案**不設**(那條路本來就一律先跳確認框,
焦點一律在確認鈕上),刪除頁對外來檔案設 —— 兩頁對同一種檔案的 critical 標記目前不一致,
留待 CmdPal 真的實作那條路時再統一。

要注意批次刪除**現在沒有這道防線**(0.11 上 Enter 一樣是確認),真正的防線是刪除全部那一頁
本身會先列出會刪掉哪些檔案。SDK 也沒有辦法把預設按鈕指定成「確認」—— 上游只有「設成取消」
跟「不設」兩種。

#### 藍色也不行,而且整個對話框我們只碰得到一個字串

紅色是註解掉的 TODO,那**藍色(強調色)呢**?WinUI 的 `ContentDialog` 只有一個機制會把某顆
按鈕變成強調色:`DefaultButton`。而 CmdPal **從來沒把它設成 `Primary`** —— `main` 唯一
設它的地方是 `IsPrimaryCommandCritical` 時設成 `Close`(那會讓「取消」變藍,不是「刪除」),
而 0.11 安裝版連那一行都沒有。也就是說那個對話框裡**兩顆按鈕永遠都是預設樣式**,
紅、藍、任何顏色都不是我們沒設,是那條路整個不存在。`PrimaryButtonStyle` 在整個套件裡
只出現在 `Microsoft.ui.xaml.dll`(框架本身),CmdPal 一次都沒用過。

那個對話框裡**唯一由擴展決定的畫素是主要按鈕上的字**:

```csharp
var name = string.IsNullOrEmpty(vm.PrimaryCommand.Name) ? confirmText : vm.PrimaryCommand.Name;
ContentDialog dialog = new() { Title = vm.Title, Content = vm.Description, PrimaryButtonText = name, ... };
```

`PrimaryButtonText` 就是我們 `PrimaryCommand.Name`。所以真要在那顆按鈕上見到顏色,
只剩一招:把 emoji 放進命令名(`Name = "🗑️ 刪除"`),emoji 字型本身是彩色的。
**現在沒有這樣做** —— 那是一顆彩色圖示,不是一顆紅色按鈕,而且跟整個介面的 Segoe Fluent
單色圖示放在一起會很突兀。要試的話改一行就行,回頭也只是改回來。

**自己畫一個確認畫面也換不到紅色按鈕。** Adaptive Cards 那條路試算過:CmdPal 的 host config
裡 `"attention"` 是 `#FF5555`(安裝版的 exe 裡掃得到 —— 注意掃法:它是 C# 字串常量,
存在 metadata 的 **#US heap,UTF-16**,跟方法名所在的 UTF-8 #Strings heap 不同堆;
照型別名那套 UTF-8 掃法會得到 False,掃字串常量要兩種編碼都掃),所以**卡片上的文字可以
是紅的**;但按鈕不行 —— AdaptiveCards 的 `Action.Style = "destructive"` 是靠宿主提供
`Adaptive.Action.Destructive` 這個資源鍵去查樣式的(那兩個字串在
`AdaptiveCards.Rendering.WinUI3.dll` 裡),而 CmdPal 的 `resources.pri` 裡**只定義了
`Adaptive.TextBlock`**,查不到就退回預設按鈕。換句話說:多做一頁、失去「Enter 直接確認」的
手感,只換到一行紅字。不做。

<a id="critical-red"></a>

### 刪除的紅色只有一個地方碰得到

上一節講的是確認框:那裡沒有任何顏色的開口。但**選單裡的那一列有** ——
`CommandContextItem.IsCritical`,SDK 的 IDL 對它的註解就一句話:

```idl
Boolean IsCritical { get; };   // READ: "make this red"
```

CmdPal 拿它做的事是換一整個 `DataTemplate`(`ContextItemTemplateSelector` 挑
`CriticalContextMenuViewModelTemplate`),圖示、標題、右邊那個鍵位字串三個都套
`SystemFillColorCriticalBrush`。**這條路在 0.11.11762.0 安裝版上是通的**,
不是只有 `main` 有(byte-scan 對照過,兩邊都掃得到):

```powershell
$d = "C:\Program Files\WindowsApps\Microsoft.CommandPalette_0.11.11762.0_x64__8wekyb3d8bbwe"
# Microsoft.CmdPal.UI.exe → ContextItemTemplateSelector / get_IsCritical
# resources.pri(UTF-16)  → CriticalContextMenuViewModelTemplate /
#                           ContextItemTitleTextBlockCriticalStyle
```

設在兩個地方:清單頁的「刪除」,以及刪除頁每一列選單裡的「直接刪除 / 刪除」。

**別跟 `IsPrimaryCommandCritical` 搞混。** 名字像,是兩件事:

| | 屬性在誰身上 | 做什麼 | 0.11 安裝版 |
|---|---|---|---|
| `IsCritical` | `CommandContextItem` | 選單那一列變紅 | **有效** |
| `IsPrimaryCommandCritical` | `ConfirmationArgs` | 把確認框的預設按鈕設成「取消」 | **完全沒作用**(見上一節) |

碰不到的地方,一次講完:

| 哪裡 | 為什麼不行 |
|---|---|
| 底部工具列的按鈕(`Enter` / `Ctrl+Enter` 那兩顆) | `CommandBar.xaml` 裡兩顆都寫死 `SubtleButtonStyle`,沒有 critical 變體。所以刪除頁上那一列的「刪除 ⏎」是白的,同一個命令在 `Ctrl+K` 選單裡卻是紅的 |
| 確認框的兩顆按鈕 | `ConfirmationArgs` 只有四個屬性,紅色在上游是註解掉的 TODO(見上一節) |
| 清單列本身(「刪除全部 N 則」那兩列的圖示) | `ListItem` 沒有 `IsCritical`,glyph 圖示跟著主題前景色走。真要紅只能改成自備的圖檔(`IconHelpers.FromRelativePath` 吃 `.svg` / `.png`,CmdPal 自己就這樣用),為了兩列多帶一份資產與淺色/深色兩張圖,現在不做 |

## 身分與介面

<a id="command-ids"></a>

### 命令 Id 為什麼要寫死

`src/Inkling/CommandIds.cs` 裡那幾個字串是對外承諾,跟資料格式一樣不能改。
**它們現在還叫 `Notelet.*`** —— 那是這個擴展改名前的名字,理由見本節最後。

CmdPal 把使用者對命令做的設定 —— alias、全域快速鍵、釘選、fallback 的顯示規則與排序 ——
全部存在自己的 settings.json 裡,鍵就是命令的 `Id`。而**命令沒有設 `Id` 時 CmdPal 會現場算一個**:
`TopLevelViewModel.GenerateId` 拿 `ProviderId + DisplayTitle + Title + Subtitle` 去做 WyHash64。
也就是說標題變一個字,那個命令對 CmdPal 來說就變成了另一個命令,使用者設過的東西全部對不上。

**現在這件事比以前更要緊**:快速記下唯一的入口就是使用者自己設的 alias,而 alias 存的鍵
就是 `Id`。`Notelet.QuickCapturePage` 改一個字,使用者的 alias 當場失效,而且症狀是
「打 `! ` 沒反應」—— 看不出跟改標題有任何關係。

歷史教訓來自已經移除的 fallback,它的標題本來就跟著使用者打的字一直變:CmdPal 的
settings.json 裡曾經留下兩個 Inkling fallback 條目,把其中一個的雜湊反推回去,正好是標題
`記下:你好` —— 某次重新載入時搜尋框裡剛好是那句話。表現出來就是「改了一次設定,
快速新增就莫名其妙不會出現了,連改回原本的前綴也救不回來」。

(那兩個雜湊條目可能還躺在你的 CmdPal settings.json 裡,無害 —— CmdPal 會忽略對不上的鍵。)

#### 為什麼這些 Id 還叫 `Notelet.*`

這個擴展從 Notelet 改名成 Inkling 時,`CommandIds.cs` 那六個字串**刻意一個都沒改**。

改名前實際打開 CmdPal 的 settings.json 看過,那裡面有兩種鍵:

```json
"Aliases": {
  "! ": { "CommandId": "Notelet.QuickCapturePage", "Alias": "!", "IsDirect": false }
},
"ProviderSettings": {
  "Notelet_bf0n0751x5hse!App!Notelet": { "IsEnabled": true }
}
```

`Aliases` 的鍵是**純命令 Id** —— 條目裡沒有 PFN、沒有 provider 參照。帶 PFN 的只有
`ProviderSettings` 與 `PinnedCommands`。所以換套件身分時,只要這些字串不動,
使用者設過的 alias 就跟著新名字走;動了它們,alias 當場全部失效。

代價只有「新來的人看到 `Notelet.List` 會困惑」,而那用一段註解就解決了。
使用者永遠看不到這些字串 —— 它們不是介面文字,是設定檔的鍵。

**改完之後實地驗過,不是只從設定檔推論的。** 換完套件身分(PFN 從
`Notelet_bf0n0751x5hse` 變成 `Inkling_bf0n0751x5hse`)並重新註冊,三個 alias 全部還在:
`!` 進得了快速記下頁(placeholder 是「打字記下想法,`;;` 後面接內文…」)、
`@` 進得了新增筆記、`#` 進得了清單頁而且列得出筆記。CmdPal 主搜尋框裡那三列右邊
也照樣掛著 `#` `!` `@` 的徽章。`ProviderSettings` 的鍵帶 PFN,那一項確實跟著失效
(擴展被當成新的,預設啟用,所以看不出差別);`Aliases` 不帶,所以活下來了。

<a id="app-list-entry"></a>

### 套件刻意不出現在開始功能表

`Package.appxmanifest` 的 `uap:VisualElements` 上有一個 `AppListEntry="none"`,
**不要拿掉**。少了它,CmdPal 的結果裡會多出第五列「Inkling / Capture thoughts in
seconds, right in Command Palette」,按 Enter 完全沒有反應。

成因跟擴展沒有關係:這個套件對 Windows 來說是一個正常的已安裝應用程式,於是進了
開始功能表的應用程式清單,而 CmdPal 內建的應用程式搜索把清單裡的東西也列進結果。
按下去它就去啟動 `Inkling.exe` —— 而那支 exe 是純 COM server,`Program.cs` 沒收到
`-RegisterProcessAsComServer` 就只 `Console.WriteLine` 一行然後結束,擴展進程又沒有
主控台,所以畫面上什麼都不會發生。

**這一列跟〈為什麼 Reload 之後有時會冒出兩個 Inkling〉是兩回事**,查的時候別搞混:

| | 多出來的那一列 |
|---|---|
| 應用程式清單項 | 副標是 manifest 的 `Description`(英文),圖示是 Windows 從 `Square44x44Logo` 挑的,按 Enter 沒反應 |
| 重複的 provider | 副標是我們自己的資源字串(跟著介面語言),四個命令整組重複 |

驗法:`Get-StartApps | Where-Object { $_.Name -like '*Inkling*' }`,有東西就是前者。

微軟自己的〈[Packaging a CLI Executable as MSIX](https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/guides/packaging-cli)〉
對同樣形狀的套件(exe 不是給人點的)開的就是這個處方。屬性在基底 `uap` 命名空間裡,
最低版本 Windows 10 1511,我們的 `MinVersion` 是 19041,不用多加命名空間。

代價與不是代價的:Inkling 不再出現在開始功能表 —— 反正點了也沒用。**解安裝不受影響**,
設定 → 應用程式 → 已安裝的應用程式 照樣列得到,`Remove-AppxPackage` 也照樣能用。
**擴展的探索也不受影響**:CmdPal 走的是 `AppExtensionCatalog`,認的是
`windows.appExtension` 註冊,跟應用程式清單可見性無關 —— 加上這一行之後重新部署,
`tools/VerifyRegistration` 照樣列得到 Inkling,四個命令也照樣在。

<a id="ui-language"></a>

### 介面語言跟著 Windows 走

介面有英文、繁體中文、簡體中文三種,**沒有設定項** —— 看到哪一種由 Windows 的顯示語言決定。

字串全部在 `src/Inkling/Properties/` 的三份 `.resx` 裡,程式碼一律經由產生出來的
`Resources.<鍵>` 取用,語言選擇是 `ResourceManager` 照 `CultureInfo.CurrentUICulture`
自己處理的,我們沒有寫任何偵測。中性(fallback)那一份是**英文**:系統語言不在這三種裡面時
(法文、日文……)拿到的就是它。

| 檔案 | 對應 | 誰會拿到 |
|---|---|---|
| `Resources.resx` | 中性,英文 | 上面兩列以外的所有語言 |
| `Resources.zh-Hant.resx` | 繁體中文 | zh-TW / zh-HK / zh-MO |
| `Resources.zh-Hans.resx` | 簡體中文 | zh-CN / zh-SG |

`zh-Hant` 一份就夠是因為 .NET 的文化回落:`zh-TW` 的 parent 就是 `zh-Hant`。
不必為每個地區各放一份。

**這件事能成立的前提是命令 Id 已經寫死**(上一節)。CmdPal 沒設 `Id` 時是拿標題去算雜湊當
身分的 —— 那樣的話光是換一種語言,使用者的 alias、快速鍵、釘選就會全部對不上。
`CommandIds.cs` 在,所以標題可以自由翻譯。

實測過的四件事(都在這台機器上,Windows 顯示語言 `zh-TW`):

- **擴展進程拿得到使用者的顯示語言。** 它是 CmdPal 用 COM 拉起來的獨立進程,不是 CmdPal 的
  子視窗,所以這件事不能想當然。`diagnostic.log` 印的是 `UI 語言:zh-TW 抽樣='設定'`。
- **trimming 不會砍掉附屬組件。** Release 是 trimmed publish,`zh-Hant\Inkling.resources.dll`
  與 `zh-Hans\Inkling.resources.dll` 都完整進到 MSIX 佈局裡(套件大小沒有可見變化)。
- **回落是乾淨的。** 強制 `fr-FR` 拿到英文,不是空字串也不是例外。
- **CmdPal 自己沒有語言覆寫。** PowerToys 有些模組會照設定裡的 `language` 去套
  `ManagedCommon.Language.LoadLanguage()`,但 0.11.11762.0 的整個 CmdPal 套件 byte-scan
  `LoadLanguage` 掃不到,`main` 的原始碼裡設 `CurrentUICulture` 的也只有單元測試。
  所以擴展與 CmdPal 本體看到的是同一個語言,不會一半中文一半英文。

**為什麼不加一個語言設定項。** 想要「Windows 是英文、但 Inkling 顯示中文」的話得自己選語言,
而那會踩到〈設定頁有兩個入口〉那一節講的限制:CmdPal 手上握著的是使用者當下開著的頁面實例,
換語言等於每一頁的 `Title` / `Name` / `PlaceholderText` 與每一塊快取都要自己重算,
`ICaptureSeparatorStore` 那個形狀要再複製一遍。跟隨系統零成本,而真的需要換語言的人
去改 Windows 的顯示語言本來就要重新登入 —— 那時候擴展進程也一起重啟了。

**改語言之後沒有立刻變**是預期行為:擴展進程被 CmdPal 拉起來之後就常駐,
Reload 或重新登入才會重讀。

**改字串的規矩:三份一起改。** `Resources.resx` 是翻譯的來源,註解(`<comment>`)只寫在
它裡面 —— 佔位符 `{0}` 是什麼意思都寫在那裡。`ResourceParityTests` 會擋住只改一份、
佔位符數目對不上、值是空的,以及「英文那份混進中文」。

<a id="icons"></a>

## 圖示

原始檔是 `assets/icon/` 底下的七個 SVG,`src/Inkling/Assets/*.png` 全部由
`tools/render-icons.ps1` 產生 —— **不要手改那些 PNG**,改圖示請改 SVG 再跑一次腳本。

構圖是「一道有壓感的下筆 + 一顆句點」:起筆重、收筆輕,最後點一下收尾。
取自 inkling 的字面 —— 一點墨水、一個還沒成形的念頭。句點是整張圖唯一的彩色元素。

**刻意避開的東西**:捲角便條紙、鉛筆加紙、記事本。那三個是 Notepad / Sticky Notes /
OneNote 的符號,而「在 CmdPal 清單裡長得像它們」正是當初要改名換圖示的理由之一。

三個取捨都是實際渲染到 20px 看過才定的:

| 決定 | 為什麼 |
|---|---|
| 筆畫直立,不斜 | 第一版起筆帶橫向,縮到 24px 讀成數字「7」 |
| 句點在右下,不在筆畫正上方 | 放上方會讀成小寫字母 `i` |
| 滿版圓角磚,不是去背形狀 | `BackgroundColor` 是 `transparent`,連 plated 那一版也沒有系統底板 —— 單色去背的形狀在深色主題會直接消失 |

原始檔的分工:

| 原始檔 | 用在 | 差別 |
|---|---|---|
| `inkling-tile.svg` | 150×150 以上 | 標準比例 |
| `inkling-tile-small.svg` | 88px 以下(工作列、CmdPal 清單) | 筆畫放大約 8%、粗細差拉開,句點從 `r=18` 放大到 `r=25` |
| `inkling-wide.svg` | 寬磚與啟動畫面 | 標記縮到 70% 置中在寬底板上 |
| `inkling-cmd-list.svg` 等四個 | 四個頂層命令 | 24 格線單色,見下 |

精細版的收筆端在 24px 只剩不到一像素,會直接斷掉;句點縮下去也剩不到兩像素。
小尺寸版因此重畫過 —— 這是圖示設計的常規做法(optical sizing),
兩份的顏色與圓角比例一致,並排看得出是同一個。

#### `Square44x44Logo` 的兩條候選階梯要各自補齊

Windows 用 MRT 從檔名的限定詞挑圖,而 `Square44x44Logo` 有**兩條分開挑的**階梯:
沒帶 `altform` 的請求走 `.scale-*`,要 unplated 的地方(應用程式清單、工作列按鈕)
走 `.targetsize-*_altform-unplated`。**一條裡有大圖救不了另一條。**

Visual Studio 模板只給兩張:`scale-200`(88px)與 `targetsize-24_altform-unplated`(24px)。
於是要 unplated 的地方永遠只有 24px 可挑 —— 這台是 150% DPI,清單列上要 30px,
它就把 24 放大,看起來是糊的。**同一個畫面上四個命令的圖示卻很銳利**,因為那些的
來源是 48px 往下縮。兩張圖並排就看得出來,而這不是渲染器或 SVG 的問題。

所以兩條都補齊:`scale-100/125/150/200/400` 與
`targetsize-16/24/32/48/256_altform-unplated`,全部由 `render-icons.ps1` 產生。
沒有再另外出「plated」(不帶 `altform`)的 `targetsize` 變體 —— `BackgroundColor` 是
`transparent`,Windows 不會畫底板,兩者長得一樣,而沒帶 `altform` 的請求本來就落在
`scale-*` 那條上。

(套件加了 `AppListEntry="none"` 之後應用程式清單那條路不會再被走到,
但工作列按鈕還在 —— 設定頁的「瀏覽…」對話框刻意不掛 owner,靠的就是那顆按鈕。)

### 四個頂層命令用自訂圖示,Ctrl+K 選單維持字形

`Icons.TopLevelList` / `TopLevelCapture` / `TopLevelNew` / `TopLevelDelete` 是自己畫的
PNG,其餘全部維持 Segoe Fluent。界線是這樣劃的:

- **頂層命令**出現在 CmdPal 主搜尋框的結果裡,要一眼看得出是同一個產品 —— 走自訂。
- **`Ctrl+K` 選單與頁面內**跟 CmdPal 內建命令混在一起,字形反而更協調;
  而且 Segoe 在 16/20px 有專業 hinting,手畫的比不上 —— 走字形。

代價講明白:**刪除那一個變弱了。** 垃圾桶(`0xE74D`)比「筆畫＋叉」一望即知,
而刪除是四個裡最需要一眼認得的。這是為了家族一致刻意付的,不是疏忽 ——
覺得誤刪風險比較重要的話,把 `Icons.TopLevelDelete` 改回 `Glyph(0xE74D)` 就好。

**一個命令要兩張 PNG。** 字形是以文字繪製的,前景色自動跟主題走;PNG 不會。
所以每個命令備了淺色主題(深色前景)與深色主題(白色前景)兩張,交給
`IconHelpers.FromRelativePaths(light, dark)` 去挑。少了這一層,深色主題下圖示會整片看不見。
`render-icons.ps1` 用同一份 SVG 渲染兩次,差別只在注入的 `color`。
那一行的 `!important` 拿不掉:SVG 檔案自己帶 `style="color:..."`(方便單獨開起來看),
而行內樣式優先權高過選擇器 —— 少了它兩張 PNG 會長得一模一樣,而且不會報錯。

**修飾符的挖空用 `mask`,不是拿底色填。** 右下角那個徽章要跟筆畫分開,
第一版是畫一個白色圓形蓋上去 —— 那在這裡是錯的:這幾張輸出成去背 PNG 疊在
CmdPal 的清單列上,而那個背景是 Mica、半透明的,填死的白圓在深色主題會變成一塊亮斑。
`mask` 才是真的把筆畫挖穿。

渲染器用的是 Chromium(Chrome 或 Edge,哪個在就用哪個):這台機器沒有 ImageMagick /
Inkscape / rsvg,而 .NET 不會解 SVG。重點是它**以目標尺寸直接向量渲染**,不是先畫大張
再縮圖,所以 24px 的邊緣是乾淨的。腳本裡有一個容易踩的地方:svg 的 CSS 尺寸要寫死成
目標像素,不能用 `100vw`/`100vh` —— headless 的版面視窗寬度不等於 `--window-size`,
用相對單位截出來會是偏移又放大的半張圖。

### `Assets` 一定要 `CopyToOutputDirectory`

`Inkling.csproj` 把圖示收成 `Content` **並且**設了 `CopyToOutputDirectory`。少了後者,
那些 PNG 不會進建置輸出,而我們是以 loose file 註冊建置輸出當套件的 ——
`AppxManifest.xml` 裡每一個 `Logo` 都會指向不存在的檔案。**症狀很騙人**:套件照樣註冊
成功、擴展照樣能用,只是所有圖示變成 Windows 的預設灰方塊,看起來像「圖示做壞了」。
`IconHelpers.FromRelativePath` 也一樣讀不到 —— 它組的是 `BaseDirectory` 底下的實體路徑,
不走 MRT,所以 CmdPal 清單裡那一列也會是空的。

<a id="dev-notes"></a>

## 開發考證

### 為什麼 Reload 之後有時會冒出兩個 Inkling

CmdPal 那邊的問題,不是擴展的。重新註冊套件會讓 Windows 的套件目錄發出**安裝**事件
(套件版本從頭到尾都是 0.1.0.0,所以它算是重裝而不是升級 —— 升級走的是「先移除再安裝」,
反而不會出事)。CmdPal 收到安裝事件後會替同一個擴展再建一個 `CommandProviderWrapper`,
而 `TopLevelCommandManager.RegisterAndLoadCommandsAsync` 是直接 `AddRange`,不去重。

手動 Reload 如果搶在那個非同步事件之前跑完,被清掉的是舊清單,事件補進來的就成了第二個。
所以 `deploy.ps1 -Reload` 會先等幾秒再送重新載入。已經看到兩個的話,**再 Reload 一次不一定收得回去** ——
實測兩次重新部署都出現重複,兩次都是 Reload 之後照樣是兩個,把 `Microsoft.CmdPal.UI`
進程停掉讓它重啟才乾淨。PowerToys 本身不用重開。

同一個根源還有一個更會騙人的症狀:**Reload / 重新部署之後,之前開著的設定頁是綁在
舊擴展實例上的死物件**,按 Save 靜靜地什麼都不做 —— 不寫檔、不重建、不報錯。
查「改設定沒反應」之前,先把設定頁關掉重開(README 疑難排解也有這條)。

### 改了 manifest 之後部署會撞 `0x80073CFB`

`Add-AppxPackage -Register` 在「同一個位置、同一個版本、但 `AppxManifest.xml` 的內容
變了」時會失敗:`0x80073CFB —— 已安裝過提供的套件,不允許重新安裝`。
`deploy.ps1` 本來只處理「位置不同」(Debug ↔ Release 互換)那一種,所以第一次在
manifest 上加屬性時就撞上了。

錯誤訊息建議「遞增要安裝之套件的版本號碼」—— **不要照做**,版本號是對外的東西,
不該為了本機部署動它。移除註冊再登錄一次就好,`-PreserveApplicationData` 保住
`LocalState` 裡的設定。`deploy.ps1` 現在會接住這個 HRESULT 自己重試一次。

代價是這條路一定會經過「移除 → 重新安裝」,也就是上面那個重複 provider 的觸發條件,
所以改完 manifest 的那一次部署要多留意有沒有變成兩個。

### 查 SDK 的實際簽章

Microsoft Learn 上的 Command Palette API 參考有些頁面是 2025 年初寫的,跟 0.11 的實際
簽章對不上(至少 `FallbackCommandItem` 的建構子與 `KeyChordHelpers.FromModifiers` 的
參數個數都不一樣)。與其靠編譯錯誤一次次試,直接問組件:

```powershell
dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult ListItem
dotnet run --project tools\ApiDump -- --paths     # 設定檔存在哪
```

<a id="perf-rules"></a>

### 效能上的規矩

需求裡有一條「擴展不能拖慢 Command Palette」,對應到程式碼是三件事:

- `TopLevelCommands()` 絕不碰磁碟。CmdPal 一啟動就會呼叫它。
- `GetItems()` 每按一鍵就會被呼叫一次,所以筆記有記憶體快取,搜索是純字串比對不用 regex,
  同一個查詢字串不重建項目(快取的形狀三個清單頁共用,見 `VersionedItemsCache`)。
- 清單一次最多送 200 則(每個項目都要跨進程 COM 封送)。被截斷時清單最後會明講
  還有幾則,不會默默少東西。

`tests/Inkling.Core.Tests/PerformanceTests.cs` 是這幾條的防退化警戒線。
