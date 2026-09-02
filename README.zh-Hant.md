<p align="center">
  <img src="assets/gallery/icon.png" width="96" alt="Inkling">
</p>
<h1 align="center">Inkling</h1>
<p align="center">不必離開 PowerToys Command Palette，就能記筆記。打字、按 Enter，想法就會存成你指定資料夾裡的一個 Markdown 檔案。</p>
<p align="center">
  <a href="https://github.com/1morr/Inkling/actions/workflows/ci.yml"><img src="https://github.com/1morr/Inkling/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://apps.microsoft.com/detail/9NDGWN4JTXHH"><img src="https://img.shields.io/badge/Microsoft%20Store-Inkling%20Notes-0078D4" alt="Microsoft Store"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/1morr/Inkling" alt="License: MIT"></a>
</p>
<p align="center"><a href="README.md">English</a> · <b>繁體中文</b></p>

Inkling 把記筆記這件事加進 Command Palette:幾秒鐘記下一個想法，之後就在同一個地方瀏覽、搜尋、編輯，不必再開其他程式。同步、手機存取，以及比較繁重的編輯，交給你已經在用的雲端硬碟和編輯器。

![頂層命令](docs/images/top-level-commands.png)

## 安裝

**[Microsoft Store — Inkling Notes](https://apps.microsoft.com/detail/9NDGWN4JTXHH)**，
或用 `winget install --source msstore --id 9NDGWN4JTXHH` 安裝。在 Command Palette 裡，
命令就叫「Inkling」。

| 需求 | |
|---|---|
| Windows | 10.0.19041 以上 |
| Command Palette | 0.11 以上(獨立的 `Microsoft.CommandPalette` 套件) |

不需要另外安裝 .NET —— 發佈的套件是 self-contained 的。GitHub Releases 上的檔案是 CI 為了送
Store 審核而建置的，**沒有簽章**，Windows 不會側載它們;想自己建置，請見
[docs/development.md](docs/development.md)。

## 上手

叫出 Command Palette，打 `Inkling`，選 **Inkling: Quick capture**。打完想法按 Enter，
整個流程就是這樣。

想連內文一起打，用 `;;` 分隔符:
`coffee machine idea;;Look up pour-over vs. espresso first`。分隔符前面是標題，
後面是內文;只打一個標題，一樣是完整的一則筆記。

**想少按幾個鍵？** 給 Quick capture 設一個 alias:CmdPal Settings → Extensions →
Inkling → `Inkling: Quick capture` → Alias。設成 `!` 之後，打 `!` 加一個空白就直接
開啟快速記下。標點比字母好，字母會跟真正的搜尋撞在一起;再配一個全域快速鍵，
連搜尋框都省了。

![快速記下](docs/images/quick-capture.gif)

## 功能

介面語言跟著 Windows 的顯示語言走 —— 英文、繁體中文或簡體中文。這裡的截圖是在英文系統上拍的。

| | |
|---|---|
| 快速記下 | 打字後按 Enter。標題相近的既有筆記會列在下面，避免同一件事記兩遍。剪貼簿裡的多行文字會主動提供給內文用([為什麼](docs/design-notes.md#paste-multiline)) |
| 記下後先看一眼 | 存檔後停留在筆記上，方便確認內容;再按一次 Enter 才收起面板。可在設定裡切換 |
| 瀏覽與搜尋 | 標題與內文都會被搜尋，多個關鍵字之間是 AND 的關係，標題命中排在前面。搜不到時按 Enter 直接進快速記下 |
| Markdown 預覽 | 按 Enter 渲染這則筆記。`Ctrl+U` 在渲染結果與原始文字之間切換，並記住你的選擇 —— 貼進來的 HTML 渲染後會消失，這時很有用 |
| 編輯 | `Ctrl+E` 開啟內建表單;`Ctrl+O` 則用你預設的編輯器開啟檔案。存檔後面板收起來，跟新增筆記一樣 |
| 複製與定位 | `Ctrl+Shift+C` 複製內文(不含 front matter);`Ctrl+L` 在檔案總管裡顯示這個 `.md` 檔 |
| 隨手草稿 | 一則永遠都在、不需要標題的筆記。沒有自動儲存([為什麼](docs/design-notes.md#scratchpad-no-autosave))—— `Tab` 再按 `Enter` 即可儲存並關閉 |
| 刪除 | `Ctrl+D` 確認後刪除，檔案會進資源回收筒。`Inkling: Delete notes` 可以一次處理多則，且動到非 Inkling 建立的檔案前一律會先確認 |

封存、標籤與釘選還沒做。

![筆記清單](docs/images/note-list.png)

## 快速鍵

選中一則筆記時，在清單頁與預覽頁上:

| 鍵 | 動作 |
|---|---|
| `Ctrl+E` | 編輯(表單) |
| `Ctrl+N` | 新增筆記(僅限清單頁) |
| `Ctrl+U` | 切換渲染結果／原始文字 |
| `Ctrl+Shift+C` | 複製內文 |
| `Ctrl+O` | 用預設程式開啟 |
| `Ctrl+L` | 在檔案總管裡選取該檔案 |
| `Ctrl+D` | 刪除(僅限清單頁) |
| `Ctrl+K` | 開啟選單 —— 每一項都會顯示自己的按鍵 |

只有複製帶 Shift，因為 `Ctrl+C` 是搜尋框自己的。
[哪些字母不能用、為什麼](docs/design-notes.md#list-shortcuts)。

`Enter` 與 `Ctrl+Enter` 按的是底部工具列的兩顆按鈕，所以它們做什麼取決於你在哪一頁 ——
工具列一律會顯示當下這一對按鈕。在編輯表單上，按 `Tab` 到「儲存」再按 `Enter` 完成儲存;
`Esc` 則是不存檔直接離開。
[為什麼這兩個頁面刻意互為鏡像](docs/design-notes.md#secondary-command)。

CmdPal 不讓使用者重新綁定擴充功能的快速鍵。頂層命令的 alias 與全域快速鍵，
則由你自己設定。

## 筆記檔長什麼樣

```markdown
---
id: 20260810-143052-a7f3
title: coffee machine idea
created: 2026-08-10T14:30:52+08:00
updated: 2026-08-11T09:15:00+08:00
---

Look up pour-over vs. espresso first.
```

資料格式是一項承諾:

- **`id` 才是筆記的身分，檔名只是給人看的。** 改標題不會重新命名檔案，雲端同步資料夾
  因此少掉一堆麻煩。
- **Inkling 看不懂的 front matter 一律保留原樣**，包括 Obsidian 加的欄位，以及它解析不了
  的日期。
- **沒有 front matter 的 `.md` 檔案也會出現在清單裡**，標題取自內文第一行有意義的文字。
  把 Inkling 指到既有的筆記資料夾就能直接用，那些檔案永遠不會被改寫。
- **檔案必須是 UTF-8**(有 BOM 也沒關係)。其他編碼的檔案會被跳過而不是讀成亂碼，
  清單會說明跳過了幾個。子資料夾會被掃描;新筆記一律寫在根目錄。
- **隨手草稿是根目錄下的 `scratchpad.md`**，純文字、沒有 front matter，不會出現在清單
  與搜尋裡。

## 設定

在主搜尋框裡選中 **Inkling** 那一列，按 `Ctrl+K` → Settings，或到 CmdPal Settings →
Extensions → Inkling。

| 設定項 | 預設值 |
|---|---|
| 筆記資料夾 | `%OneDrive%\Inkling`;沒有 OneDrive 時則是 `Documents\Inkling`。只接受完整路徑，第一次儲存時會自動建立。「瀏覽…」會開啟資料夾選擇器 |
| 快速記下分隔符 | `;;` —— 長度不限，半形與全形視為相同;清空後會還原成預設值 |
| 記下後先看一眼 | 開啟 |

## 同步

Inkling **不做同步**。它只是把 Markdown 寫進你指定的資料夾，離線可用性、衝突處理與
手機存取都交給你的雲端硬碟。Obsidian 之類的工具也可以指向同一個資料夾。

**OneDrive 使用者**:把資料夾設成「一律保留在此裝置上」。開著 Files On-Demand 時，
只有雲端佔位符的檔案在讀取時會觸發下載，搜尋會因此卡住。如果兩台機器同時編輯同一則
筆記，OneDrive 會產生一份 `name-ComputerName.md` 副本;兩列都會出現、標上
**衝突副本**，各自對應自己的檔案。

## 遇到問題

**改了設定卻沒反應** —— 把設定頁關掉再重新開啟。那個頁面綁定的是特定一個擴充功能實例，
重新載入之後按 Save 會靜靜地什麼都不做。

**介面語言不對** —— 它跟著 Windows 的**顯示**語言走，不是地區格式，也沒有覆寫選項。
改完之後要登出再登入才會生效。

**設定全部回到預設值** —— 如果 `settings.json` 變成不合法的內容，Inkling 會把它搬到
`settings.json.corrupt-<timestamp>`，然後從預設值重新開始，而不是一直靜靜地失敗。
舊的值仍然留在那個檔案裡。

其餘問題 —— 部署、註冊、trimming、診斷 log —— 都在
[docs/development.md](docs/development.md#troubleshooting)。

## 文檔

| | |
|---|---|
| [docs/development.md](docs/development.md) | 建置、部署、專案結構、疑難排解 |
| [docs/design-notes.md](docs/design-notes.md) | 每個決定背後的考量，給維護者與其他 CmdPal 擴充功能作者參考 |
| [docs/known-issues.md](docs/known-issues.md) | 已知的缺陷，各附一份重現步驟 |
| [CHANGELOG.md](CHANGELOG.md) | 版本紀錄 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 動手改這個 repo 之前先讀 |
| [PRIVACY.md](PRIVACY.md) | 這個 App 收集、傳送什麼(都沒有) |

## 參與貢獻

歡迎回報 bug 與提出功能建議 —— 請使用 issue 範本。送出 PR 之前請先讀
[CONTRIBUTING.md](CONTRIBUTING.md)，這個 repo 有幾條不算顯而易見的規則。

## 授權

[MIT](LICENSE)
