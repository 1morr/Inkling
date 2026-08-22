# 已知缺陷

還沒修的東西。**每一條都在真機上重現過或讀原始碼確認過**,不是猜測。

這份文檔跟 [`design-notes.md`](design-notes.md) 的分工:那邊記「為什麼是這樣」(已經決定的
取捨),這邊記「這樣是錯的,只是還沒修」。**一條修掉就從這裡刪掉** —— 修好的東西留在這裡
比沒寫更糟,它會讓人以為問題還在。行為變了就同輪更新兩份 README、
[`manual-test-checklist.md`](manual-test-checklist.md) 與 [`CHANGELOG.md`](../CHANGELOG.md)。

**「查過、量過,然後決定不做」的東西不進這裡**,進
[設計考證〈評估過但沒有做〉](design-notes.md#deferred)。差別是:那邊是決定,這邊是債。

嚴重度:**阻擋發布** / **應修** / **建議**。發現日期一律 2026-08-22(首次公開發佈前的
總體檢),來源見該輪的稽核紀錄。

---

## 資料完整性

<a id="k-1"></a>

### K-1 同一個 `id` 的兩份檔案,編輯與刪除只作用在其中一份 —— 阻擋發布

**現象**

`FileSystemNoteRepository.GetById` 是 `GetAll().FirstOrDefault(n => n.Id == id)`
(`src/Inkling.Core/FileSystemNoteRepository.cs:150-151`),而 `Update`(`:207`)與
`Delete`(`:230`)都經由它解析目標。資料夾裡有兩個檔案帶著同一個 front matter `id` 時,
清單會列出兩列,但**兩列都指向同一份檔案**。

**重現**(實測兩次)

1. 放兩個檔案,front matter 的 `id` 相同、`title` 與內文不同:

   ```
   dupe-a.md   id: 20260820-9999-dupe   title: 重複ID甲   內文:我是甲
   dupe-b.md   id: 20260820-9999-dupe   title: 重複ID乙   內文:我是乙
   ```

2. 清單出現兩列。選中「重複ID乙」按 `Ctrl+E` → 表單的頁面標題寫著
   **「編輯：重複ID乙」**,但欄位帶出來的是**甲的內容**(`內文=我是甲`、`標題=重複ID甲`)。
3. 改一下按儲存 → 寫進 `dupe-a.md`,`dupe-b.md` 一個位元組都沒動。

**為什麼是阻擋級**

這不是造出來的情境,它就是 OneDrive 的衝突副本。多台機器同時改同一則筆記時,OneDrive
會產生 `<檔名>-<電腦名>.md`,那是**整檔複製**,`id` 一模一樣。用真實檔名重跑一次
(`20260822-1200-會議記錄.md` + `20260822-1200-會議記錄-DESKTOP-A1B2C3.md`)結果相同。

而「把筆記放進 OneDrive、同步交給它」正是這個擴展的賣點,兩份 README 也曾經明著寫
「nothing is lost… you decide which to keep」—— 那句話已經改掉了(改成「在檔案總管或
編輯器裡處理衝突副本」),但那是繞過去,不是解決。

**建議修法**

身分不能只看 `id`。`Update` / `Delete` 內部本來就已經拿著 `existing.FilePath`,
把入口從「傳 id」換成「傳 `Note`」即可,呼叫端(清單列、預覽頁、刪除頁)手上都有那個物件。
`GetById` 保留給真的只有 id 的路徑,但命中多於一筆時要講出來 —— 清單那一列可以用
`ListItem.Tags` 標一個「衝突」(那條路是活的,見
[設計考證〈複製完留在原地〉](design-notes.md#copy-feedback))。

<a id="k-2"></a>

### K-2 `settings.json` 一旦不是合法 JSON,設定永久性地、無聲地存不回去 —— 應修

**現象**

- 讀:toolkit 的 `LoadSettings` 把例外吞掉 → 四項設定全部退回預設 → **筆記資料夾變回
  `%OneDrive%\Inkling`**,使用者的清單換成別的內容。
- 寫:`SaveSettings` 內部 `JsonNode.Parse(舊內容)` 也失敗 → 走 else 分支 → **完全不寫檔**。
  而 `SettingsManager.Save`(`src/Inkling/SettingsManager.cs:229-243`)看不到任何例外,
  回傳 `true`,於是 `:209` 的 `return ApplyResult.SaveFailed` 與
  `InklingSettingsForm.cs:105-110` 那個 case **永遠到不了**,設定頁照樣走成功路徑。

**重現**

把 `settings.json` 改成 `{ "Inkling.NotesDirectory": "…", , }`(多一個逗號)→ Reload →
清單顯示的是預設資料夾的內容;進設定頁改分隔符按儲存 → 表單走成功路徑(`GoHome()` 回主頁)、
檔案位元組**完全沒變**、重啟又還原。**使用者在 app 內修不好它**,唯一的解是手動刪檔,
而使用者不會知道要去做。

**觸發來源**:手改多打一個逗號;或**寫檔中途斷電 / 當機** —— toolkit 走的是
`File.WriteAllText`,**不是** atomic write(我們自己寫筆記時是走 `AtomicFile.cs`,
設定檔沒有這個保護)。

**建議修法**

`SettingsManager` 建構子在 `LoadSettings()` 之前先 `JsonNode.Parse` 試一次,失敗就把檔案
改名成 `settings.json.corrupt-<時間戳>` 隔離掉,並 `DiagnosticLog.Failure` 留痕。另外讓
`Save` 真的驗證寫入結果(寫完讀回來比對),`ApplyResult.SaveFailed` 才不會是死路 ——
`SettingsManager.cs:219-228` 與 `InklingSettingsForm.cs:119-127` 兩段註解都明言要修掉這個
失敗模式,實際上一行都沒修到。

<a id="k-3"></a>

### K-3 非 UTF-8 的外來 `.md` 被讀成亂碼,編輯存檔後原始位元組永久消失 —— 應修

**重現**

放一個 Big5 編碼、無 BOM 的 `.md`(內容 `# 這是一個 Big5 編碼的筆記`):

1. 清單標題顯示 `# �o�O�@�� Big5 �s�X�����O` —— 每個非 ASCII 位元組變成 U+FFFD。
2. `Ctrl+E` → Tab → Tab → Enter 存檔。
3. 檔案被改寫成 UTF-8,**U+FFFD 被烤進去**,Big5 再也解不回來;而且 Inkling 順手塞進了
   front matter(`id: file-…` / `created` / `updated`,`title` 就是那串亂碼)。
4. **資源回收筒項目數前後相同(2740 → 2740)**,沒有任何備份;資料夾裡也沒有 `.tmp`。

**位置**:`src/Inkling.Core/FileSystemNoteRepository.cs:394-405`(`File.ReadAllText`,
預設 UTF-8 且**不 throw**,無效位元組直接換成 U+FFFD)、`src/Inkling.Core/AtomicFile.cs`
(`File.Move(overwrite: true)` 就地換掉)。

**為什麼要修**:[CLAUDE.md](../CLAUDE.md)〈慣例〉把「沒有 front matter 的外來 `.md` 也要能
列出來」列為資料格式承諾。把外來檔案默默轉碼成亂碼並覆寫,是那條承諾的反面。

**建議修法**:讀檔改用 `new UTF8Encoding(false, throwOnInvalidBytes: true)`,失敗就計進
`SkippedFileCount` —— 那條路已經有畫面了(清單最後那一列「有 N 個檔案讀不出來」),
訊息也已經是「檔案還在資料夾裡」的口徑,正好對得上。退一步的做法是把那則標成唯讀不給編輯。

<a id="k-16"></a>

### K-16 日期欄位解析失敗時被靜靜丟棄並改寫 —— 建議

`NoteFile.cs:246-251` 把 `created` / `updated` 交給 `ParseDate`(`DateTimeOffset.TryParse` +
`InvariantCulture`),失敗回 `null` → `TryReadNote` 改用檔案系統時間 → 下一次在 Inkling 裡
編輯就把原字串**永久覆蓋**。原始那一行**不會**落進 `ExtraFrontMatter`(它在認得的 switch
分支裡被消化掉了),所以「不認得的欄位原樣保留」那條承諾在這裡不成立。

兩種觸發,第二種更糟因為完全無聲:

- `created: 2024-01-05 (approx)` → 編輯一次 → 變成檔案建立時間,原字串消失。
- `created: 05/01/2024`(dd/MM,多數非美式工具的寫法)→ InvariantCulture 讀成 **5 月 1 日**
  → 寫回 `2024-05-01T…`。日期被默默改掉,而且是永久的。

**建議修法**:解析失敗時把原行推進 `ExtraFrontMatter`,或在 `Serialize` 時保留原字串。

---

## 使用者看得到的行為

<a id="k-4"></a>

### K-4 `CommandResult.GoBack()` 不動,編輯表單存完停在原地 —— 應修

`NoteFormContent.cs:114` 的 `AfterSave` 對編輯回傳 `GoBack()`、對新增回傳 `GoHome()`。
實測:新增存完**確實**回到主頁;編輯存完**停在編輯頁不走**(等五秒也一樣),只有底部的
InfoBar「已儲存：<標題>」會出現。同一個 `SubmitForm`、同一個回傳路徑,差別只在回傳哪一種
`CommandResult`,所以不是我們的程式沒走到那一行。

跟 `CommandResult.GoToPage` 是同一類的空殼(見 [CLAUDE.md](../CLAUDE.md) 硬規則 8)。
**能用的只有 `GoHome` / `Dismiss` / `KeepOpen` / `Confirm` / `ShowToast`。**

**還沒決定怎麼處理**:接受它並把文檔改成「存完留在原地,自己按 `Esc` / `Alt+Left`」,
或者改成 `GoHome()`(但那會把使用者丟回主頁,比停在原地更遠),或者讓表單存檔後由回呼
主動導頁。三個都有代價,先記著。

<a id="k-6"></a>

### K-6 編輯表單的 `Enter` 會跳去外部編輯器並丟掉卡片上未存的輸入 —— 應修

`NoteEditPage.cs:32-59` 的 `Commands` 只有一項:
`OpenNoteFileCommand(note.FilePath, dismissOnSuccess: true)`。因為 `ContentPage` 的底部
工具列主命令就是 `Commands[0]`(見 [CLAUDE.md](../CLAUDE.md) 硬規則 8),**`Enter` 就是它**。

**觸發**:進編輯表單後焦點在多行的內文框(那裡 `Enter` 是換行,碰不到),但按一次 `Tab`
就到**單行的標題框** —— 在單行輸入框裡按 `Enter` 是很自然的「送出」手勢,結果是外部編輯器
被打開、面板被 `Dismiss` 收掉,卡片上打過的字全部消失。

這個危險是**已知的**:`NoteEditPage.cs:52-57` 的註解承認了(「實機驗過」),
[`CHANGELOG.md`](../CHANGELOG.md) 也寫著「焦點在單行標題欄時很容易誤觸,卡片上未儲存的
修改會靜靜消失。副標現在把這個代價講出來」。**但目前的處置只是在副標裡警告,行為沒改。**

而那段註解下的結論「**Enter 本身收不回來**」—— **那句話不對**。同一個 repo 裡就有反例:
`ScratchpadPage.cs:42-45` 刻意把無害的「捨棄變更」放在 `Commands[0]`、把跳外部編輯器
推到 `Commands[1]`(`Ctrl+Enter`);`NewNotePage` 與 `InklingSettingsPage` 則根本沒有設
`Commands`。所以 `Commands[0]` 是可控的。

**建議修法**:照 `ScratchpadPage` 的形狀,把跳出去那一項降到 `Commands[1]`,`[0]` 放一個
明確無害的東西。注意 `Commands[0]` **不能**是「儲存」—— 底部工具列走的是無參數的
`ICommand.Invoke()`,拿不到使用者剛打的字(`ScratchpadPage.cs:38-41` 已經記過這件事),
所以只能是一顆標著「繼續編輯」之類、回傳 `CommandResult.KeepOpen()` 的空命令。
順手在 `tests/Inkling.Tests/PageCommandOrderTests.cs` 加一條斷言把 `Commands[0]` 釘住。

<a id="k-5"></a>

### K-5 「只刪 Inkling 建立的」會刪掉別人的 vault 檔 —— 應修

判準是 `IsExternal = parsed.Id is null`(`src/Inkling.Core/FileSystemNoteRepository.cs:387`)
—— 只要 front matter 裡有 `id:` 這個 key,就被算成 Inkling 建立的。

**重現**:資料夾裡三個檔案 —— 一則 Inkling 筆記、一個 Zettelkasten 風格的
`zettel.md`(`id: 202401051200` / `title:` / `tags: [zettel]`)、一個完全沒有 front matter 的
`plain.md`。刪除頁顯示「刪除全部 **3** 則」/「只刪 Inkling 建立的 **2** 則」/
「保留 **1** 則不是 Inkling 建立的」——**Zettelkasten 那個檔被算成我們的**。

`id:` 在 Obsidian / Zettelkasten / Hugo 生態裡極常見。把筆記資料夾指到既有 vault 的使用者,
按下那顆「保留不是我建立的」按鈕時,反而會刪掉自己的東西(進資源回收筒救得回,但畫面上的
承諾是假的)。

這**不是** design-notes 記過的取捨 —— 那裡記的是「外來檔案也要列得出來」,而這條是那個
減災措施本身失效。

**建議修法**:判準改成「有 `id` **而且** 形狀是我們產的」(`^\d{8}-\d{6}-[0-9a-f]{4}$`,
見 `NoteFileName.cs:22-26`)。不需要改任何既有檔案,也不需要新的 front matter 欄位。

<a id="k-7"></a>

### K-7 快速記下頁會帶著上一次的字進來,反射性 `Enter` 就多存一則重複筆記 —— 應修

**重現**

1. `! ` → 打 `殘留測試乙` → `Enter`(存檔,進記下並預覽頁)→ `Enter`(完成,toast,面板關閉)。
2. 之後用**任何非 alias 的路徑**回到快速記下頁 —— 例如清單頁搜一個搜不到的字,
   再對「找不到符合的筆記」那一列按 `Enter`。
3. 搜尋框裡還是 `殘留測試乙`,第一列是「記下：殘留測試乙 / 存成新筆記」而且是選中的,
   第二列是剛剛存好的那一則。再按一次 `Enter` 就是重複筆記。

**為什麼平常看不到**:alias 觸發時 CmdPal 會送 `ClearSearchMessage`(見
[CLAUDE.md](../CLAUDE.md) 硬規則 3),把它蓋掉;用 `Esc` 退出頁面也會清掉。
只有「被『完成』關掉面板」這條路會把字留下來,而那正是最常走的路。

**位置**:`src/Inkling/Pages/QuickCapturePage.cs` 全檔沒有任何對 `SearchText` 的寫入,
而這個頁面是 `ProviderState` 持有的長壽實例。

**建議修法**:存檔成功後把 `SearchText` 設回空字串。

<a id="k-9"></a>

### K-9 `Section` 是死碼,分節標頭從來沒有出現過 —— 應修(小)

`DeleteNotesPage.cs:127,143,162,280,308` 與 `QuickCapturePage.cs:231,300` 在**有命令的**
`ListItem` 上設 `Section`,而 CmdPal 只在 `Command` 為 null 的列上才把它當標頭文字用。
實測:UIA 樹裡沒有任何標頭列,截圖上也沒有。

機制與「要真的做出標頭該怎麼做」寫在
[設計考證〈分節標頭:`Section` 不是分組鍵〉](design-notes.md#section-not-grouping)。

**兩條路,都可以**:插 command-less 的標頭列把它做出來,或者把那七處賦值刪掉。
**不能維持現狀** —— 現在是「程式碼設了、文檔寫了、畫面沒有」。

<a id="k-8"></a>

### K-8 摘要與推導標題的 120 字截斷會把代理對切成兩半 —— 應修(小)

`src/Inkling.Core/Note.cs:69`(`line[..NoteBody.MaxLineLength]`)與 `:75`
(`line.AsSpan(0, NoteBody.MaxLineLength)`)都是裸索引切割。第一行有效文字超過 120 個
UTF-16 字元、而第 120 個位置正好落在代理對中間(emoji、擴充區漢字)時,尾端會留下落單的
high surrogate,畫面上是 �。

**同一個問題在檔名那條路已經修過而且有測試**:`src/Inkling.Core/NoteFileName.cs:70`
明確判 `char.IsHighSurrogate(slug[cut - 1])`,測試是
`tests/Inkling.Core.Tests/NoteFileNameTests.cs:53 Slug_DoesNotSplitSurrogatePairs`。
摘要這條沒有等價保護,測試裡也沒有對應案例。

**建議修法**:抄 `NoteFileName.cs:70` 那三行,順手補一條測試。

<a id="k-10"></a>

### K-10 設定頁三個設定項之間沒有分隔線 —— 建議

卡片有宣告 `"separator": true`(`src/Inkling/Pages/InklingSettingsForm.cs:272,305`),
但畫面上看不到線(截圖逐列掃描 y=230..300,找不到任何整列非背景色的像素)。純視覺,
不影響任何行為。要嘛查出為什麼沒渲染,要嘛把手動驗證清單那條 👀 項刪掉。

---

## 隱私與診斷

<a id="k-12"></a>

### K-12 `DiagnosticLog.Failure` 把筆記標題與使用者名字寫進 CmdPal 的共用 log,而且訊息是中文 —— 應修

`src/Inkling/DiagnosticLog.cs:43-58` 的 `Failure` 走
`ExtensionHost.LogMessage($"[Inkling] {message}")`,目的地是
`%LOCALAPPDATA%\Microsoft\PowerToys\CmdPal\Logs\<版本>\` —— [CLAUDE.md](../CLAUDE.md)
自己註明那份 log **永遠開著**,而且是所有擴展共用的。

**帶路徑進去的呼叫點**:`Commands/OpenNoteFileCommand.cs:70`、`:77`、
`Commands/ShowNoteInFolderCommand.cs:55`、`SettingsManager.cs:170`。路徑形如
`<筆記資料夾>\<時間戳>-<標題 slug>.md`,所以同時帶著**筆記標題**與(經 `%OneDrive%` 或
`Documents`)**Windows 使用者名**。另外八個 `Failure($"… {ex}")` 會把例外訊息裡的路徑
一起送出去。

**真正的外洩路徑繞過我們的 issue 範本**:PowerToys 自己的 Bug Report Tool 會把整個
`%LOCALAPPDATA%\Microsoft\PowerToys\` 打包,使用者拿去貼在 `microsoft/PowerToys` 的公開
issue 上,完全看不到 `.github/ISSUE_TEMPLATE/bug_report.yml:67-73` 的遮蔽提醒。

**附帶**:14 個 `Failure` 訊息全是繁體中文,違反 [CLAUDE.md](../CLAUDE.md)〈慣例〉
「**永遠英文**:識別符、字串常量、**log 訊息**…」。這一條在別處可能無所謂,但這份 log
是所有擴展共用、被 PowerToys 維護者拿來 triage 別人的 bug 的 —— `[Inkling]` 前綴認得出
是誰寫的,訊息本身除了我們沒人讀得懂。

**建議修法**:完整路徑只寫進 opt-in 的本機 `diagnostic.log`(走 `Write`),共用通道只送
失敗種類;所有 `DiagnosticLog` 訊息改英文。

<a id="k-17"></a>

### K-17 `diagnostic.log` 沒有大小上限,而且一條熱路徑在寫它 —— 建議

`NoteListPage.cs:167` 每次清單重建(≈每個按鍵)就寫一行,內容包含**使用者打的查詢字串**
與命中數。開著跑幾十分鐘就十幾 KB,而且沒有輪替。使用者照排錯指示建了 `diagnostic.on`
之後忘了刪,這個檔案會無上限成長 —— 附進 bug report 時等於交出搜尋歷史。
一個「超過 N MB 就砍掉重來」的判斷就夠。

---

## 發版與工具鏈

這一區的每一條都必須在**第一個公開版本出去之前**處理掉,否則代價會跟著使用者一起放大。

<a id="k-13"></a>

### K-13 上架之後,所有 `Get-AppxPackage -Name Inkling` 都會落空 —— 應修(發版前)

Microsoft Store 會**重指派** `Identity` 的 `Name` 與 `Publisher`。本機兩個已上架的
CmdPal 擴展就是證據:

```
AdvaithAJ.CurrencyConverterforCommandPalette   Publisher = CN=4F1E0968-…
NickXii.PowerTranslator                        Publisher = CN=F48B8B01-…
```

`Name` 變成 `<發行者>.<名稱>`、`Publisher` 變成 `CN=<GUID>`。而 `-Name Inkling` 是**精確
比對**,不是萬用字元,所以上架之後這些全部回 `$null`:

- `tools/deploy.ps1:67`(`$packageName = 'Inkling'`,用在 `:84`、`:140`、`:169`、`:175`)
- `tools/cmdpal-ui.ps1:211`
- `tools/VerifyRegistration/Program.cs:12`
- **`.github/ISSUE_TEMPLATE/bug_report.yml:50`** —— **必填**欄位,要每個回報者貼
  `(Get-AppxPackage Inkling).Version` 的輸出
- **`.github/ISSUE_TEMPLATE/bug_report.yml:86`** —— 教他們怎麼找 `<PFN>`

後兩處是對使用者的,從第一個 Store 版本起每個回報者跑那兩行都會得到空的。

`docs/release-checklist.md` §1 說硬編碼 PFN 已經清乾淨了 —— **只清掉一半**:PFN 換成了
動態查詢,但每個動態查詢都以字面值 `Inkling` 當鍵,而那正是 Store 會換掉的東西。

**建議修法**:改用 `Get-AppxPackage *Inkling*`,或把套件名收成單一變數;並在真的換身分那
一輪重跑一次 §1 的掃描,用**新的** `Name`。

<a id="k-14"></a>

### K-14 未簽章的資產被掛上公開 GitHub Release,而說明裡沒有任何提示 —— 應修(發版前)

目前**完全沒有簽章**:機器與 repo 的憑證存放區都沒有 subject 含 Inkling/Notelet 的憑證,
repo 樹內沒有 `*.pfx` / `*.p12` / `*.cer`,`Inkling.csproj` 沒有 `AppxPackageSigningEnabled`
之類的屬性,`Get-AppxPackage Inkling` 的 `SignatureKind` 是 `None`。

`release.yml` 在沒有 `SIGNING_CERT_BASE64` 時跳過兩個簽章步驟(正確),但仍然**無條件**
`gh release create --generate-notes` 附上 msix 與 bundle。使用者下載後雙擊會拿到
`0x800B0109`,而 `--generate-notes` 的正文是從 commit message 產的,不會提這件事。
唯一寫著這件事的地方是 `README.md` 的安裝章節與一行 YAML 註解 —— 沒有人會看到。

對一個**首次公開**發佈,這是最差的第一印象。

**建議修法**:沒有簽章時把 release 建成 draft 或 prerelease,或改用顯式 `--notes`
說明那些資產是 Store 送審用、不能側載。

<a id="k-15"></a>

### K-15 `release.yml` 的版本 regex 放行 Store 不收的版本號 —— 應修(發版前)

`.github/workflows/release.yml:67` 是 `if ($tag -notmatch '^\d+\.\d+\.\d+$')`,所以
`0.2.0` 會通過驗證、`makeappx` 也會照打。但 Microsoft Learn〈App package requirements for
MSIX app〉寫明版本各段「must be set to an integer between 0 and 65535 (**except for the
first section, which cannot be 0**)」。

於是失敗會發生在**最貴的時機**:CI 全綠、資產都在、上傳 Partner Center 才被退,而 tag
已經推上去了(同一個版本號在 Store 端也已經被佔掉,不能重推)。

而 `docs/release-checklist.md` 的範例正是 `git tag v0.2.0` —— **照著文檔做就會踩到**。
(那兩處範例已經改成 `v1.x`。)

**建議修法**:regex 收成 `^[1-9]\d*\.\d+\.\d+$`。第一個公開版本從 `v1.0.0` 起。

<a id="k-11"></a>

### K-11 `CommandIds` 的七個字串沒有任何自動化把關 —— 應修

`grep -rn "Notelet\.\|CommandIds" tests/` **零命中**。`PageCommandOrderTests` /
`ListPageCacheTests` / `PageDisposeTests` 都不碰 Id 字串。

那七個常數(`src/Inkling/CommandIds.cs:30,33,36,45,55,66,74`)是 CmdPal 存 alias、
全域快速鍵、釘選的鍵,[CLAUDE.md](../CLAUDE.md) 硬規則 1 把它們列為**對外承諾**。
改一個字,使用者設過的全部靜靜失效,而且**沒有任何一步會報錯** —— 編譯過、測試綠、
部署成功,只有使用者發現他的 `!` 不見了。

**建議修法**:一個 `[Fact]` 逐字比對那七個常數,十行。
`docs/release-runbook.md` 第 0 步的那個 `git diff` 目前是唯一的閘門。

---

## 沒有列進來的東西

- **效能**:2026-08-22 在 210 則的資料夾上實測,清單載入 <5 秒、打字過濾即時,
  沒有可感的延遲。有幾條「理論上會慢」的路(每個按鍵重讀剪貼簿、每次存/刪丟掉整份快取、
  每個按鍵重建 200 列的選單與 Details、watcher 與資料夾掃描搶同一把鎖)**沒有量測數據
  支持或否定**,所以不列為缺陷。真的要動之前先量。
- **`*.md.tmp` 殘骸**:`AtomicFile` 在 `File.Move` 失敗時會嘗試刪掉暫存檔,刪不掉就留著。
  掃描只看 `*.md`,所以殘骸不會被列出也不會被「刪除全部」清掉。已經是刻意的取捨,
  手動驗證清單也有對應項,不算缺陷。
