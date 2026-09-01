# `.claude/skills/`

只有一份:`verify-cmdpal-ui/`,**這個 repo 自己寫的**。

## `verify-cmdpal-ui/`

在真機上驅動 Command Palette 的畫面驗證 Inkling —— 讀 UI Automation 樹、截圖、
打字與按快速鍵，補上 `docs/manual-test-checklist.md` 裡那些「只能靠眼睛」的項目。
工具是 `tools/cmdpal-ui.ps1`,`CLAUDE.md` 與 `CONTRIBUTING.md` 都指到這份。

重點結論(都是實測出來的，細節在那份 SKILL.md):

- **`orca computer` 那套看不到 CmdPal 的主面板** —— 它是 WinUI 3 應用，主面板
  永遠不會成為進程的 MainWindow(`MainWindowHandle` 平常是 0),orca 的視窗列舉
  照那個屬性過濾就整個跳過了。要走 UI Automation。**例外**:「Command Palette Settings」
  視窗開著時 `MainWindowHandle` 會指向它、orca 也列得出 CmdPal —— 但那是設定視窗，
  主面板依然列舉不到，對驗證沒有用。(orca 是作者自己的桌面工具，沒有它略過
  這條即可，核心驗證工具是 `tools/cmdpal-ui.ps1`。)
- **CmdPal 一失焦就自我隱藏**，所以一整串動作要在同一次呼叫裡跑完。
- 面板隱藏之後 **UIA 只回得到根節點** —— 那半截樹看起來像「畫面上什麼都沒有」。

## 以前這裡還有六份，2026-08-31 刪掉了

CmdPal「建立擴展」功能會在產生出來的專案裡附一份 `.github/`(API 速查加幾條常見工作流程,
上游是 `microsoft/PowerToys`,授權 MIT)。這個 repo 是從零寫的，沒有用模板，但曾經把
那份 `.github/` 的六份文件原封不動搬進這個資料夾:`add-adaptive-card-form/`、
`add-dock-band/`、`add-extension-settings/`、`add-fallback-commands/`、
`cmdpal-extension-api/`、`publish-extension/`。

**刪掉的理由**:那六份合計約 1400 行，是搬過來的 Microsoft 文件，不是這個 repo 寫的東西 ——
留著會讓一個從頭到尾原創的 repo 看起來像是拿模板長出來的。而且內容本身部分已經跟這個
repo 的取捨衝突或過期:`add-dock-band/` 記的是 `CLAUDE.md`〈評估過但沒有做〉那節
(`docs/design-notes.md#no-dock-band`)講過從未實作的功能;`add-fallback-commands/`
記的做法正是 `CLAUDE.md` 硬規則第 3 條明著說「不要把快速記下改回 fallback」的那條路。

真要重新查 CmdPal 官方怎麼寫這幾類功能，用 CmdPal 的「建立擴展」功能重新產生一份模板，
`.github/skills/` 底下就是原文 —— 不必也不該把它再搬進這個 repo。
