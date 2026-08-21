# 開發與部署

從原始碼建置、部署到本機、以及出事時怎麼查。使用者文檔在 [README](../README.md),
「為什麼做成這樣」的完整考證在 [design-notes.md](design-notes.md)。

## 需求

| | |
|---|---|
| Windows | 10.0.19041 以上 |
| Command Palette | 0.11 以上(獨立 MSIX 套件 `Microsoft.CommandPalette`) |
| .NET SDK | 10.0 以上 |
| Developer Mode | 必須開啟。設定 → 系統 → 開發人員專用 |

不需要 Visual Studio,整套流程走 dotnet CLI。

<a id="build"></a>

## 建置與本機部署

```powershell
git clone https://github.com/1morr/Inkling.git Inkling
cd Inkling
.\tools\deploy.ps1 -Configuration Release -Reload
```

`deploy.ps1` 依序做:停掉還活著的擴展進程 → build/publish → 以 loose file 註冊套件 →
查 Windows 的 AppExtension 目錄確認 CmdPal 真的看得到它(自動的,不必靠肉眼開 CmdPal)→
送出 `x-cmdpal://reload`。

`-Reload` 需要先打開 CmdPal 設定 → 一般 → For developers → Enable external reload;
沒開的話腳本會講明白,那次要自己在 Command Palette 執行 **Reload**(選副標題是
`Reload Command Palette extensions` 的那一個)。**沒有 Reload,CmdPal 會繼續用舊的擴展實例**,
看起來就像改動沒生效。

Reload 之後還要對一次進程時間 —— 踩過一次:部署說「已送出 reload」、檔案也真的換了,
但舊的 `Inkling.exe` 還活著,畫面上看到的是上一版的行為。

```powershell
Get-Process Inkling | Select-Object StartTime                       # 要晚於下面這個
(Get-Item src\Inkling\bin\stage-Release\Inkling.dll).LastWriteTime
```

不是的話 `Stop-Process -Name Inkling`,再不行連 `Microsoft.CmdPal.UI` 一起停掉重來
(PowerToys 本身不用重開)。

### 只建擴展、只跑測試

```powershell
dotnet build src\Inkling\Inkling.csproj -p:Platform=x64   # 擴展(進程活著時會鎖輸出)
dotnet test                                               # Core 層的全部行為
dotnet test --filter "FullyQualifiedName~QuickCapture"    # 單一測試類別/方法
```

**方案層級建不了擴展。** `dotnet build Inkling.slnx` 走 AnyCPU,會撞 MSIX 打包目標的
「Packaged .NET applications with an app host exe cannot be ProcessorArchitecture neutral」——
打包專案不吃 AnyCPU,而 `Inkling.slnx` 沒有 x64 那個組態,帶 `-p:Platform=x64` 也救不了。
要建擴展就指定專案。`dotnet test` 不受影響,它只建測試專案與 Core。

### Debug 與 Release 的差別

trimming 只在 `dotnet publish` 時生效,而 `Add-AppxPackage -Register` 註冊的是 build 佈局
—— 兩者不是同一份輸出,所以腳本對兩種組態走不同的路:

| | 做法 | 大小 | 用途 |
|---|---|---|---|
| `Debug` | 直接註冊 build 佈局 | ~106 MB | 開發。`Debug.WriteLine` 有作用,建置快 |
| `Release` | 先 publish(trimming 生效),再併入 build 產生的 `AppxManifest.xml` | ~30 MB | 日常使用 |

只跑 `dotnet publish -c Release` 而不做後面那步,註冊到的仍然是未 trim 的 build 佈局,
等於完全沒驗到 trimming 有沒有把東西砍壞。

**loose file 註冊會綁住路徑**:`Add-AppxPackage -Register` 不會複製檔案,Windows 直接引用
`src\Inkling\bin\` 底下那個佈局,所以**不要在部署後刪掉 `bin\`**(`git clean -xfd` 也會刪),
否則擴展會壞掉;真的刪了就重跑一次 `deploy.ps1`。

**移除**:`Get-AppxPackage -Name Inkling | Remove-AppxPackage`。

### 改了圖示

`assets/icon/*.svg` 才是原始檔,`src/Inkling/Assets/*.png` 由腳本產生,不要手改 PNG
(這套圖示怎麼挑的,見[設計考證〈圖示〉](design-notes.md#icons)):

```powershell
pwsh -NoProfile -File tools\render-icons.ps1
```

那些 PNG 必須帶 `CopyToOutputDirectory`(見 `Inkling.csproj` 的註解)—— 少了它套件照樣註冊
得起來,只是所有圖示變成 Windows 的預設灰方塊。

同一支腳本還產兩張不進套件的圖:`assets/gallery/icon.png`(gallery 投稿用,
**兩份 README 頂部引用的就是它**,所以改 SVG 重跑一次 README 就跟著換)與
`assets/social-preview.png`(GitHub repo 的 social preview;GitHub 沒有上傳 API,
要自己到 repo Settings → General → Social preview 貼上去)。

### 在真機上驗畫面

CmdPal 沒有提供 UI 自動化介面,但清單內容、快速鍵、placeholder、有沒有跳 toast 這些可以
用 Windows 的 UI Automation 驗掉。**一整串動作必須在同一次呼叫裡跑完** —— CmdPal 一失焦
就自我隱藏:

```powershell
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:# |wait:1400|tree:6"
pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "notes"     # 目前設定的筆記資料夾內容
```

顏色、圖示長相、游標位置只能靠眼睛,那些收在 [manual-test-checklist.md](manual-test-checklist.md)。

### 重拍截圖與 GIF

兩份 README 共用 `docs/images/` 裡的 PNG 與 GIF,全部是真機上用 `cmdpal-ui.ps1` 的 `shot`
動作拍的(`PrintWindow`,1200×720)。改了圖示、命令標題或版面就要重拍,兩份 README 一起換。

1. **先把筆記資料夾指到 demo 資料夾**,別把真的筆記放進公開 repo。備份
   `%LOCALAPPDATA%\Packages\<PFN>\LocalState\settings.json`(`<PFN>` 用
   `(Get-AppxPackage Inkling).PackageFamilyName` 查),把 `Inkling.NotesDirectory` 改成
   `%TEMP%\inkling-demo`,裡面放幾則英文的 demo 筆記(帶 front matter,標題像真的),
   `Stop-Process -Name Inkling` 再 `Start-Process 'x-cmdpal://reload'`,
   用 `-Steps "notes"` 確認清單讀到的是 demo。
2. **拍**,一整串在同一次呼叫裡。三張 PNG 分別是主搜尋框打 `Inkling` 的結果、快速記下頁打完字、
   清單頁;GIF 是快速記下那條路連拍(`$g` 是放格子的資料夾):

   ```powershell
   pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|wait:900|type:! |wait:1500|shot:$g\f02.png|type:coffee |wait:450|shot:$g\f03.png|type:machine |wait:450|shot:$g\f04.png|type:idea|wait:1200|shot:$g\f05.png|key:Enter|wait:2000|shot:$g\f06.png|esc|wait:700|esc|wait:700|esc"
   ```

   **不要拿 `show` 之後那一張當第一格**:主頁會帶著上一次的查詢字與使用者自己的應用程式 /
   最近項目,那是使用者的東西,從快速記下頁那一格開始。剪貼簿有多行文字時會多一列
   「內文取自剪貼簿」,那是真的功能,留著沒關係。
3. **合成**(ffmpeg,`winget install ffmpeg`)。concat demuxer 給每一格自己的停留秒數,
   palettegen / paletteuse 兩段式壓成 256 色,縮到 960 寬(GitHub 的 README 欄位本來就不到這個寬):

   ```text
   # list.txt —— 最後一格要再列一次,否則 concat 會吃掉它的 duration
   file 'f02.png'
   duration 1.3
   file 'f03.png'
   duration 0.45
   file 'f04.png'
   duration 0.45
   file 'f05.png'
   duration 1.6
   file 'f06.png'
   duration 3.0
   file 'f06.png'
   ```

   ```powershell
   ffmpeg -y -f concat -safe 0 -i list.txt -vf "fps=10,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=256:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle" -loop 0 docs\images\quick-capture.gif
   ```

   目前那張是 5 格、9.8 秒、960×576、約 130 KB。
4. **換回去**:把備份蓋回 `settings.json`,再 `Stop-Process -Name Inkling` + reload,
   `-Steps "notes"` 確認回到真的資料夾;demo 資料夾刪掉。

`quick-capture.png`(靜態的那張)README 已經不引用,留著是給 gallery 投稿的 `screenshots/`
用(那邊不收 GIF),見 [gallery/README.md](gallery/README.md)。

<a id="structure"></a>

## 專案結構

```
src/
  Inkling.Core/      純 net10.0 類別庫,不引用任何 CmdPal 型別 → 100% 可單元測試。
                     front matter 讀寫、id/檔名、搜索排序、標題/內文切分、
                     摘要與推導標題(NoteBody)、隨手草稿的檔案讀寫(ScratchpadStore)
                     都在這一層。原子寫檔(AtomicFile)與換行正規化(Newlines)
                     是筆記與草稿共用的
  Inkling/           CmdPal 擴展(MSIX COM server),只負責把 Core 的結果
                     翻譯成 IListItem / IContent
    CommandIds        頂層命令的固定 Id(還叫 Notelet.*,改名前的名字,故意留著;
                      改了會清掉使用者的 alias / 快速鍵 / 釘選)
    Properties/       介面字串:英文(中性)+ 繁中 + 簡中,語言跟著 Windows 走
    Shortcuts         鍵位集中在這裡(挑鍵的規則寫在檔案註解裡)
    Commands/NoteCommands  編輯 / 複製 / 開啟那幾項選單的唯一組裝處(三個頁面共用)
    RecycleBinFileDeleter / FolderPicker  資源回收筒、設定頁的「瀏覽…」對話框
    Pages/            快速記下、記下後的預覽、清單、預覽、編輯、新增、隨手草稿、刪除、設定
                      (進 Adaptive Cards 的字串一律經 CardText 做 JSON 跳脫;
                      項目快取的形狀三個清單頁共用,見 VersionedItemsCache)
assets/icon/         圖示的原始檔(SVG);src/Inkling/Assets 的 PNG 全部由
                     tools/render-icons.ps1 產生,不要手改。五個頂層命令各一個
                     inkling-cmd-*.svg,每個輸出淺 / 深兩張 PNG —— 加新的頂層命令
                     時 SVG 與 render-icons.ps1 的 $targets 兩邊都要補。
                     assets/gallery/icon.png(gallery 投稿 + README 頂部)與
                     assets/social-preview.png(GitHub social preview)也是它產的
tests/               Inkling.Core.Tests(xUnit)
tools/               deploy.ps1(build→註冊→驗證)、VerifyRegistration、
                     ApiDump(印 SDK 型別的實際簽章)、cmdpal-ui.ps1(真機驅動
                     CmdPal 畫面)、render-icons.ps1(SVG→PNG)、
                     stage-layout.ps1(組出可註冊 / 可打包的套件佈局;
                     deploy.ps1 與 release.yml 共用同一份)
docs/                design-notes.md(設計考證)、development.md(這一份)、
                     manual-test-checklist.md、release-checklist.md、
                     images/(兩份 README 共用的截圖與 GIF,真機拍的,重拍流程見上方)、
                     gallery/(CmdPal Extension Gallery 的投稿草稿)
.github/             workflows/ci.yml 與 workflows/release.yml(見下一節)、
                     ISSUE_TEMPLATE/
.claude/skills/      CmdPal 官方模板的 API 速查與工作流程,都加了「本專案的例外」;
                     另有自己寫的 verify-cmdpal-ui,見 .claude/skills/README.md
```

分層的界線是「能不能自動化測試」:`Inkling.Core` 不知道 Command Palette 的存在,
容易寫錯的邏輯都在那一層,因此都有單元測試涵蓋。新增行為時先問「這段邏輯能不能放進 Core」。

<a id="workflows"></a>

### CI 覆蓋到哪裡

兩個 workflow,都在 `windows-latest` 上跑:

| | 何時跑 | 做什麼 |
|---|---|---|
| `ci.yml` | push 到 master / main、每個 pull request | Debug 建擴展 → 建兩支工具 → `dotnet test` → 兩個 RID 各 publish 一次(trimmed)→ **組套件佈局 + `makeappx pack`** → 檢查 manifest 宣告的語言 |
| `release.yml` | 推 `v*` tag,或手動 `workflow_dispatch` | 解析版本 → 注入 manifest → `dotnet test` → publish → 組佈局 → `makeappx pack` →(有憑證才)簽 → 組 bundle(帶 `/bv`)→(有憑證才)簽 → 建 GitHub Release |

兩件事值得記住:

- **`ci.yml` 的打包那兩步是後來才補的,而它們補的是一個真的付過代價的缺口。**
  `makeappx` 以前只存在於 `release.yml`,而那條路在推 tag 之前一次都不會執行 ——
  於是「release job 沒有 checkout」「`makeappx bundle` 少了 `/bv`」「bundle 沒簽章」
  三個問題全部安安靜靜地躺在那裡,要到第一次發版當天才會一起爆,而那時 tag 已經推上去了。
  CI 那一步刻意**不加 `/nv`**:要的就是 makeappx 完整的套件驗證。
- **`release.yml` 的 `workflow_dispatch` 是拿來試跑的**,填一個版本號(預設 `0.0.0`),
  它會把 stage → pack → bundle 整條路跑完,只跳過最後建 Release 那一步。
  改過打包相關的東西就跑一次,不要拿真的 tag 當測試。

佈局那段兩邊共用 `tools/stage-layout.ps1`:**publish 輸出裡沒有 `AppxManifest.xml`**
(那是 build 佈局才會產生的),而 trimming 只在 publish 生效 —— 兩邊各有一半,得併起來。

`Package.appxmanifest` 的 `<Resources>` **刻意不用官方模板的 `x-generate`**:
那會讓 MRT 拿 PRI 裡實際存在的語言限定詞去展開,而我們的介面字串走 .NET 的附屬組件、
不是 PRI,於是它退回「預設語言」那一個 —— 而預設語言取的是**建置機器的顯示語言**。
同一份原始碼在作者機器上產出 `ZH-TW`、在 Actions 上產 `EN-US`,而 Store 與 gallery
讀的就是這一段。現在三種語言明確列出來,`Inkling.csproj` 的 `<DefaultLanguage>`
同時釘住 PRI 那一邊,`ci.yml` 最後一步檢查產出物真的是那三種。

<a id="settings-file"></a>

## 設定檔:位置、格式、更新之後還在嗎

```
%LOCALAPPDATA%\Packages\<PFN>\LocalState\settings.json
```

`<PFN>` 是套件家族名,用 `(Get-AppxPackage Inkling).PackageFamilyName` 查得到 ——
**不要把它寫死進文檔**,換套件身分時那些字串會靜靜失效。路徑裡那串雜湊是從
`Package.appxmanifest` 的 `Identity` 算出來的(MSIX 路徑重導向)。CmdPal 自己那份設定
(啟用與否、alias、快速鍵、釘選)存在 CmdPal 的套件底下,擴展碰不到。

一層扁平的 JSON,鍵是 `Inkling.<屬性名>`,值**一律是字串**(布林也是 `"true"` / `"false"`)。

**手改的話要小心格式。** toolkit 的載入是一個沒有逐項 `try/catch` 的迴圈(`Settings.Update`):
某一項解析失敗,例外一路拋到 `LoadSettings` 的 `catch`,**排在它後面的設定項連碰都碰不到**,
靜靜退回預設值,沒有任何錯誤訊息。最容易踩的是「記下後先看一眼」:`ToggleSetting` 存的是
**字串** `"true"` / `"false"`(`Input.Toggle` 回傳的就是字串),寫成 JSON 的 `true` 就會炸。
所以 `Settings.Add` 的順序是照「**壞掉的代價小的排後面**」排的,而不是隨便排:
兩個字串項在前,「記下後先看一眼」在後(它是設定頁上看得到、使用者可能手改的那一個),
最後是 `Inkling.ShowSource` —— 那只是 `Ctrl+U` 的檢視狀態,再按一次就回來,
是這幾項裡唯一丟了也不痛的。

**更新擴展不會動到它。** `Identity` 的 `Name` 與 `Publisher` 不變,套件家族名就不變,
`LocalState` 就是同一個資料夾。`tools/deploy.ps1` 切換佈局時的 `Remove-AppxPackage` 帶了
`-PreserveApplicationData` 就是為了保住它 —— 拿掉那個參數,每次部署設定都被清空。

**沒有 schema 版本,也沒有遷移程式,而且不需要**(以下都對 toolkit 0.11.260520004 實測過):

- **加設定項**:舊檔案裡沒有那個鍵,`Update` 就不去碰它,值留在程式裡宣告的預設值。
- **移設定項**:`SaveSettings` 是**合併**進舊檔案,不認得的鍵原樣留著(fallback 時代的
  `QuickCaptureEnabled` / `QuickCapturePrefix` 現在多半還在你的檔案裡)。想清掉就手動刪那幾行。
- **改預設值**(例如把「記下後先看一眼」從關改成開):只對**檔案裡還沒有那個鍵**的人生效。
  `SettingsManager.Apply` 一次把全部設定項寫回去,按過一次儲存,鍵就都在檔案裡了。

**真的會弄丟設定的只有兩件事**:改 `Identity` 的 `Name` 或 `Publisher`(換成 **subject 不同**
的簽名憑證就會,例如上架時換成 Partner Center 或 CA 指派的身分;套件家族名是從 Publisher
**字串**算的,同 subject 換發/更新憑證不影響),以及不帶 `-PreserveApplicationData` 的
`Remove-AppxPackage`。想重置回預設,把 `settings.json` 刪掉再 Reload 即可。

筆記本身完全不受影響:那是設定裡指到的一般資料夾,跟套件的生命週期無關。

<a id="troubleshooting"></a>

## 排錯

**改了程式但 CmdPal 沒反應** — 要跑 Reload,而且要選副標題是
`Reload Command Palette extensions` 的那一個。重新部署後有時會冒出兩個 Inkling
(CmdPal 在套件安裝事件上沒去重)。再 Reload 一次有時收得回去,清不掉就把
`Microsoft.CmdPal.UI` 停掉讓它重啟,成因見[設計考證](design-notes.md#dev-notes)。

**搜尋結果裡多出一列 Inkling,按 Enter 沒反應** — 那不是重複的 provider,是 Windows
的應用程式清單項被 CmdPal 內建的應用程式搜索列了進來(副標會是 manifest 的英文
`Description` 而不是我們的資源字串,而且只多一列不是多五列)。分辨方式:

```powershell
Get-StartApps | Where-Object { $_.Name -like '*Inkling*' }
```

有輸出就是這一種 —— manifest 的 `AppListEntry="none"` 掉了,見
[設計考證〈套件刻意不出現在開始功能表〉](design-notes.md#app-list-entry)。
沒有輸出就是上一條那個重複的 provider。

**改了 `Package.appxmanifest` 之後部署失敗說 `0x80073CFB`** — 位置與版本都沒變、
但 manifest 內容變了就會這樣。**不要照它建議的去遞增版本號**;`deploy.ps1` 會自己接住
這個錯誤,先 `Remove-AppxPackage -PreserveApplicationData` 再重新註冊。

**build 失敗說檔案被佔用** — CmdPal 把擴展的 COM server 留著沒關。`deploy.ps1` 會自動
先停掉它;直接跑 `dotnet build` 的話要自己 `Get-Process Inkling | Stop-Process -Force`。

**部署說成功,跑的卻還是舊版本** — 同一個 identity + version 已經註冊時,
`Add-AppxPackage -Register` 會**靜默地什麼都不做**,舊的 `InstallLocation` 原封不動
(在 Debug 與 Release 之間切換時特別容易中招)。`deploy.ps1` 已經處理:位置不同就先
`Remove-AppxPackage -PreserveApplicationData` 再註冊,事後還會確認 `InstallLocation`
真的變了。想確認目前跑的是哪一份:`(Get-AppxPackage -Name Inkling).InstallLocation`。

**設定頁按 Save 什麼都沒發生** — 那個頁面是綁在**某一個擴展實例**上的。中間只要發生過
Reload 或重新部署,舊的擴展進程就被換掉了,設定頁手上的物件已經死了,按下去靜靜地什麼也
不會做:不寫檔、不重建、不報錯。**把設定頁關掉重開**(退回 Extensions 清單再點進來)就好。
查證方式是打開 DiagnosticLog 再按一次 Save:

- 什麼都沒印 → 呼叫根本沒到擴展這邊,就是上面這件事
- 印出 `Apply: 資料夾='…' 分隔符='…' …` 跟 `SaveSettings(Apply): 已寫入 …` → 設定確實存下去了

擴展這一側的存檔失敗也會記進同一個檔 —— toolkit 的 `JsonSettingsManager.SaveSettings`
自己把例外吞掉、只往 CmdPal 的 log 丟一行字,所以 `SettingsManager.Save` 另外記了一筆完整的例外。

**介面變成英文(或不是預期的語言)** — 介面語言跟著 Windows 的顯示語言走,沒有設定項,
見[設計考證〈介面語言跟著 Windows 走〉](design-notes.md#ui-language)。
打開 DiagnosticLog 再 Reload,擴展一啟動就會印 `UI 語言:zh-TW 抽樣='設定'` 這樣一行:

- 語言不對 → 是 Windows 那邊的顯示語言(不是「地區格式」那個設定),或是剛改完還沒重新登入
- 語言對、抽樣卻是英文(`Settings`)→ 附屬組件沒進套件。查
  `src\Inkling\bin\stage-Release\zh-Hant\Inkling.resources.dll` 在不在

**擴展沒出現在 CmdPal 裡** — 跑 `dotnet run --project tools\VerifyRegistration`,它會列出
Windows 認得的所有 CmdPal 擴展:不在裡面是註冊沒成功;在裡面卻不出現是 CmdPal 端,先試 Reload。

**`APPX1707` 警告** — 官方擴展模板也會出現,無害。

<a id="diagnostic-log"></a>

### 讓擴展自己說話(DiagnosticLog)

擴展跑在獨立的 COM server 進程裡,沒有主控台;`Debug.WriteLine` 在 Release 整個編掉,
而日常安裝的正是 Release。要確認某段程式有沒有跑到,得看 `DiagnosticLog` 寫出來的檔。

預設關閉。開啟方式是在設定資料夾裡建一個空檔,然後 Reload:

```powershell
$ls = "$env:LOCALAPPDATA\Packages\$((Get-AppxPackage Inkling).PackageFamilyName)\LocalState"
New-Item -ItemType File "$ls\diagnostic.on"
Get-Content "$ls\diagnostic.log" -Encoding utf8 -Wait   # 邊操作邊看
```

沒有 `diagnostic.on` 時每次呼叫只是一個布林判斷。用完把 `.on` 檔刪掉即可。

(`dotnet run --project tools\ApiDump -- --paths` 印的是**未封裝**身分下的路徑,
跟上面那個不是同一個,別搞混。)

## 查 SDK 與 CmdPal 的實際行為

Microsoft Learn 上的 API 參考有些頁面對不上 0.11 的實際簽章。要確認就直接問組件:

```powershell
dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult
```

從 PowerToys 的 `main` 分支讀原始碼得到的結論**一律要跟使用者裝的版本對照過**再寫進文檔 ——
已經有好幾條「`main` 有、安裝版沒有」的落差被當成事實寫進過 README。對照的手法(byte-scan,
UTF-8 與 UTF-16 都要掃)與已知落差清單見 [CLAUDE.md](../CLAUDE.md) 的〈查證 CmdPal 的行為〉。

## 延伸閱讀

| | |
|---|---|
| [CLAUDE.md](../CLAUDE.md) | 架構、跟 CmdPal 打交道的硬規則、慣例。**動手改程式前先讀這份** |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | 對外的貢獻入口(英文),只指路,規則不在那裡 |
| [README.md](../README.md) / [README.zh-Hant.md](../README.zh-Hant.md) | 使用者文檔的兩個語言版本,改一份就改另一份 |
| [design-notes.md](design-notes.md) | 「為什麼是這樣」的完整考證 |
| [manual-test-checklist.md](manual-test-checklist.md) | 只能靠眼睛驗的項目 |
| [release-checklist.md](release-checklist.md) | 首次公開發佈、套件身分與簽章 |
| [gallery/](gallery/) | 投稿 CmdPal Extension Gallery 的素材草稿 |
