# 對外文案

這一份是**所有對外文案的來源**。改文案先改這裡，再同步到下面那張表列出的地方;
反過來(先改 Store 再回頭補這裡)保證會漂移，因為 Store 的文字不在 repo 裡，
下一個人打開 repo 是看不到的。

| 文案 | 實際住在哪 | 怎麼生效 |
|---|---|---|
| Store listing × 3 語言 | Partner Center(不在 repo 裡) | 開一個新的 submission 再送審，見 [`release-runbook.md` 第 17 步](release-runbook.md) |
| Gallery 卡片 | `docs/gallery/extension.json` | 投稿 `microsoft/CmdPal-Extensions` 時一起送 |
| Windows 應用程式清單的副標 | `src/Inkling/Package.appxmanifest` 的 `Description`(兩處，要一致) | 跟著套件發版 |
| 使用者文檔 | `README.md` / `README.zh-Hant.md` | 推上 master 就生效 |

## 三條規則

1. **alias 是可選的加速方式，不是第一步。** 預設路徑是「叫出面板 → 打 Inkling → 選快速記下」，
   那條路不設定任何東西就能走完。alias 與全域快速鍵一律寫在後面，用「想更快的話」的語氣帶出來。
   把 alias 寫成前置步驟會讓人以為不設就不能用。
2. **講做什麼，不講為什麼。** 對外文案只寫「按哪個鍵、發生什麼事、檔案存到哪」。
   考證、取捨、我們踩過的坑收在 `docs/design-notes.md`，README 用連結指過去就好。
   避免破折號堆疊、警句式的因果句、三段排比 —— 那是這份 repo 的文案以前最明顯的毛病。
3. **內部細節不要出現在 Store。** front matter 的 id 怎麼設計、`Note.IsExternal`、
   「沒有宣告網路能力」這種實作語彙，使用者不需要知道，也看不懂。
   隱私講一句「筆記只留在你自己的電腦上，Inkling 不蒐集也不傳送任何資料」就夠了。

中文標點見 [CLAUDE.md〈慣例〉](../CLAUDE.md):逗號全形，括號與冒號維持半形。

---

## Microsoft Store listing

三個語言各一份完整的 listing(Partner Center 強制)，所以每一次改動都要改三份。
「What's new in this version」不在這裡，每次發版時從 CHANGELOG 譯，見 runbook 第 17 步。

### English (United States)

**Short description**

```
Take notes without leaving Command Palette. Type your thought, press Enter, and it is saved as a Markdown file.
```

**Description**

```
Inkling lets you take notes inside PowerToys Command Palette. Summon the palette, type your thought, press Enter. The note is saved.

Your notes are ordinary Markdown files in a folder you pick. Open them in any editor, sync them with OneDrive or any other cloud drive, or drop your existing .md files into the folder and they show up in the list right away.

Requires PowerToys Command Palette 0.11 or later, on Windows 10 version 2004 (10.0.19041) or later. Inkling is an extension for Command Palette, so it has no window of its own; everything happens inside the palette.

The interface follows your Windows display language (English, Traditional Chinese, Simplified Chinese). Your notes stay on your PC. Inkling collects nothing and sends nothing.
```

**Product features**(一行一條)

```
Quick capture: type your thought, press Enter, and it is saved. A separator you choose writes the title and the body in one line.
Notes list: search, preview, edit, copy the body, open in your editor, show the file, or delete (to the Recycle Bin, on drives that have one).
Scratchpad: one permanent note for thoughts that do not need a title yet. It opens with whatever you left there last time.
Plain Markdown: front matter fields Inkling does not know are left untouched, so it coexists with Obsidian, VS Code, and anything else that reads .md.
Keyboard-first: every action has a shortcut.
Optional shortcut: assign an alias or a global hotkey to Quick capture in Command Palette settings to jump straight in.
```

### 中文(繁體)

**簡短描述**

```
不離開 Command Palette 就能記筆記。打字、按 Enter，想法就存成一個 Markdown 檔。
```

**描述**

```
Inkling 讓你在 PowerToys Command Palette 裡直接記筆記。叫出面板，打字，按 Enter，筆記就存好了。

筆記是普通的 Markdown 檔，放在你自己指定的資料夾裡。你可以用任何編輯器打開它們，用 OneDrive 之類的雲端硬碟同步;把手上既有的 .md 檔放進那個資料夾，它們也會直接出現在清單裡。

需要 PowerToys Command Palette 0.11 以上，以及 Windows 10 2004(10.0.19041)以上。Inkling 是 Command Palette 的擴展，沒有自己的視窗，所有操作都在面板裡完成。

介面語言跟著 Windows 的顯示語言走(英文、繁體中文、簡體中文)。筆記只留在你自己的電腦上，Inkling 不蒐集也不傳送任何資料。
```

**產品功能**

```
快速記下:打完字按 Enter 就存成一則筆記。想連內文一起寫，用你設的分隔符一行寫完標題和內文。
筆記清單:搜尋、預覽、編輯、複製內文、在編輯器開啟、開啟檔案位置，或刪除(送資源回收筒)。
隨手草稿:一則常駐的便條，給還沒成形、不必取標題的想法。打開就是你上次留在那裡的內容。
純 Markdown:不認得的 front matter 欄位原樣保留，跟 Obsidian、VS Code 之類的工具並存沒問題。
全鍵盤操作:常用的動作都有快速鍵。
想更快可以設快速鍵:在 Command Palette 設定裡給「快速記下」設一個 alias 或全域快速鍵，一步就跳進去。
```

### 中文(簡體)

**簡短描述**

```
不离开 Command Palette 就能记笔记。输入、按 Enter，想法就存成一个 Markdown 文件。
```

**描述**

```
Inkling 让你在 PowerToys Command Palette 里直接记笔记。叫出面板，输入，按 Enter，笔记就存好了。

笔记是普通的 Markdown 文件，放在你自己指定的文件夹里。你可以用任何编辑器打开它们，用 OneDrive 之类的云盘同步;把手上已有的 .md 文件放进那个文件夹，它们也会直接出现在列表里。

需要 PowerToys Command Palette 0.11 以上，以及 Windows 10 2004(10.0.19041)以上。Inkling 是 Command Palette 的扩展，没有自己的窗口，所有操作都在面板里完成。

界面语言跟着 Windows 的显示语言走(英文、繁体中文、简体中文)。笔记只留在你自己的电脑上，Inkling 不收集也不发送任何数据。
```

**產品功能**

```
快速记录:输入完按 Enter 就存成一条笔记。想连正文一起写，用你设的分隔符一行写完标题和正文。
笔记列表:搜索、预览、编辑、复制正文、在编辑器中打开、打开文件位置，或删除(送回收站)。
随手草稿:一条常驻的便签，给还没成形、不必取标题的想法。打开就是你上次留在那里的内容。
纯 Markdown:不认识的 front matter 字段原样保留，跟 Obsidian、VS Code 之类的工具并存没问题。
全键盘操作:常用的动作都有快捷键。
想更快可以设快捷键:在 Command Palette 设置里给“快速记录”设一个 alias 或全局快捷键，一步就跳进去。
```

---

## Gallery(`docs/gallery/extension.json`)

`title` 是 `Inkling Notes`，跟 Store 上的名字一致，不要改成 `Inkling`
(商標問題，見[設計考證](design-notes.md#package-identity))。

**shortDescription**(上限 200 字)

```
Take notes without leaving Command Palette. Type your thought, press Enter, and it is saved as a Markdown file in a folder you choose.
```

**description**(上限 3000 字)

```
Inkling lets you take notes inside PowerToys Command Palette. Summon the palette, type your thought, press Enter. The note is saved.

Your notes are ordinary Markdown files in a folder you pick. Open them in any editor, sync them with OneDrive or any other cloud drive, or drop your existing .md files into the folder and they show up in the list right away.

- Quick capture: type your thought, press Enter, and it is saved. A separator you choose writes the title and the body in one line.
- Notes list: search, preview, edit, copy the body, open in your editor, show the file, or delete (to the Recycle Bin, on drives that have one).
- Scratchpad: one permanent note for thoughts that do not need a title yet.
- Plain Markdown: front matter fields Inkling does not know are left untouched, so it coexists with Obsidian, VS Code, and anything else that reads .md.
- Keyboard-first: every action has a shortcut. Assign an alias or a global hotkey to Quick capture if you want to jump straight in.
```

---

## Windows 應用程式清單(`Package.appxmanifest` 的 `Description`)

英文，最短的一個槽，必須經得起被截斷。`uap:VisualElements` 與 `uap3:AppExtension`
**兩處要一字不差**。

```
Capture thoughts in seconds, right in Command Palette
```

---

## README 頂部那一句

兩份 README 的第一行，比 Store 的短描述長一點，放得下「Markdown 檔」與「資料夾」。

**English**

```
Take notes without leaving PowerToys Command Palette. Type your thought, press Enter, and it is saved as a Markdown file in a folder you choose.
```

**繁體中文**

```
不離開 PowerToys Command Palette 就能記筆記。打字、按 Enter，想法就存成你指定資料夾裡的一個 Markdown 檔。
```
