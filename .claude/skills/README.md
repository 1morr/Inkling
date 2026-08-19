# `.claude/skills/`

`verify-cmdpal-ui/` 是**這個 repo 自己寫的**,其餘六份是從 CmdPal 的擴展模板搬來的。

## `verify-cmdpal-ui/`(自己寫的)

在真機上驅動 Command Palette 的畫面驗證 Notelet —— 讀 UI Automation 樹、截圖、
打字與按快速鍵,補上 `docs/manual-test-checklist.md` 裡那些「只能靠眼睛」的項目。
工具是 `tools/cmdpal-ui.ps1`。

重點結論(都是實測出來的,細節在那份 SKILL.md):

- **`orca computer` 那套看不到 CmdPal** —— 它是 WinUI 3 應用,`MainWindowHandle`
  永遠是 0,orca 的視窗列舉照那個屬性過濾就整個跳過了。要走 UI Automation。
- **CmdPal 一失焦就自我隱藏**,所以一整串動作要在同一次呼叫裡跑完。
- 面板隱藏之後 **UIA 只回得到根節點** —— 那半截樹看起來像「畫面上什麼都沒有」。

---

# 底下六份是從 CmdPal 的擴展模板搬來的

來源是 CmdPal「建立擴展」功能產生出來的專案裡的 `.github/`(檔案日期 2026-05-22,
也就是 Command Palette 0.11 安裝包裡夾帶的那一份)。這個 repo 是從零寫的,沒有用模板,
但那份 `.github/` 是模板唯一真正多出來的東西 —— 官方整理的 API 速查與幾條常見工作流程,
其中 dock band 是本專案完全沒碰過的功能面。

## 對照表

| 這裡 | 來源 |
|---|---|
| `cmdpal-extension-api/` | `.github/instructions/cmdpal-extension.instructions.md` |
| `add-adaptive-card-form/` | `.github/skills/add-adaptive-card-form/` |
| `add-dock-band/` | `.github/skills/add-dock-band/` |
| `add-extension-settings/` | `.github/skills/add-extension-settings/` |
| `add-fallback-commands/` | `.github/skills/add-fallback-commands/` |
| `publish-extension/` | `.github/skills/publish-extension/` |

模板的 `SKILL.md` 本來就是 `name` + `description` 的 YAML frontmatter,跟 Claude Code
認的格式一樣,所以那五個 skill 只是換了位置。`cmdpal-extension.instructions.md` 是
Copilot 專用的格式(`description` + `applyTo: '**/*.cs'`),frontmatter 換成
`name` + `description` 才讀得到,正文一個字沒動。

**`.github/copilot-instructions.md` 沒有搬。** 它講的是專案結構、建置部署、可用的 skill 清單
—— 這個 repo 的 `CLAUDE.md` 同樣的東西寫得更準(它還停在「用 Visual Studio 的 Build > Deploy」,
而這台機器沒有 VS)。多一份會互相打架。

## 每一份都加了「本專案的例外」

**正文一個字都沒改**,只在 frontmatter 後面插了一塊引言,寫這個 repo 實測到、跟上游文檔
衝突的地方。這件事非做不可:那份文檔推薦 `ToastStatusMessage`,而在這個專案裡發一個 toast
等於把整個 CmdPal 面板關掉(見 README〈刪除成功時一個 toast 都不發〉);它也把
`ListItem.Details` 寫成一般屬性,而那條通知路徑跨進程是斷的。照著做不會有編譯錯誤,
只會得到「值改了、畫面不動」這種查半天的症狀。

正文不動是為了之後好對差異:CmdPal 更新之後重新產生一份模板,直接 diff 正文就知道上游改了什麼。

## 重新同步

```powershell
# 用 CmdPal 的「建立擴展」產生一份新模板,然後比對正文
$new = "<新模板路徑>\.github"
Compare-Object (Get-Content "$new\skills\add-dock-band\SKILL.md") `
               (Get-Content ".claude\skills\add-dock-band\SKILL.md")
```

差異裡開頭那一塊 `>` 引言是我們自己加的,其餘才是上游的改動。
