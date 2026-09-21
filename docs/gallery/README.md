# Gallery 投稿素材

這個資料夾是投稿 [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions)
gallery 的材料。**條目 2026-09-04 已上線**（[PR #165](https://github.com/microsoft/CmdPal-Extensions/pull/165)）。
之後要改條目走同一條路，材料留在這裡。

gallery 的 `installSources` 只接受 msstore 或 WinGet 的 id，而且 CI 與人工審核都會去點那個
listing。Store 的 <https://apps.microsoft.com/detail/9NDGWN4JTXHH> 2026-08-25 上架生效，
這個 id 已經是真的。

**文案不要在這裡改。** `extension.json` 的 `shortDescription` 與 `description` 從
[`docs/copy.md`](../copy.md) 抄過來，那一份是所有對外文案的來源。

⚠ **投稿時要把 `docs/gallery/extension.json` 整個檔案複製過去，不要在投稿 repo 那邊
重打或改字。** 踩過：2026-09-03 建投稿分支時 `shortDescription` 被臨場改短成
「Take notes without leaving Command Palette. They are plain Markdown files in a folder
you choose.」，而那句**整個 repo 的歷史裡從來沒有出現過**（`git log --all -S` 掃得掉），
所以本機看不出任何異狀 —— 直到條目上線、拿 feed 跟 `docs/copy.md` 逐欄位比對才發現。
掉的那半正好是「做什麼」（打字、按 Enter），剩下的只講檔案放哪，而 `They` 也沒有先行詞。
修正見下面〈後續修正〉。**上線之後想確認有沒有漂，拿
`https://raw.githubusercontent.com/microsoft/CmdPal-Extensions/main/extensions.json`
的條目跟 `docs/gallery/extension.json` 逐欄位 diff，不要只看畫面。**

⚠ **`title` 是 `Inkling Notes`，跟 Store 上的名字一致** —— `Inkling` 被商標擋下了
（見[設計考證〈套件身分凍結在 Partner Center 指派的那一組〉](../design-notes.md#package-identity)）。CmdPal 面板裡的命令標題仍然是
「Inkling」，那是 `.resx`，跟這裡無關。

## 檔案

- `extension.json` — 投稿用的中繼資料。送出去之前再核一次：
  - `installSources`：`msstore` / `9NDGWN4JTXHH`，**已經是真的**（2026-08-26 用
    `winget show --id 9NDGWN4JTXHH --source msstore` 驗過查得到）。
  - `homepage`：`https://github.com/1morr/Inkling`，repo 已公開、路徑核對過。
  - `author.url`：`https://github.com/1morr`，帳號頁面存在。
  - `id` 是 `1morr.inkling`，對應投稿 repo 裡的資料夾 `extensions/1morr/inkling/`，
    兩邊必須一致（CI 會驗）。
  - `tags` 是**搜尋用的關鍵字，不是形容詞**。gallery 網站與 CmdPal 應用內都做子字串比對，
    網站那邊**不比對 `description`**（只比 title / shortDescription / author / tags /
    categories）—— 所以只出現在描述裡的字，在網站上搜不到。`writing` 曾經佔一格，
    2026-09-03 換成 `scratchpad`：前者 80 個既有擴展裡 0 人用、也不是我們的功能，
    後者是我們自己的功能名，而且是網站搜得到它的唯一途徑。
  - `categories` 只給 `productivity`，**這是刻意的**。80 個既有擴展裡 55 個給兩個，
    最常見的第二個是 `utilities-and-tools`（62 次），而最直接的競品 `qqshi13.quick-notes`
    兩個桶都在 —— 也就是說使用者在那一頁瀏覽看得到它、看不到我們。但上游分類表裡
    `productivity` 的定義明著寫了 "note-taking"，`utilities-and-tools` 是
    "Calculators, converters, file managers"，我們不是。**用曝光換分類正確性的代價太隱形，
    要改的話先想清楚。**
- `icon.png` 不放在這裡 —— 由 `tools\render-icons.ps1` 產生在 `assets\gallery\icon.png`
  （256×256 PNG、≤100 KB，腳本會驗尺寸與大小）。投稿時複製過去，跟 `extension.json`
  放同一個資料夾。

## 欄位規則（投稿 repo 的 CI 會擋的）

- `title` ≤100 字，**不可含 “for Command Palette”**（gallery 裡那是冗贅）。
- `shortDescription` ≤200 字；`description` ≤3000 字。
- `categories` 最多 3 個，只能從固定清單挑；Inkling 用 `productivity`。
- `tags` 最多 5 個、每個 ≤30 字。
- `icon`：PNG 或 JPEG（**SVG 不收**）、≤100 KB、建議 256×256，檔名要跟 `icon` 欄位一致。
- 可選 `screenshots/` 子資料夾：PNG/JPEG、每張 ≤1 MB、最多 5 張，**GIF 不收**，
  而且**尺寸與比例完全沒有規定** —— schema 與上游的 `validate.py` 都沒有驗。
  檔名按字母序決定順序（用 `01-`、`02-` 前綴控制）。用的是 `assets/gallery/*.png`，
  檔名已經帶好前綴，跟 `icon.png` 放在同一個資料夾，投稿時整包複製過去就好。
  三張都進版控，平常不必重產；改了 `docs/images/` 的來源截圖才跑
  `pwsh -NoProfile -File tools\make-store-screenshots.ps1 -Bare`。

  ⚠ **不要拿 `assets/store/*.png` 去送。** 那一組是 1920×1080、鋪了 Windows 桌布的
  合成圖，存在的唯一理由是 Store listing 的 1366×768 下限 —— gallery 沒有下限，
  而它的卡片本來就只有面板那麼大，鋪一張桌面等於把面板縮到更小（而且那三張每張
  逼近 1 MB 上限）。gallery 這一組是 `-Bare` 出來的裸面板，約 1178×709、70-90 KB，
  跟 52 個有截圖的既有擴展裡那 31 個的做法一致，包括官方的
  `microsoft/sample-extension` 與 CmdPal 開發者自己的 `zadjii/virtual-desktops`。

## 改條目的流程

在 fork（<https://github.com/1morr/CmdPal-Extensions>）開新分支，把
`docs/gallery/extension.json`（以及圖示、截圖，如果有換）整包複製到
`extensions/1morr/inkling/`，開 PR 到上游 `main`。要注意的：

- **第一次送 microsoft 的 repo 要簽 CLA，簡法是在 PR 裡留一則
  `@microsoft-github-policy-service agree`。** 而且 CLA 在這個 repo 不是硬關卡：
  維護者可能在 check 還 queued 時就 merge，PR 列表上的黃點不必理，要看的是 `validate`。
- **merge 之後不會馬上出現在 gallery 裡。** 根目錄的 feed 要維護者另外開
  `Update extensions.json` 的 PR 重產才生效（上游 `docs/CONTRIBUTING.md` 第 9 步）。
- **gallery 的網站是死的，別拿它驗。** <https://microsoft.github.io/CmdPal-Extensions/>
  回 404；實際看得到條目的地方是 **CmdPal 應用內的擴展清單**，它直接讀 raw 的
  `extensions.json`。同一次 merge 觸發的 `Deploy extension gallery to GitHub Pages`
  失敗也是同一個原因，**那不是我們的問題**。
- 上游的 `validate.py` 可以先在本機跑過再開 PR。
- ⚠ **刪 fork 重建時**：`gh` 做不到（token 沒有 `delete_repo` scope），得上 fork 的
  Settings → Danger Zone；而且**刪 fork 會連遠端分支一起帶走，本機 clone 是唯一救生索**
  —— 刪之前先確認它還在。

<a id="後續修正"></a>
## 後續修正

| 改什麼 | 分支 | 狀態 |
|---|---|---|
| `shortDescription` 改回 `docs/copy.md` 的版本（見上面那則 ⚠） | `fix-inkling-shortdescription` | ✅ [PR #183](https://github.com/microsoft/CmdPal-Extensions/pull/183)（2026-09-21 開） |

原先的修正分支隨 fork 被刪而消失；2026-09-21 重建 fork、從 upstream `main` 重開同名分支，
一樣只動 `extension.json` 一行，`validate.py` 本機跑過。

**真正的關卡是 CI，不是人工審核。** 2026-09-03 翻過上游最近 30 個 PR：投稿類幾乎都是
維護者直接 Approve、當天或隔天 merge，沒有人被要求改描述或截圖。兩件沒 merge 的都不是
品質問題 —— #109 是資料夾用了大寫又沒回應被關掉，#134 是一張截圖超過 1 MB（換掉就過了）。
