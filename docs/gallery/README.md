# Gallery 投稿素材

這個資料夾是投稿 [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions)
gallery 的材料。**分支已經推上去了，只差開 PR** —— 見下面〈投稿流程〉。

gallery 的 `installSources` 只接受 msstore 或 WinGet 的 id，而且 CI 與人工審核都會去點那個
listing。Store 的 <https://apps.microsoft.com/detail/9NDGWN4JTXHH> 2026-08-25 上架生效，
`installSources` 填的 `9NDGWN4JTXHH` 現在是活的。

**文案不要在這裡改。** `extension.json` 的 `shortDescription` 與 `description` 從
[`docs/copy.md`](../copy.md) 抄過來，那一份是所有對外文案的來源。

⚠ **`title` 是 `Inkling Notes`，跟 Store 上的名字一致** —— `Inkling` 被商標擋下了
(見[設計考證〈套件身分凍結在 Partner Center 指派的那一組〉](../design-notes.md#package-identity))。CmdPal 面板裡的命令標題仍然是
「Inkling」，那是 `.resx`，跟這裡無關。

## 檔案

- `extension.json` — 投稿用的中繼資料。送出去之前再核一次:
  - `installSources`:`msstore` / `9NDGWN4JTXHH`,**已經是真的**(2026-08-26 用
    `winget show --id 9NDGWN4JTXHH --source msstore` 驗過查得到)。
  - `homepage`:`https://github.com/1morr/Inkling`,repo 已公開、路徑核對過。
  - `author.url`:`https://github.com/1morr`，帳號頁面存在。
  - `id` 是 `1morr.inkling`，對應投稿 repo 裡的資料夾 `extensions/1morr/inkling/`,
    兩邊必須一致(CI 會驗)。
  - `tags` 是**搜尋用的關鍵字，不是形容詞**。gallery 網站與 CmdPal 應用內都做子字串比對，
    網站那邊**不比對 `description`**(只比 title / shortDescription / author / tags /
    categories)—— 所以只出現在描述裡的字，在網站上搜不到。`writing` 曾經佔一格，
    2026-09-03 換成 `scratchpad`:前者 80 個既有擴展裡 0 人用、也不是我們的功能，
    後者是我們自己的功能名，而且是網站搜得到它的唯一途徑。
  - `categories` 只給 `productivity`,**這是刻意的**。80 個既有擴展裡 55 個給兩個，
    最常見的第二個是 `utilities-and-tools`(62 次)，而最直接的競品 `qqshi13.quick-notes`
    兩個桶都在 —— 也就是說使用者在那一頁瀏覽看得到它、看不到我們。但上游分類表裡
    `productivity` 的定義明著寫了 "note-taking",`utilities-and-tools` 是
    "Calculators, converters, file managers"，我們不是。**用曝光換分類正確性的代價太隱形，
    要改的話先想清楚。**
- `icon.png` 不放在這裡 —— 由 `tools\render-icons.ps1` 產生在 `assets\gallery\icon.png`
  (256×256 PNG、≤100 KB，腳本會驗尺寸與大小)。投稿時複製過去，跟 `extension.json`
  放同一個資料夾。

## 欄位規則(投稿 repo 的 CI 會擋的)

- `title` ≤100 字，**不可含 “for Command Palette”**(gallery 裡那是冗贅)。
- `shortDescription` ≤200 字;`description` ≤3000 字。
- `categories` 最多 3 個，只能從固定清單挑;Inkling 用 `productivity`。
- `tags` 最多 5 個、每個 ≤30 字。
- `icon`:PNG 或 JPEG(**SVG 不收**)、≤100 KB、建議 256×256，檔名要跟 `icon` 欄位一致。
- 可選 `screenshots/` 子資料夾:PNG/JPEG、每張 ≤1 MB、最多 5 張，**GIF 不收**,
  而且**尺寸與比例完全沒有規定** —— schema 與上游的 `validate.py` 都沒有驗。
  檔名按字母序決定順序(用 `01-`、`02-` 前綴控制)。用的是 `assets/gallery/*.png`,
  檔名已經帶好前綴，跟 `icon.png` 放在同一個資料夾，投稿時整包複製過去就好。
  三張都進版控，平常不必重產;改了 `docs/images/` 的來源截圖才跑
  `pwsh -NoProfile -File tools\make-store-screenshots.ps1 -Bare`。

  ⚠ **不要拿 `assets/store/*.png` 去送。** 那一組是 1920×1080、鋪了 Windows 桌布的
  合成圖，存在的唯一理由是 Store listing 的 1366×768 下限 —— gallery 沒有下限，
  而它的卡片本來就只有面板那麼大，鋪一張桌面等於把面板縮到更小(而且那三張每張
  逼近 1 MB 上限)。gallery 這一組是 `-Bare` 出來的裸面板，約 1178×709、70-90 KB,
  跟 52 個有截圖的既有擴展裡那 31 個的做法一致，包括官方的
  `microsoft/sample-extension` 與 CmdPal 開發者自己的 `zadjii/virtual-desktops`。

## 投稿流程

1. ✅ **先把擴展上架** —— Store 2026-08-25 上架，這一步做完了。
2. ✅ **Fork 與分支**(2026-09-03)—— <https://github.com/1morr/CmdPal-Extensions>
   的 `add-1morr-inkling`,**單一 commit** `Add 1morr.inkling to the gallery`,
   直接坐在上游 `main` 上(ahead 1 / behind 0)。

   這個 fork 是**當天第二次建的**。第一次那個分支上有兩個 commit,第一個把操作路徑
   寫錯了(見 [`docs/copy.md`](../copy.md) 那一輪的修正)，留在 PR 裡只會讓 reviewer
   讀到一份錯的中間狀態。整個 fork 砍掉重建之後，遠端只剩正確的那一個 commit。

   ⚠ **刪 fork `gh` 做不到，得上網頁。** token 沒有 `delete_repo` scope
   (`gh auth status` 看得到:`gist, read:org, repo, workflow`)，而
   `gh auth refresh -h github.com -s delete_repo` 會開瀏覽器。網頁上的位置是 fork 的
   Settings → 最下面的 Danger Zone。重建就是
   `gh repo fork microsoft/CmdPal-Extensions --clone=false`。

   ⚠ **刪 fork 會連遠端分支一起帶走，本機那份 clone 是唯一的救生索。**
   這次重來只花一行 `git push -u origin add-1morr-inkling`,commit 內容與訊息
   一字未改 —— 前提是本機的 clone 還在。它放在暫存目錄裡，**刪 fork 之前先確認
   它沒被清掉**，否則五個檔案要重放、commit 訊息要重寫。
3. ✅ **檔案** —— `extensions/1morr/inkling/` 底下是 `extension.json`、`icon.png`
   與 `screenshots/01..03`,全部複製自 `assets/gallery/`(那個資料夾裡就是這四個檔)。
   截圖 2026-09-04 從 1920×1080 的合成圖換成裸面板，理由見上一節。
4. ⬜ **開 PR** —— 這一步留給人按:
   <https://github.com/microsoft/CmdPal-Extensions/compare/main...1morr:CmdPal-Extensions:add-1morr-inkling>
   target 選 `main`。PR 範本是一張七項的 checklist，逐項都已經符合。
   第一次送 microsoft 的 repo 要簽 **Microsoft CLA**。CLA bot 會在 PR 裡留言，
   **簽法是回一則留言**，內容就是 `@microsoft-github-policy-service agree`
   (代表公司的話後面加 `company="..."`)。不簽不會 merge。
5. (上游)CI 自動驗 schema(欄位、字數、類別清單、id 與資料夾路徑一致、圖示格式與大小)。
   同一支腳本可以先在本機跑，見下一節。
6. (上游)CmdPal 團隊人工審核。**merge 之後還不會馬上出現在 gallery 裡** —— 維護者要
   另外開一個 PR 重產根目錄的 `extensions.json`，那一份 merge 了才生效(上游的
   `docs/CONTRIBUTING.md` 第 9 步)。

**真正的關卡是 CI，不是人工審核。** 2026-09-03 翻過上游最近 30 個 PR:投稿類幾乎都是
維護者直接 Approve、當天或隔天 merge，沒有人被要求改描述或截圖。兩件沒 merge 的都不是
品質問題 —— #109 是資料夾用了大寫又沒回應被關掉，#134 是一張截圖超過 1 MB(換掉就過了)。

**之後要改條目**(換描述、換圖示、加截圖)走同一條路:在 fork 開新分支，改
`extensions/1morr/inkling/` 底下的檔案，再開一個 PR。文案一樣先改
[`docs/copy.md`](../copy.md)。

## 送出前自己驗一次

投稿 repo 自己帶著 CI 用的那支腳本，clone 下來就能跑同一份:

```powershell
python -m pip install jsonschema
$env:PYTHONIOENCODING = 'utf-8'   # 少了它，腳本印 ✅ 會在 cp950 主控台上炸掉
python .github\scripts\validate.py extensions\1morr\inkling\extension.json
```

(那個環境變數是為了輸出被導向檔案或管線的情況:那時 Python 退回 locale 編碼，
cp950 印不出 ✅ 會直接 `UnicodeEncodeError`。直接在主控台跑通常不會踩到，設著比較穩。)

它除了驗 schema，還會抓 <https://apps.microsoft.com/detail/9NDGWN4JTXHH> 的 `og:title`
來跟 `title` 比。**對不上只是 warning**，但那正是 `title` 得跟 Store 上的名字一致的理由。
2026-09-03 跑過，零錯誤零 warning。

**`extension.json` 的 `$schema` 在這個 repo 裡是指不到的，那是正常的。**
那個相對路徑(`../../../.github/schemas/extension.schema.json`)是以**投稿之後的位置**
為基準算的 —— 檔案放進 `microsoft/CmdPal-Extensions` 的 `extensions/<author>/inkling/`
之後，往上三層剛好是那個 repo 的根目錄。留在這裡只是為了讓草稿與最終檔案一字不差，
不要為了「修掉紅線」把它改成別的路徑。
