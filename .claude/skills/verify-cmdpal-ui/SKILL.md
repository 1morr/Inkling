---
name: verify-cmdpal-ui
description: >-
  在真機上驅動 Command Palette 的畫面驗證 Notelet:讀 UI Automation 樹、截圖、
  打字與按快速鍵,補上 docs/manual-test-checklist.md 裡那些「只能靠眼睛」的項目。
  改了 src/Notelet 底下的頁面、命令、快速鍵或 .resx 之後要驗證有沒有真的生效時看這份;
  要確認 README 對 CmdPal 行為的某條斷言時也看這份。
  Use when verifying Command Palette UI behavior on a real machine, driving CmdPal,
  computer use, UI automation, screenshots, or running the manual test checklist.
---

# 在真機上驗證 Notelet 的畫面

`dotnet test` 涵蓋 `Notelet.Core` 的全部行為,但 `src/Notelet` 那一層 —— 頁面長什麼樣、
按下去有沒有反應、快速鍵有沒有被搶走 —— 一行自動化測試都沒有。這份 skill 補的就是那一半。

工具是 `tools\cmdpal-ui.ps1`。

## 先讀這一條:computer-use 對 CmdPal 走不通

**`orca computer` 那套指令看不到 Command Palette。** 實測:

```
orca computer list-apps --json          # 清單裡沒有 CmdPal
orca computer list-windows --app pid:<CmdPal 的 pid>
  → { "ok": false, "error": { "code": "app_not_found" } }
```

原因是 CmdPal 是 WinUI 3 應用,`(Get-Process Microsoft.CmdPal.UI).MainWindowHandle`
**永遠是 0**,連面板開著的時候也是 —— orca 的視窗列舉照那個屬性過濾,於是整個進程被跳過。
同一個原因,任何照 `MainWindowHandle` 找視窗的腳本都會失敗。

`tools\cmdpal-ui.ps1` 因此自己走 `EnumWindows` + 比對 pid 與視窗標題,再用 Windows 內建的
UI Automation 讀畫面。**要驗 CmdPal 就用這個腳本,不要繞回 `orca computer`。**

反過來,**computer-use 在「CmdPal 以外的視窗」上仍然是對的工具**,而 Notelet 有幾條路
正好會跳出去:

| 要驗什麼 | 用什麼 |
|---|---|
| `Ctrl+L` 有沒有在檔案總管裡**選中**那個 `.md` | `orca computer` 讀 explorer |
| `Ctrl+O` 有沒有用預設程式開起來 | `orca computer list-apps` 看有沒有多一個視窗 |
| 設定頁「瀏覽…」跳出來的資料夾對話框 | `orca computer`(它是一般的 Win32 對話框) |
| CmdPal 面板本身的任何東西 | **`tools\cmdpal-ui.ps1`** |

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

**這一步不能跳過。** 預設值是 `%OneDrive%\Notelet` —— 也就是使用者真正在用的筆記。
任何會**新增、編輯或刪除**的驗證都不可以在那裡跑。

要跑寫入類的驗證,先換到測試資料夾,跑完換回去:

```powershell
$settings = "$env:LOCALAPPDATA\Packages\Notelet_bf0n0751x5hse\LocalState\settings.json"
$backup = Get-Content $settings -Raw -Encoding UTF8      # 先留一份原值

$json = $backup | ConvertFrom-Json
$json.'Notelet.NotesDirectory' = "$env:TEMP\notelet-verify"
New-Item -ItemType Directory -Force "$env:TEMP\notelet-verify" | Out-Null
$json | ConvertTo-Json | Set-Content $settings -Encoding UTF8
# 換資料夾要 Reload 才會重建 repository

# ... 跑驗證 ...

Set-Content $settings -Value $backup -Encoding UTF8      # 一定要換回去
```

只讀的驗證(瀏覽、搜索、預覽、切原始文字、看快速鍵有沒有被搶走)不必換。

### 3. 想看擴展內部發生了什麼就開診斷日誌

在 `%LOCALAPPDATA%\Packages\Notelet_bf0n0751x5hse\LocalState\` 建一個空檔 `diagnostic.on`,
Reload,之後 `-Steps "...|log"` 就讀得到。擴展沒有主控台,`Debug.WriteLine` 在 Release
被編掉,這是唯一能讓它自己說話的路。

## 怎麼用

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1200|tree:9"
```

**一定要用 `pwsh`(PowerShell 7)。** 腳本是無 BOM 的 UTF-8,Windows PowerShell 5.1
會照系統 ANSI 讀,中文全部變亂碼。

### 一整串動作一定要在同一次呼叫裡跑完

**CmdPal 一失焦就自我隱藏**(沒有開關,README〈刪除成功時一個 toast 都不發〉那條規矩的
成因也是它)。每啟動一個新的 PowerShell 進程都可能把它打斷 —— 所以是

```powershell
-Steps "show|type:Notelet|wait:900|tree:6"      # 對
```

而不是分成三次呼叫。腳本會在每個需要視窗的動作之後檢查 CmdPal 還是不是前景視窗,
不是就把整串重跑(預設最多 4 次),輸出裡會看到 `~~~ CmdPal 中途失焦,整串重跑 ~~~`。
偶爾出現一次是正常的,連著 4 次都失敗代表有別的東西在搶焦點。

### 動作

| 動作 | 做什麼 |
|---|---|
| `show` | 叫出 CmdPal。熱鍵**從 CmdPal 自己的 `settings.json` 讀**,不寫死 |
| `esc` | 送 Esc,退一層頁面;在主頁等於關掉面板 |
| `type:<文字>` | 打字。走 `KEYEVENTF_UNICODE`,中文與全形符號都打得出來 |
| `key:<組合>` | 按鍵,例如 `key:Enter`、`key:Ctrl+D`、`key:Ctrl+Shift+C` |
| `wait:<毫秒>` | 等待 |
| `tree[:<深度>]` | dump UI Automation 樹,預設深度 14 |
| `shot:<路徑>` | 截圖 |
| `toast` | 看 toast 視窗在不在 |
| `notes` | 列出目前設定的筆記資料夾內容 |
| `log[:<行數>]` | `diagnostic.log` 的尾巴 |
| `state` | 兩份 `settings.json` 的摘要 |

**`type:` 的尾隨空白有意義,不要順手刪掉。** alias 是「alias + 空白」才觸發的
(indirect alias 存的鍵就帶著那個空白),所以進清單頁是 `type:# ` 而不是 `type:#`。

腳本層級還有兩個參數:`-Retries`(失焦重跑幾次,預設 4)與 `-MaxText`
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
腳本已經每一輪都重取,所以看到這行代表三輪都沒讀到,再跑一次通常就好了。

**兩種都不要照著那半截樹下判斷。** 想確定畫面上到底有什麼,同一次序列裡加一個 `shot`
對照 —— 截圖走的是 `PrintWindow`,跟 UIA 是兩條獨立的路,不會一起壞。

## 判讀 UIA 樹

CmdPal 主頁打 `Notelet` 之後大致長這樣:

```
Window: 'Command Palette' [FOCUS]
    Edit: '搜尋應用程式、檔案和命令...' value='Notelet' [FOCUS]
      List: ''
        ListItem: ' ListItemViewModel'
          Text: '結果'                          ← 分節標題
        ListItem: 'Notelet' [SELECTED]          ← 選中的那一列
          Group: 'Notelet'
            Text: 'Notelet'                     ← Title
            Text: '瀏覽與搜索筆記'               ← Subtitle
            Custom: '# '                        ← 使用者設的 alias
    Button: 'Open Command Palette settings, shortcut Control plus comma'
    Button: '開啟'                              ← 底部命令列,主命令
    Button: 'More'                              ← Ctrl+K 選單
```

進了 Notelet 清單頁(`type:# `)之後:

```
Window: 'Command Palette' [FOCUS]
      Button: 'Back'
      Edit: '搜索標題與內文…' value='' [FOCUS]      ← placeholder 換了 = 真的進頁了
        List: ''
          ListItem: 'rime' [SELECTED]
          ListItem: 'DSHCLIPROXY'
      Pane: 'rime'                                 ← 詳細面板,名字是選中那則的標題
        Text: 'rime'
        Text: '```⏎當前我認為Rime…(共 3613 字)'      ← 渲染後的內文
        ScrollBar: 'Vertical' [off]
      Text: 'Notelet'                              ← 左下角的頁面名
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
  清單頁是「搜索標題與內文…」。
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

README〈刪除成功時一個 toast 都不發〉那條規矩靠 `toast` 動作驗。CmdPal 的 toast 是
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
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:Notelet|wait:900|tree:7|shot:$env:TEMP\toplevel.png"
```

四個頂層命令要都在。**圖示長什麼樣樹裡看不出來,一定要開 `shot` 出來的圖用眼睛確認** ——
UIA 只會給你一個 `Image: ''`,空白佔位圖跟真圖示在樹裡完全一樣。

**快速記下(第 2 節)**

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:! |wait:800|tree:8|type:測試想法;;這是內文|wait:800|tree:8|key:Enter|wait:1000|toast|notes|log:10"
```

要看的:進頁之後 placeholder 換了沒、打字之後第一列是不是「記下:測試想法」而副標是
「內文:這是內文」、Enter 之後 `notes` 有沒有多一個檔案、`toast` 是不是 `可見=False`。

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
  而 UIA 不給顏色資訊。確認框的按鈕連屬性都沒有(README〈確認框的按鈕沒有顏色〉)。
- **圖示的外觀。** 樹裡只有 `Image: ''`。
- **游標在輸入框裡的位置。** 做不到,見 CLAUDE.md 第 4 條。
- **確認框的預設按鈕。** 安裝版整個套件掃不到 `set_DefaultButton`,那個旗標沒有效果。

## 驗完之後

1. **筆記資料夾換回原值**(如果換過)。
2. 測試筆記清乾淨。
3. 行為有變的話,同一輪更新 `README.md` 與 `docs/manual-test-checklist.md` ——
   驗證清單裡曾經留過一條照上游 `main` 寫、但安裝版根本不成立的測試項,
   那種東西留著比沒有更糟。
