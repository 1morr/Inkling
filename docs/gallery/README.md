# Gallery 投稿素材(草稿)

這個資料夾是投稿 [microsoft/CmdPal-Extensions](https://github.com/microsoft/CmdPal-Extensions)
gallery 的準備材料。**要等 Store 上架之後才能送。**

`installSources` 已經填上真的 Store product ID(`9NDGWN4JTXHH`,2026-08-23 在 Partner Center
保留名稱時拿到的)，但**那個 listing 要通過認證才會活** ——
<https://apps.microsoft.com/detail/9NDGWN4JTXHH> 在那之前是 404，而 gallery 的 CI 與人工審核
都會去點它。順序是:Store 送審 → 通過並上架 → 才開這個 PR。

⚠ **`title` 是 `Inkling Notes`，跟 Store 上的名字一致** —— `Inkling` 被商標擋下了
(見 [`release-checklist.md` §1](../release-checklist.md))。CmdPal 面板裡的命令標題仍然是
「Inkling」，那是 `.resx`，跟這裡無關。

## 檔案

- `extension.json` — 投稿用中繼資料草稿。送出去之前要確認:
  - `installSources`:換成真實的 WinGet package identifier 或 msstore product ID
    (上架 Store / WinGet 之後才會有，先完成那一站再回來改這裡)。
  - `homepage`:`https://github.com/1morr/Inkling`,repo 已公開、路徑核對過。
  - `author.url`:`https://github.com/1morr`，帳號頁面存在。
  - `id` 是 `1morr.inkling`，對應投稿 repo 裡的資料夾 `extensions/1morr/inkling/`,
    兩邊必須一致(CI 會驗)。
- `icon.png` 不放在這裡 —— 由 `tools\render-icons.ps1` 產生在 `assets\gallery\icon.png`
  (256×256 PNG、≤100 KB，腳本會驗尺寸與大小)。投稿時複製過去，跟 `extension.json`
  放同一個資料夾。

## 欄位規則(對岸 CI 會擋的)

- `title` ≤100 字，**不可含 “for Command Palette”**(gallery 裡那是冗贅)。
- `shortDescription` ≤200 字;`description` ≤3000 字。
- `categories` 最多 3 個，只能從固定清單挑;Inkling 用 `productivity`。
- `tags` 最多 5 個、每個 ≤30 字。
- `icon`:PNG 或 JPEG(**SVG 不收**)、≤100 KB、建議 256×256，檔名要跟 `icon` 欄位一致。
- 可選 `screenshots/` 子資料夾:PNG/JPEG、每張 ≤1 MB、最多 5 張
  (README 用的 `docs/images/*.png` 直接拿得來用，投稿時複製過去並加前綴;GIF 不收),
  檔名按字母序決定順序(用 `01-`、`02-` 前綴控制)。

## 投稿流程

1. **先把擴展上架**:Store 或 WinGet 至少要有一個 —— gallery 的 `installSources`
   只接受這兩種。**套件身分已經換成 Partner Center 指派的了**(2026-08-23，見
   [`release-checklist.md` §1](../release-checklist.md));剩下的是把 msixbundle 送審、
   等它通過。**通過之前不要開 PR** —— listing 還是 404。
2. Fork `microsoft/CmdPal-Extensions`。
3. 建 `extensions/1morr/inkling/`，放入 `extension.json` 與 `icon.png`(可加 `screenshots/`)。
4. 開 PR。第一次送 microsoft 的 repo 要簽 **Microsoft CLA**(CLA bot 會在 PR 裡提示，
   照著做即可)。
5. CI 自動驗 schema(欄位、字數、類別清單、id 與資料夾路徑一致、圖示格式與大小)。
6. CmdPal 團隊人工審核，merge 之後擴展出現在 gallery。

**`extension.json` 的 `$schema` 在這個 repo 裡是指不到的，那是正常的。**
那個相對路徑(`../../../.github/schemas/extension.schema.json`)是以**投稿之後的位置**
為基準算的 —— 檔案放進 `microsoft/CmdPal-Extensions` 的 `extensions/<author>/inkling/`
之後，往上三層剛好是那個 repo 的根目錄。留在這裡只是為了讓草稿與最終檔案一字不差，
不要為了「修掉紅線」把它改成別的路徑。
