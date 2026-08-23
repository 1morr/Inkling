# 已知缺陷

還沒修的東西。**每一條都要在真機上重現過或讀原始碼確認過**,不是猜測。

這份文檔跟 [`design-notes.md`](design-notes.md) 的分工:那邊記「為什麼是這樣」(已經決定的
取捨),這邊記「這樣是錯的,只是還沒修」。**一條修掉就從這裡刪掉** —— 修好的東西留在這裡
比沒寫更糟,它會讓人以為問題還在。行為變了就同輪更新兩份 README、
[`manual-test-checklist.md`](manual-test-checklist.md) 與 [`CHANGELOG.md`](../CHANGELOG.md)。

**「查過、量過,然後決定不做」的東西不進這裡**,進
[設計考證〈評估過但沒有做〉](design-notes.md#deferred)。差別是:那邊是決定,這邊是債。

嚴重度:**阻擋發布** / **應修** / **建議**。

---

## 目前沒有

2026-08-23 的全量驗證抓到一條(K-18:`GoHome()` 把 InfoBadge 一起吃掉,用完整表單
新增筆記完全沒有確認),**同一輪修掉了**,所以照上面那條規則從這裡刪掉 ——
處置與考證寫在[設計考證〈編輯表單〉](design-notes.md#edit-form)最後一條。

## 更早之前的 17 條去哪了

首次公開發佈前的總體檢列出過 17 條(K-1 ~ K-17),**同一輪全部處理完了**。
做了什麼、為什麼那樣做,分別在:

| 原本那一條在講什麼 | 現在寫在哪 |
|---|---|
| 同一個 `id` 的兩份檔案,編輯與刪除作用在錯的那份 | [設計考證〈解析一則筆記認的是路徑,不是 `id`〉](design-notes.md#identity-is-the-path) |
| `settings.json` 壞掉之後設定永久性、無聲地存不回去 | [〈`settings.json` 壞掉時把它搬走〉](design-notes.md#settings-quarantine) |
| 非 UTF-8 的外來檔被讀成亂碼、編輯後原始位元組消失 | [〈非 UTF-8 的檔案整個跳過〉](design-notes.md#strict-utf8) |
| 日期解析失敗被靜靜丟棄並改寫 | [〈讀不懂的日期原樣留著〉](design-notes.md#unreadable-dates) |
| `CommandResult.GoBack()` 不動,編輯表單存完停在原地 | [〈編輯表單〉](design-notes.md#edit-form)最後一段 |
| 編輯表單的 `Enter` 會跳外部編輯器並丟掉未存的輸入 | [〈編輯表單的 `Enter` 是一顆什麼都不做的命令〉](design-notes.md#edit-form-enter) |
| 「只刪 Inkling 建立的」會刪掉別人的 vault 檔 | [〈「不是 Inkling 建立的」判準是 `id` 的形狀〉](design-notes.md#external-id-shape) |
| 快速記下頁帶著上一次的字進來 | `QuickCapturePage.ClearQuery` 上的註解 |
| `Section` 是死碼,分節標頭從來沒出現過 | [〈分節標頭:`Section` 不是分組鍵〉](design-notes.md#section-not-grouping) |
| 摘要與推導標題的 120 字截斷會切開代理對 | `NoteBody.Truncate` + `DataIntegrityTests` |
| 設定頁三個設定項之間沒有分隔線 | [〈設定卡片上沒有分隔線,因為畫不出來〉](design-notes.md#settings-no-separator) |
| `DiagnosticLog.Failure` 把標題與使用者名字寫進共用 log,訊息還是中文 | [〈診斷 log 有兩個通道〉](design-notes.md#log-two-channels) |
| `diagnostic.log` 沒有大小上限 | 同上 |
| 上架之後 `Get-AppxPackage -Name Inkling` 全部落空 | `tools/deploy.ps1` 的 `$packageNamePattern` 註解 |
| 未簽章的資產被掛上公開 Release 而沒有提示 | `.github/workflows/release.yml` 的 Create GitHub Release 那一步 |
| `release.yml` 的版本 regex 放行 Store 不收的版本號 | 同一支檔案的 Resolve version 那一步 |
| `CommandIds` 的七個字串沒有任何自動化把關 | `tests/Inkling.Tests/CommandIdTests.cs` |

這張表留著是因為它是「一條修掉就從這裡刪掉」之後唯一的去向索引;
**它不是待辦清單**,上面每一條都已經修完了。

---

## 沒有列進來的東西

- **效能**:2026-08-22 在 210 則的資料夾上實測,清單載入 <5 秒、打字過濾即時,
  沒有可感的延遲。有幾條「理論上會慢」的路(每個按鍵重讀剪貼簿、每次存/刪丟掉整份快取、
  每個按鍵重建 200 列的選單與 Details、watcher 與資料夾掃描搶同一把鎖)**沒有量測數據
  支持或否定**,所以不列為缺陷。真的要動之前先量。
- **`*.md.tmp` 殘骸**:`AtomicFile` 在 `File.Move` 失敗時會嘗試刪掉暫存檔,刪不掉就留著。
  掃描只看 `*.md`,所以殘骸不會被列出也不會被「刪除全部」清掉。已經是刻意的取捨,
  手動驗證清單也有對應項,不算缺陷。
- **`Input.Toggle` 的 UIA 名字是別的元素的字**(截到過「瀏覽…」與那塊警告文字)。
  Adaptive Cards 的 WinUI 渲染器沒有替它設 automation name,UIA 就往前抓了一個。
  這是**渲染器的行為,不是我們設得到的屬性**,而且改動卡片內容只會換成另一個錯的名字。
  畫面上那個核取方塊旁邊的字是對的;只有讀屏軟體會讀錯。留在這裡當紀錄,
  真的要修得等上游或改用別的控件。
