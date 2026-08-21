---
name: verify-cmdpal-ui
description: >-
  在真機上驅動 Command Palette 的畫面驗證 Inkling:讀 UI Automation 樹、截圖、
  打字與按快速鍵,補上 docs/manual-test-checklist.md 裡那些「只能靠眼睛」的項目。
  改了 src/Inkling 底下的頁面、命令、快速鍵或 .resx 之後要驗證有沒有真的生效時看這份;
  要確認 README 對 CmdPal 行為的某條斷言時也看這份。跑出面板以外的視窗
  (檔案總管、外部編輯器、資料夾對話框)怎麼驗也寫在這份裡 —— 那些用 orca computer。
  Use when verifying Command Palette UI behavior on a real machine, driving CmdPal,
  computer use, UI automation, screenshots, or running the manual test checklist,
  or when a command leaves the palette and lands in Explorer, an external editor, or a dialog.
---

# 在真機上驗證 Inkling 的畫面

`dotnet test` 涵蓋 `Inkling.Core` 的全部行為,但 `src/Inkling` 那一層 —— 頁面長什麼樣、
按下去有沒有反應、快速鍵有沒有被搶走 —— 一行自動化測試都沒有。這份 skill 補的就是那一半。

工具是 `tools\cmdpal-ui.ps1`。

## 先讀這一條:面板用腳本,面板以外用 computer-use

這份 skill 管的是 **CmdPal 的面板**,不是「只准用 `cmdpal-ui.ps1`」。Inkling 有好幾條路
會跳出面板 —— 在編輯器開啟、開啟檔案位置、設定頁的「瀏覽…」對話框 —— 那些用
`orca computer` 驗得到,**別因為這份 skill 沒寫就停在那裡說「驗不到」**。

### CmdPal 的面板:`orca computer` 看不到

**`orca computer` 那套指令看不到 Command Palette 的主面板。**(orca 是作者自己的
桌面工具;沒有它的話略過這幾行即可 —— 核心驗證工具是 `tools\cmdpal-ui.ps1`,
不依賴 orca。)實測:

```
orca computer list-apps --json          # 清單裡沒有 CmdPal
orca computer list-windows --app pid:<CmdPal 的 pid>
  → { "ok": false, "error": { "code": "app_not_found" } }
```

原因是 CmdPal 是 WinUI 3 應用,**主面板永遠不會成為進程的 MainWindow**:
`(Get-Process Microsoft.CmdPal.UI).MainWindowHandle` 平常是 0,連面板開著的時候也是 ——
orca 的視窗列舉照那個屬性過濾,於是整個進程被跳過。同一個原因,任何照
`MainWindowHandle` 找視窗的腳本都會失敗。

**但這個斷言有一個例外,別被它騙了:「Command Palette Settings」視窗開著時,
`MainWindowHandle` 會指向它**(實測 `MainWindowTitle` = 'Command Palette Settings'),
orca 的 `list-apps` 也列得出 CmdPal、`list-windows` 回得到那個視窗。那個 handle
是設定視窗的,**主面板依然列舉不到** —— 看到 orca 列得出 CmdPal 不代表它能驗面板。

`tools\cmdpal-ui.ps1` 因此自己走 `EnumWindows` + 比對 pid 與視窗標題,再用 Windows 內建的
UI Automation 讀畫面。**要驗 CmdPal 就用這個腳本,不要繞回 `orca computer`。**

### 面板以外:`orca computer` 是對的工具

`orca computer capabilities` 在這台機器上回報的能力(實測):

```
Observation: screenshot=true elementFrames=true annotatedScreenshot=false
Actions: click, typeText, pressKey, hotkey, pasteText, scroll, drag, setValue, performAction
```

| 要驗什麼 | 用什麼 |
|---|---|
| `Ctrl+L` 有沒有在檔案總管裡**選取**那個 `.md` | `orca computer` 讀 explorer(見下方三步) |
| `Ctrl+O` 有沒有用預設程式開起來 | `orca computer list-apps` 看有沒有多一個視窗 |
| 外部編輯器裡的內容對不對 | `orca computer get-app-state --app <編輯器>` |
| 設定頁「瀏覽…」跳出來的資料夾對話框 | `orca computer`(它是一般的 Win32 對話框) |
| CmdPal 面板本身的任何東西 | **`tools\cmdpal-ui.ps1`** |

固定是三步:`list-windows` 拿 id → `get-app-state` 讀樹 → `click` / `type-text` 動它。

```powershell
orca computer list-windows --app explorer --json     # 拿 pid 與 window-id
orca computer get-app-state --app pid:6580 --window-id 7343176 --json > state.json
orca computer click --app pid:6580 --window-id 7343176 --element-index 36 --no-screenshot --json
```

踩過的坑,每一條都是實測:

- **`--app explorer` 會挑到桌面殼。** 直接 `get-app-state --app explorer` 拿到的是一個
  1×1、`elementCount` = 1 的 `Progman` 視窗,樹裡什麼都沒有 —— 成因跟 CmdPal 那條一樣。
  **資料夾視窗是另一個 `explorer.exe` 進程**(實測桌面殼 pid 12352、資料夾視窗 pid 6580),
  所以一定要先 `list-windows` 拿到 pid 與 window-id 再指名,不要只給 `--app`。
  同理,`list-windows` 只回一個 1×1 視窗時,代表**那個資料夾視窗根本還沒開**,不是工具壞了。
- **選取狀態不在清單項目上,在狀態列。** 清單項目那一行只有
  `清單項目 design-notes.md, Secondary Actions: SetValue, Select`,選中與否看不出來;
  真正的證據是狀態列那一行變成 `文字 已選取 1 個項目 82.1 KB`。驗 `Ctrl+L` 就抓這一行,
  再拿 KB 數跟檔案大小對一下。
- **`--element-index` 會在快照之間位移。** 選取一次之後狀態列多了一個群組,同一個
  `design-notes.md` 就從 34 變成 36。**每動一次就重新 `get-app-state`**,不要沿用舊索引。
- **`--json` 會把整棵樹印在 stdout,很長。** 導到檔案再挑,不要直接讓它進上下文。
  檔案總管的**預覽窗格**尤其毒:它把整份 `.md` 的渲染結果塞成一個 base64 `data:` URI
  當節點名字,一個節點就幾十 KB。
- **`get-app-state` 預設會截圖**,路徑在回傳的 `result.screenshot.path`
  (`%TEMP%\orca-computer-use\*.png`,有 `expiresAt`),那個檔案直接用 Read 打得開。
  不需要圖就加 `--no-screenshot`,省一次寫檔。

**全螢幕截圖 orca 沒有指令**(`get-app-state` 只截目標視窗)。要看整個桌面自己來:

```powershell
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$b = [System.Windows.Forms.SystemInformation]::VirtualScreen
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
[System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
$bmp.Save("$env:TEMP\shot.png", [System.Drawing.Imaging.ImageFormat]::Png)
```

存出來的 PNG 用 Read 開得起來,**而且這條路看得到 CmdPal 的面板** —— 它是螢幕像素,
不經過視窗列舉,上面那個「列舉不到主面板」的限制擋不到它。只是它給不了元素樹,
判斷邏輯還是回 `cmdpal-ui.ps1`。

## 開始之前

### 1. 部署並 Reload

```powershell
.\tools\deploy.ps1 -Configuration Release -Reload
```

沒 Reload 的話 CmdPal 繼續用舊的擴展實例,你會以為改動沒生效。
驗設定相關的項目前,**還要把設定頁關掉重開** —— 舊實例上的設定頁按 Save 會靜靜地什麼都不做。

### 2. 先確認筆記資料夾是不是真的資料 ⚠

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "notes"
```

**這一步不能跳過。** 預設值是 `%OneDrive%\Inkling` —— 也就是使用者真正在用的筆記。
任何會**新增、編輯或刪除**的驗證都不可以在那裡跑。

要跑寫入類的驗證,先換到測試資料夾,跑完換回去:

```powershell
$pfn = (Get-AppxPackage Inkling).PackageFamilyName
$settings = "$env:LOCALAPPDATA\Packages\$pfn\LocalState\settings.json"
$backup = Get-Content $settings -Raw -Encoding UTF8      # 先留一份原值

$json = $backup | ConvertFrom-Json
$json.'Inkling.NotesDirectory' = "$env:TEMP\inkling-verify"
New-Item -ItemType Directory -Force "$env:TEMP\inkling-verify" | Out-Null
$json | ConvertTo-Json | Set-Content $settings -Encoding UTF8
# 換資料夾要 Reload 才會重建 repository

# ... 跑驗證 ...

Set-Content $settings -Value $backup -Encoding UTF8      # 一定要換回去
```

只讀的驗證(瀏覽、搜索、預覽、切原始文字、看快速鍵有沒有被搶走)不必換。

### 3. 想看擴展內部發生了什麼就開診斷日誌

在 `%LOCALAPPDATA%\Packages\<PFN>\LocalState\` 建一個空檔 `diagnostic.on`
(`<PFN>` 用 `(Get-AppxPackage Inkling).PackageFamilyName` 查),
Reload,之後 `-Steps "...|log"` 就讀得到。擴展沒有主控台,`Debug.WriteLine` 在 Release
被編掉,這是唯一能讓它自己說話的路。

## 怎麼用

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1200|tree:9"
```

**一定要用 `pwsh`(PowerShell 7)。** 腳本是無 BOM 的 UTF-8,Windows PowerShell 5.1
會照系統 ANSI 讀,中文全部變亂碼。

### 一整串動作一定要在同一次呼叫裡跑完

**CmdPal 一失焦就自我隱藏**(沒有開關,[設計考證〈刪除成功時一個 toast 都不發〉](../../../docs/design-notes.md#delete-no-toast)那條規矩的
成因也是它)。每啟動一個新的 PowerShell 進程都可能把它打斷 —— 所以是

```powershell
-Steps "show|type:Inkling|wait:900|tree:6"      # 對
```

而不是分成三次呼叫。

### 腳本不會把按鍵送到別的視窗

會送按鍵的動作(`type` / `key` / `esc`)在**送出之前**先確認 CmdPal 的面板真的在前景
而且已經可以用了。`SendInput` 指定不了目標視窗 —— 它送到的永遠是當下的前景視窗,
所以「先送再檢查」等於把字打進使用者正在用的編輯器,而檢查只是事後報告。
(腳本以前就是先送再檢查,而且失焦會整串重跑,同一串字會被打進錯的地方好幾遍。)

擋下來的時候輸出會直接告訴你當時前景是誰:

```
### type Inkling
  !! CmdPal 不在前景,'type' **沒有送出**(送了會打進別的視窗)
     目前前景:Orca (pid=7572) 'Orca'
```

擋下來之後整串重跑(含第一次最多試 4 次),輸出裡會看到
`~~~ CmdPal 中途失焦,整串重跑 ~~~`。偶爾出現一次是正常的;連著 4 次都失敗代表
有別的東西一直在搶焦點,這時候腳本**以非零結束** —— 不會靜靜地假裝跑完了。

**重跑是從第一步開始的。** 序列裡有存檔、刪除這類有副作用的步驟時,重跑等於再做一次
(真的重跑了輸出會警告)。要驗有副作用的東西,序列盡量短、把 `key:Enter` 放在最後。

### 動作

| 動作 | 做什麼 |
|---|---|
| `show` | 叫出 CmdPal。熱鍵**從 CmdPal 自己的 `settings.json` 讀**,不寫死 |
| `esc` | 送 Esc,退一層頁面;在主頁等於關掉面板 |
| `type:<文字>` | 打字。走 `KEYEVENTF_UNICODE`,中文與全形符號都打得出來 |
| `key:<組合>` | 按鍵,例如 `key:Enter`、`key:Ctrl+D`、`key:Ctrl+Shift+C`。**一次只吃一組組合** —— `key:Down Down Down` 會被當成不認得的按鍵,整串中止(非零結束);連按就拆成多個 `key:Down` |
| `wait:<毫秒>` | 等待 |
| `tree[:<深度>]` | dump UI Automation 樹,預設深度 14 |
| `shot:<路徑>` | 截圖 |
| `toast` | 看 toast 視窗在不在 |
| `notes` | 列出目前設定的筆記資料夾內容 |
| `log[:<行數>]` | `diagnostic.log` 的尾巴 |
| `state` | 兩份 `settings.json` 的摘要 |

**`type:` 的尾隨空白有意義,不要順手刪掉。** alias 是「alias + 空白」才觸發的
(indirect alias 存的鍵就帶著那個空白),所以進清單頁是 `type:# ` 而不是 `type:#`。

**不認得的動作會中止整串並以非零結束**(跟 `key:` 同一個理由:印個警告繼續跑的話,
後面的步驟會落在沒預期的地方)。打錯動作名不會看到「跑完了」的假象。

腳本層級還有兩個參數:`-Retries`(整串最多嘗試幾次,含第一次,預設 4)與 `-MaxText`
(樹裡每個字串印到幾個字,預設 120)。

### 樹裡只有一行 `Window: 'Command Palette'` 的時候

那**不是**「畫面上什麼都沒有」。有兩個成因,腳本各給一行不同的訊息:

```
### tree 5
  !! CmdPal 不在前景,這棵樹讀不到內容          ← 面板收起來了,焦點被搶走
  !! 樹只讀到根節點 —— 畫面可能還在轉場…        ← 畫面好好的,是 UIA 元素失效
```

第一種是 CmdPal 隱藏了 —— 面板一收起來 UIA 就只回得到根節點,重試機制會把整串重跑。

第二種比較陰:**畫面完全正常**(同一次跑裡截出來的圖是滿的),但那個
`AutomationElement` 就是問不出子節點。拿舊的 root 重試沒有用,要重新 `FromHandle` ——
腳本已經每一輪都重取,所以看到這行代表三輪都沒讀到。這時**重跑未必有用**;
真正有效的是把動作之間的 `wait` 拉長到 1800ms 以上,讓轉場走完再讀。

在主面板上這兩種現在都少很多:`tree` 跟送按鍵一樣要先過守門,而守門的判準
**就是 UIA 讀不讀得到子節點**(純看視窗可見與否分辨不出「開著」跟「正在關」——
面板收起來的那一小段時間裡它仍然 IsWindowVisible,前景進程也還是 CmdPal)。
popup 不受這個保護,見下面。

**兩種都不要照著那半截樹下判斷。** 想確定畫面上到底有什麼,同一次序列裡加一個 `shot`
對照 —— 截圖走的是 `PrintWindow`,跟 UIA 是兩條獨立的路,在主面板上不會一起壞。

**例外是 popup(`Ctrl+K` 選單、確認框):兩條路會一起空。** popup 是獨立的頂層視窗,
`PrintWindow` 拍主視窗拍不到它,而轉場中的 UIA 又常常只讀到根節點 ——
實測 `key:Ctrl+K` 之後立刻 `tree|shot`,樹只有根節點、截圖裡也沒有選單,但選單是開著的。
所以「截圖裡沒有選單」不能推論成「選單沒開」:**驗 popup 只能靠 UIA,而且要把
`wait` 拉到 1800ms 以上再讀。**(另外 `shot` 在轉場途中偶爾只截到 800x480 的小圖,
那是視窗 rect 還沒穩定下來,同樣是 wait 不夠長。)

## 判讀 UIA 樹

CmdPal 主頁打 `Inkling` 之後大致長這樣:

```
Window: 'Command Palette' [FOCUS]
    Edit: '搜尋應用程式、檔案和命令...' value='Inkling' [FOCUS]
      List: ''
        ListItem: ' ListItemViewModel'
          Text: '結果'                          ← 分節標題
        ListItem: 'Inkling' [SELECTED]          ← 選取的那一列
          Group: 'Inkling'
            Text: 'Inkling'                     ← Title
            Text: '瀏覽與搜尋筆記'               ← Subtitle
            Custom: '# '                        ← 使用者設的 alias
    Button: 'Open Command Palette settings, shortcut Control plus comma'
    Button: '開啟'                              ← 底部命令列,主命令
    Button: 'More'                              ← Ctrl+K 選單
```

進了 Inkling 清單頁(`type:# `)之後:

```
Window: 'Command Palette' [FOCUS]
      Button: 'Back'
      Edit: '搜尋標題與內文…' value='' [FOCUS]      ← placeholder 換了 = 真的進頁了
        List: ''
          ListItem: 'rime' [SELECTED]
          ListItem: 'DSHCLIPROXY'
      Pane: 'rime'                                 ← 詳細面板,名字是選取那則的標題
        Text: 'rime'
        Text: '```⏎當前我認為Rime…(共 3613 字)'      ← 渲染後的內文
        ScrollBar: 'Vertical' [off]
      Text: 'Inkling'                              ← 左下角的頁面名
      Button: '預覽'                                ← 底部命令列,主命令
      Button: '編輯'
      Button: 'More'
```

字串太長會截斷成 `…(共 N 字)`,換行印成 `⏎` —— 一則長筆記的內文原樣印出來足以淹掉整份
輸出。要看完整內文直接讀那個 `.md` 檔,不要調 `-MaxText` 去硬撈。

幾個要注意的:

- **搜尋框的 `Name` 是 placeholder,`value` 才是使用者打的字。** 驗
  「placeholder 有沒有跟著分隔符設定更新」看的是 `Name`;
  「進了哪一頁」也看它 —— 主頁是「搜尋應用程式、檔案和命令...」,
  清單頁是「搜尋標題與內文…」。
- **`[SELECTED]` 是唯一能看出焦點落在哪一列的東西。** CLAUDE.md〈已知落差〉提到
  安裝版沒有 sticky selection,「刪掉當前那一列之後焦點落在哪」沒有保證 ——
  要驗那個,就在刪除前後各 dump 一次比對。
- **詳細面板是 `Pane: '<筆記標題>'`**,底下的 `Text` 就是渲染後的內文。
  `Ctrl+U` 切換原始文字前後各 dump 一次,比對那塊 `Text` 變了沒。
  注意 `ListItem.Details` **只能整個換掉**,就地改屬性跨不過 out-of-process 邊界
  (CLAUDE.md 硬規則第 2 條)—— 症狀正是「值改了、樹卻不動」。
- **Adaptive Card 表單**(新增/編輯/設定頁)在樹裡是一串 `Edit` 與 `Button`。
  欄位**順序**看得到,**游標在欄位裡的位置看不到**(CmdPal 只做
  `Focus(FocusState.Programmatic)`,那個做不到,見 CLAUDE.md 第 4 條)。

## toast:唯一一條「沒發生才算對」的驗證

[設計考證〈刪除成功時一個 toast 都不發〉](../../../docs/design-notes.md#delete-no-toast)那條規矩靠 `toast` 動作驗。CmdPal 的 toast 是
**另一個頂層視窗**(`Command Palette Toast`),它一出現就搶焦點,主視窗一失焦就自己隱藏 ——
「做完之後整個面板消失」的成因就是它,不是 `GoHome()`。

```
toast 視窗:HWND=132196 可見=False / 主視窗還在=True     ← 對
!! 有 toast 跳出來 —— 主面板會跟著消失                    ← 錯,那條路徑上有人回了 ShowToast
```

toast 視窗**本來就一直存在**(CmdPal 啟動時就建好了),所以要看的是 `可見=`,不是存不存在。
`ToastStatusMessage` 不會讓它可見 —— 那個走的是 host 的 `ShowStatus`,畫成底部命令列的
`InfoBadge`,兩者名字很像但不是同一件事。

## 典型的驗證流程

對照 `docs/manual-test-checklist.md` 的章節。以下都假設**已經換到測試資料夾**。

**頂層命令與圖示(第 1 節)**

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:Inkling|wait:900|tree:7|shot:$env:TEMP\toplevel.png"
```

五個頂層命令要都在(清單 / 快速記下 / 新增筆記 / 隨手草稿 / 刪除筆記)。**圖示長什麼樣樹裡看不出來,一定要開 `shot` 出來的圖用眼睛確認** ——
UIA 只會給你一個 `Image: ''`,空白佔位圖跟真圖示在樹裡完全一樣。

**快速記下(第 2 節)**

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:! |wait:800|tree:8|type:測試想法;;這是內文|wait:800|tree:8|key:Enter|wait:1000|toast|notes|log:10"
```

要看的:進頁之後 placeholder 換了沒、打字之後第一列是不是「記下：測試想法」而副標是
「內文：這是內文」、Enter 之後 `notes` 有沒有多一個檔案、`toast` 是不是 `可見=False`。

**快速鍵沒有被搜尋框搶走(第 10 條硬規則)**

清單頁的焦點永遠在搜尋框上,而 CmdPal 在 tunneling 階段就把鍵送去比對,比 `TextBox` 早收到。
所以驗一個新綁的鍵要驗兩件事:

```powershell
# 1. 綁的鍵有作用
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1200|key:Ctrl+U|wait:600|tree:9"
# 2. 搜尋框的編輯鍵沒有被拿走 —— 打字、Ctrl+A、Ctrl+C 之後 value 還對
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1200|type:abc|key:Ctrl+A|key:Backspace|wait:400|tree:4"
```

第二條的 `Edit` 的 `value` 應該變回空字串。變不回來就代表那個鍵被擴展綁走了。

**設定改了不必 Reload(第 2a 節)**

`ICaptureSeparatorStore` / `ICapturePreviewStore` 那條路的重點是「頁面自己響應」。
驗法是**把快速記下頁開著**,另外改設定,再回到那個還開著的頁面看 placeholder 換了沒 ——
中間**不要 Reload**。腳本開一次 `show` 留著,改設定用另一個 PowerShell,然後再 `tree`。

## 驗不到的東西

別在這些上面浪費時間,它們只能靠眼睛看 `shot` 出來的圖,或者根本驗不到:

- **顏色。** `CommandContextItem.IsCritical` 的紅色是擴展唯一碰得到的顏色,
  而 UIA 不給顏色資訊。確認框的按鈕連屬性都沒有([設計考證〈確認框的按鈕沒有顏色,也沒有「危險」樣式〉](../../../docs/design-notes.md#confirm-dialog-colors))。
- **圖示的外觀。** 樹裡只有 `Image: ''`。
- **游標在輸入框裡的位置。** 做不到,見 CLAUDE.md 第 4 條。
- **確認框的預設按鈕。** 安裝版整個套件掃不到 `set_DefaultButton`,那個旗標沒有效果。

## 驗完之後

1. **筆記資料夾換回原值**(如果換過)。
2. 測試筆記清乾淨。
3. 行為有變的話,同一輪更新 `README.md` 與 `docs/manual-test-checklist.md` ——
   驗證清單裡曾經留過一條照上游 `main` 寫、但安裝版根本不成立的測試項,
   那種東西留著比沒有更糟。
