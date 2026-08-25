<p align="center">
  <img src="assets/gallery/icon.png" width="96" alt="Inkling">
</p>
<h1 align="center">Inkling</h1>
<p align="center">不離開 PowerToys Command Palette 就能記筆記。打字、按 Enter，想法就存成你指定資料夾裡的一個 Markdown 檔。</p>
<p align="center">
  <a href="https://github.com/1morr/Inkling/actions/workflows/ci.yml"><img src="https://github.com/1morr/Inkling/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/1morr/Inkling" alt="License: MIT"></a>
</p>
<p align="center"><a href="README.md">English</a> · <b>繁體中文</b></p>

<!-- 這一份與 README.md 是同一份文檔的兩個語言版本，改一份就要改另一份
     (結構、章節、表格的列都要對得上)。

     對外文案只有一個來源:docs/copy.md。上面那句 pitch、三個語言的 Store listing、
     gallery 的卡片、manifest 的 Description 全部住在那裡 —— 先改那一份，再把措辭搬過來。
     文案要守的規則(alias 是可選的、講做什麼不講為什麼、實作語彙不要進 Store)
     寫在同一份的開頭。

     頂部的圖示直接引用 assets/gallery/icon.png —— 它是 render-icons.ps1 的產出，
     改 SVG 重跑腳本這裡就跟著更新，不要另外放一份。 -->

Inkling 把記筆記這件事放進 Command Palette:幾秒鐘記下一個想法，然後在同一個面板裡瀏覽、搜尋、
編輯，不必再開別的程式。同步、手機存取與比較重的編輯，交給你已經在用的雲端硬碟和編輯器。

![頂層命令](docs/images/top-level-commands.png)

<!-- 截圖用 tools\cmdpal-ui.ps1 的 shot 動作在真機上拍(PrintWindow，見該腳本與
     .claude/skills/verify-cmdpal-ui)。改了圖示、命令標題或版面就要重拍;
     拍之前先把筆記資料夾指到一個 demo 資料夾，別把真的筆記放進公開 repo。
     GIF 是同一個腳本連拍幾張再用 ffmpeg 合成的，流程寫在 docs/development.md。 -->

介面語言跟著 Windows 的顯示語言走(英文、繁體中文、簡體中文);**這裡的截圖是在英文系統上拍的** ——
兩份 README 共用同一組圖，而截圖裡有一半的字串(「Results」、底部那排按鈕)是 CmdPal 自己的，
跟著系統語言走、我們改不了。

## 需求

| | |
|---|---|
| Windows | 10.0.19041 以上 |
| Command Palette | 0.11 以上(獨立 MSIX 套件 `Microsoft.CommandPalette`) |

**不需要裝 .NET** —— 發佈的套件是 self-contained。

## 安裝

**Microsoft Store** — [Inkling Notes](https://apps.microsoft.com/detail/9NDGWN4JTXHH)。
Store 上的名字是「Inkling Notes」，Command Palette 裡的命令仍然叫「Inkling」。

用 WinGet 裝的是同一個套件:`winget install --source msstore --id 9NDGWN4JTXHH`。

掛在 GitHub Releases 上的檔案是 CI 為了送 Store 審核而建的，**沒有簽章**，Windows 不讓側載。
想自己建:[開發與部署](docs/development.md)，`tools\deploy.ps1` 一條指令部署到本機。

## 上手

叫出 Command Palette，打 `Inkling`，選 **Inkling：快速記下**，把想法打完按 Enter。
整個流程就是這樣。

想連內文一起記，用分隔符:`買咖啡機的想法;;先查一下手沖跟義式的差別`。
`;;` 前面是標題，後面是內文。分隔符是選用的，只打一個標題也是一則完整的筆記。

**想少按幾個鍵?** 給快速記下設一個 alias:CmdPal 設定 → Extensions → Inkling →
`Inkling：快速記下` → Alias 填 `!`。設好之後在面板裡打 `!` 加一個空白就直接進去。
這裡挑標點比挑字母好，字母會跟真實搜尋撞在一起;再給它一個全域快速鍵的話，連搜尋框都省了。
清單頁與新增筆記同樣可以設 alias (最上面那張圖右側的 `#` 與 `@` 就是)。

![快速記下](docs/images/quick-capture.gif)

## 功能

| | |
|---|---|
| 快速記下 | 打完字按 Enter 就存好。想連內文一起記就 `<標題>;;<內文>` (分隔符可換)。底下會列出標題相近的既有筆記，免得同一件事記兩遍。剪貼簿裡是多行內容時，會多給一列直接拿它當內文([為什麼](docs/design-notes.md#paste-multiline)) |
| 記下後先看一眼 | 存好會停在筆記上，讓你確認沒記錯;再按一次 Enter 收起面板並說一聲存了什麼。預設開著，設定裡可以關掉 |
| 新增(完整) | `Inkling：新增筆記`，或在清單頁按 `Ctrl+N`，開一張可以寫多行內文的表單。存檔會說一聲並收起面板;編輯既有筆記則相反，存完留在原地([為什麼](docs/design-notes.md#edit-form)) |
| 瀏覽與搜尋 | 標題與內文都能搜，多個關鍵字是 AND，標題命中排前面，副標是內文的第一行。搜不到時按 Enter 直接進快速記下([空白提示有兩種](docs/design-notes.md#empty-content)) |
| Markdown 預覽 | 選中筆記按 Enter 看渲染結果。隨手打的單一換行會照樣顯示成換行，磁碟上的 `.md` 一個字都不變([為什麼](docs/design-notes.md#preview-line-breaks)) |
| 原始文字 | `Ctrl+U` 在渲染結果與原文之間切換，而且記得住。貼進來的 HTML 或 SVG 渲染完會整段消失，這個模式看得到([細節](docs/design-notes.md#source-mode)) |
| 編輯 | `Ctrl+E` 開內建表單，`Tab` 到「儲存」再按 `Enter`;或用 `Ctrl+O` 跳到預設編輯器改 |
| 複製內文 / 開啟檔案位置 | `Ctrl+Shift+C` 複製內文，不含 front matter，面板底部會講出複製到的是哪一則([為什麼要講](docs/design-notes.md#copy-feedback))。`Ctrl+L` 在檔案總管裡選中那個 `.md` |
| 跳出去之後 | `Ctrl+O` 與 `Ctrl+L` 跳到外部程式之後，面板只是讓開，熱鍵叫回來還停在原本那一頁([為什麼](docs/design-notes.md#open-external-return))。編輯表單與隨手草稿這兩頁會直接收起面板，避免畫面上那份舊副本蓋掉你剛在外面改好的檔案。檔案被移走，或 `.md` 沒有預設程式時，底部會說明原因 |
| 隨手草稿 | `Inkling：隨手草稿` 是一塊永久的便條紙，不必取標題，打開就是上次留下的內容。沒有自動儲存([為什麼](docs/design-notes.md#scratchpad-no-autosave)):`Tab` 到「儲存」按 `Enter`，存完會說一聲並收起面板。`Ctrl+O` 跳到系統預設編輯器 |
| 刪除 | 清單頁按 `Ctrl+D`，確認後檔案送資源回收筒，底部會講出刪掉的是哪一則。選取會落在下一則上，連著刪好幾則不會被丟回最上面([為什麼這件事不是免費的](docs/design-notes.md#selection-survives-rebuild))。網路磁碟或沒有資源回收筒的裝置上是永久刪除，刪除筆記那一頁的詳細窗格會講明白 |
| 連續刪 / 清空 | `Inkling：刪除筆記` 這一頁，`Enter` 刪除(先問一次)、`Ctrl+Enter` 直接刪。「刪除全部」會先列出會刪掉哪些檔案;不是 Inkling 建立的檔案兩條路都會問([這兩個鍵為什麼這樣配](docs/design-notes.md#delete-keys)) |
| 介面語言 | 英文、繁體中文、簡體中文，跟著 Windows 的顯示語言走，[沒有設定項](docs/design-notes.md#ui-language) |

封存、tag 分類、置頂還沒做。`tags` 欄位讀得懂，但沒有值就不會寫進檔案。

![清單頁](docs/images/note-list.png)

## 快速鍵

清單頁與預覽頁上，選中一則筆記之後:

| 鍵 | 做什麼 | 清單頁 | 預覽頁 |
|---|---|:-:|:-:|
| `Ctrl+E` | 編輯(表單) | ✅ | ✅ |
| `Ctrl+N` | 新增筆記(開表單) | ✅ | — |
| `Ctrl+U` | 切換渲染結果 / 原始文字(全域，記得住) | ✅ | ✅ |
| `Ctrl+Shift+C` | 複製內文 | ✅ | ✅ |
| `Ctrl+O` | 用系統預設的程式開啟 `.md` | ✅ | ✅ |
| `Ctrl+L` | 在檔案總管裡選中這個檔案 | ✅ | ✅ |
| `Ctrl+D` | 刪除(先跳確認框) | ✅ | — |
| `Ctrl+K` | 打開選單，上面每一項都寫著自己的鍵 | ✅ | ✅ |

**`Enter` 與 `Ctrl+Enter` 按的是底部工具列固定的兩顆按鈕**，所以它們做什麼要看你在哪一頁:

| 頁面 | `Enter` | `Ctrl+Enter` |
|---|---|---|
| 清單頁 | 預覽 | 編輯 |
| 預覽頁 | 編輯 | 完成(收起面板) |
| 記下並預覽(快速記下的 Enter 落點) | 完成(收起面板) | 編輯 |
| `Inkling：刪除筆記` | 刪除(先問一次) | 直接刪 |
| 編輯表單 | 繼續編輯(什麼都不做) | 在預設編輯器開啟 |
| 隨手草稿 | 捨棄變更(焦點在文字框裡時 `Enter` 是換行，要先 `Tab` 出來) | 在預設編輯器開啟 |

編輯表單的存檔是 `Tab` 到「儲存」再按 `Enter`，存完這一頁會留著，按 `Esc` 回上一頁;
焦點在標題欄時按 `Enter` 刻意什麼都不做。隨手草稿的存法一樣，存完會收起面板並說一聲剛才做了什麼。
預覽頁與記下並預覽頁的兩個鍵為什麼刻意相反，見[設計考證](docs/design-notes.md#secondary-command)。

**只有複製帶 Shift**，因為 `Ctrl+C` 是搜尋框自己的複製鍵。哪些字母不能碰、為什麼刪除是 `Ctrl+D`，
見[設計考證](docs/design-notes.md#list-shortcuts)。
**CmdPal 目前不讓使用者改擴展的快速鍵**，能改的只有頂層命令的 alias 與全域快速鍵。

## 筆記檔長什麼樣

```markdown
---
id: 20260810-143052-a7f3
title: 買咖啡機的想法
created: 2026-08-10T14:30:52+08:00
updated: 2026-08-11T09:15:00+08:00
---

先查一下手沖跟義式的差別。
```

資料格式是承諾:

- **`id` 才是身分，檔名只是給人看的。** 改標題不會重新命名檔案，雲端同步資料夾因此少掉一堆麻煩。
  清單上某一列動到哪個檔案看的是它的路徑，所以雲端的衝突副本兩份各自獨立編輯得動。
- **Inkling 讀不懂的 front matter 原樣保留**，包括看不懂的日期，以及 Obsidian 之類的工具加的
  `aliases`、`cssclass`。`created` 與 `updated` 用 ISO 8601 寫，空的 `tags` 則不寫進檔案。
- **沒有 front matter 的 `.md` 照樣出現在清單裡**，標題取內文的第一行有效文字、時間取檔案時間戳。
  你可以直接把既有的筆記資料夾指給 Inkling。這些檔案 Inkling 不會去改寫，刪除相關的動作也一律
  先問過([為什麼](docs/design-notes.md#delete-page))。
- **檔案必須是 UTF-8** (有 BOM 沒問題)。其他編碼會整個跳過而不是讀成亂碼，清單最後會講有幾個
  讀不出來。會掃子資料夾，新筆記一律寫在根目錄。
- **隨手草稿是同一個資料夾根目錄下的 `scratchpad.md`**，純文字、沒有 front matter，
  所以別的編輯器打開就是你寫的字。它不會出現在清單與搜尋裡;子資料夾裡剛好也叫這個名字的
  檔案照樣是一則筆記。

## 設定

在主搜尋框選中 **Inkling** 那一列按 `Ctrl+K` → 設定(`Ctrl+Enter` 直接到)，
或 CmdPal 設定 → Extensions → Inkling。

| 設定 | 預設 | 說明 |
|---|---|---|
| 筆記資料夾 | `%OneDrive%\Inkling`;沒有 OneDrive 時是 `Documents\Inkling` | 只接受完整路徑。指向還不存在的資料夾時，第一次存檔會建立它。旁邊的「瀏覽…」開系統的選資料夾對話框，選好就直接存 |
| 快速記下的分隔符 | `;;` | 前面是標題、後面是內文。長度不限，半形全形算同一個，清空就回到 `;;`。改完當下開著的快速記下頁就會跟上 |
| 記下後先看一眼 | 開啟 | Enter 記下並停在筆記上，再按一次才收起面板;關掉就是記完直接收起 |

存成功會跳一則提示並回到主搜尋框;被拒絕的(相對路徑、寫檔失敗)則留在表單上、打過的值還在，
改完再送一次就好。設定檔的位置、格式與「更新擴展之後還在嗎」見
[開發與部署](docs/development.md#settings-file)。

## 同步

Inkling **不做同步**，它只是把 Markdown 檔寫進你指定的資料夾，其餘交給雲端硬碟:
離線可用性、衝突處理、手機存取全部沿用 OneDrive 或 Dropbox 既有的能力。
要在手機上看就裝那個雲端硬碟的 App;Obsidian 之類的工具也可以指向同一個資料夾。

**OneDrive 使用者**:把 Inkling 資料夾設成「一律保留在此裝置上」(資料夾按右鍵)。
開著「檔案隨選」而檔案只有雲端佔位符時，讀取會觸發下載，搜尋就會卡住。多台機器同時編輯同一則
筆記時，OneDrive 會產生 `檔名-電腦名.md` 這種副本，兩份都會出現在清單裡並標上**衝突副本**。
每一列動到的是它自己的檔案，所以在詳細窗格裡比一下，留下要的那份、把另一份刪掉。

## 遇到問題

**改了設定卻沒反應** — 先把設定頁關掉重開。那個頁面綁在某一個擴展實例上，中間發生過 Reload
或重新部署的話，按 Save 會靜靜地什麼都不做。

**介面不是預期的語言** — 語言跟著 Windows 的**顯示語言**走(不是「地區格式」那個設定)，
沒有設定項。剛改完顯示語言要重新登入才生效。

**設定全部退回預設值** — `settings.json` 變成不合法的 JSON 時(編輯器改壞、寫到一半當機)，
Inkling 會把它搬成 `settings.json.corrupt-<時間戳>` 再從預設值開始，存檔那條路才不會從此
靜靜地失效。設定頁最上面會講這件事並寫出檔名;舊的值還在那個檔案裡，要撈回來隨時可以。

其餘(部署、註冊、trimming、擴展自己的診斷 log)見
[開發與部署 → 排錯](docs/development.md#troubleshooting)。

## 文檔

<!-- README.md 這一節多一句「The in-depth docs are written in Traditional Chinese.」，
     對中文讀者是廢話，所以刻意不對應 —— 兩份 README 唯一容許不對齊的地方，別「補齊」。 -->

| | |
|---|---|
| [docs/development.md](docs/development.md) | 建置、部署、專案結構、排錯 |
| [docs/design-notes.md](docs/design-notes.md) | 「為什麼是這樣」的完整考證(快速記下為什麼是頁面不是 fallback、回饋為什麼只有三個通道、確認框的按鈕為什麼沒有顏色……)，對象是維護者與其他 CmdPal 擴展作者 |
| [docs/copy.md](docs/copy.md) | 所有對外文案的來源:Store listing、gallery 卡片、manifest 的描述 |
| [docs/known-issues.md](docs/known-issues.md) | 已知還沒修的缺陷，每條都附重現步驟 |
| [CHANGELOG.md](CHANGELOG.md) | 版本紀錄 |
| [CONTRIBUTING.md](CONTRIBUTING.md) | 動手改這個 repo 之前先讀 |
| [PRIVACY.md](PRIVACY.md) | 這個 app 收集什麼、送出什麼(都沒有) |

## 參與貢獻

歡迎回報 bug 與提功能建議(用 GitHub 的 issue 範本)。要送 PR 請先讀
[CONTRIBUTING.md](CONTRIBUTING.md) —— 這個 repo 有幾條不算顯然的規矩
(介面字串三份 `.resx` 一起改、改了行為要同步更新兩份 README 與手動驗證清單)。

## 授權

[MIT](LICENSE)
