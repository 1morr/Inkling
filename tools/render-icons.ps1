<#
.SYNOPSIS
    把 assets/icon 底下的 SVG 渲染成 MSIX 套件要的那幾張 PNG。

.DESCRIPTION
    圖示的原始檔是 SVG,不是 PNG。src\Inkling\Assets 底下那幾個檔全部由這支腳本產生,
    **不要手改**,改了下次執行就被蓋掉;要調圖示請改 assets\icon\*.svg 再跑一次。

    為什麼用瀏覽器當渲染器:這台機器沒有 ImageMagick / Inkscape / rsvg,而 .NET 本身
    不會解 SVG。Chromium 是唯一現成、而且結果可預期的向量渲染器 —— 它是以目標尺寸直接
    向量渲染(不是先畫大張再縮),所以 24px 的邊緣是乾淨的。

    原始檔的分工(見各 SVG 裡的註解):
      inkling-tile.svg        套件磚,精細版,150x150 以上
      inkling-tile-small.svg  套件磚,小尺寸版,88px 以下(工作列、CmdPal 清單)
      inkling-wide.svg        套件磚,寬幅版,寬磚與啟動畫面
      inkling-cmd-*.svg       五個頂層命令的單色圖示(清單 / 快速記下 / 新增 / 隨手草稿 / 刪除)

    命令圖示為什麼一個要產兩張:字形圖示是以文字繪製的,前景色自動跟主題走,PNG 不會。
    所以每個命令各出淺色主題(深色前景)與深色主題(白色前景)兩張,由
    IconHelpers.FromRelativePaths(light, dark) 挑。SVG 裡寫的是 currentColor,
    下面的 Fg 欄位就是餵給它的顏色。

    除了套件資產,另外產一張 CmdPal gallery 投稿用的圖示到 assets\gallery\icon.png
    (256x256 PNG、≤100 KB —— microsoft/CmdPal-Extensions 的規則,SVG 不收)。

.EXAMPLE
    pwsh -NoProfile -File tools\render-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$svgDir = Join-Path $repoRoot 'assets\icon'
$outDir = Join-Path $repoRoot 'src\Inkling\Assets'
$galleryDir = Join-Path $repoRoot 'assets\gallery'

# Chrome 與 Edge 都是 Chromium,哪個在就用哪個。
$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $browser) {
    throw "找不到 Chrome 或 Edge,無法渲染 SVG。"
}

Write-Host "渲染器: $browser" -ForegroundColor DarkGray

# 檔名 → (原始檔, 寬, 高)。尺寸沿用 Visual Studio 模板產生的那一組,
# 換掉檔案內容但不動檔名 —— Package.appxmanifest 與 csproj 都是照名字引用的。
# Dir 不設就是 $outDir(套件資產);MaxKB 是 gallery 投稿的 100 KB 上限。
$targets = @(
    # Square44x44Logo 的兩條候選階梯。Windows 用 MRT 從檔名的限定詞挑,
    # **兩條是分開挑的**:沒帶 altform 的走 scale-*,unplated 走 targetsize-*。
    # 模板只給了 scale-200 與 targetsize-24_altform-unplated 兩張,而要 unplated 的
    # 那些地方(應用程式清單、工作列按鈕)只有 24 可挑 —— 這台 150% DPI 要 30px,
    # 於是把 24 放大,看起來就是糊的;同一畫面上 48px 來源的命令圖示卻很銳利。
    # 兩條階梯都補齊,讓它每次都挑得到不必縮放或只需縮小的那一張。
    @{ Name = 'Square44x44Logo.scale-100.png';                       Svg = 'inkling-tile-small.svg'; W = 44;  H = 44 }
    @{ Name = 'Square44x44Logo.scale-125.png';                       Svg = 'inkling-tile-small.svg'; W = 55;  H = 55 }
    @{ Name = 'Square44x44Logo.scale-150.png';                       Svg = 'inkling-tile-small.svg'; W = 66;  H = 66 }
    @{ Name = 'Square44x44Logo.scale-200.png';                       Svg = 'inkling-tile-small.svg'; W = 88;  H = 88 }
    @{ Name = 'Square44x44Logo.scale-400.png';                       Svg = 'inkling-tile.svg';       W = 176; H = 176 }
    @{ Name = 'Square44x44Logo.targetsize-16_altform-unplated.png';  Svg = 'inkling-tile-small.svg'; W = 16;  H = 16 }
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png';  Svg = 'inkling-tile-small.svg'; W = 24;  H = 24 }
    @{ Name = 'Square44x44Logo.targetsize-32_altform-unplated.png';  Svg = 'inkling-tile-small.svg'; W = 32;  H = 32 }
    @{ Name = 'Square44x44Logo.targetsize-48_altform-unplated.png';  Svg = 'inkling-tile-small.svg'; W = 48;  H = 48 }
    @{ Name = 'Square44x44Logo.targetsize-256_altform-unplated.png'; Svg = 'inkling-tile.svg';       W = 256; H = 256 }
    # 不另外出「plated」(不帶 altform)的 targetsize 變體:BackgroundColor 是 transparent,
    # Windows 不會畫底板,兩者長得一樣,而沒帶 altform 的請求本來就落在上面的 scale-* 階梯上。
    @{ Name = 'StoreLogo.png';                                      Svg = 'inkling-tile-small.svg'; W = 50;   H = 50 }
    @{ Name = 'LockScreenLogo.scale-200.png';                       Svg = 'inkling-tile-small.svg'; W = 48;   H = 48 }
    @{ Name = 'Square150x150Logo.scale-200.png';                    Svg = 'inkling-tile.svg';       W = 300;  H = 300 }
    @{ Name = 'Wide310x150Logo.scale-200.png';                      Svg = 'inkling-wide.svg';       W = 620;  H = 300 }
    @{ Name = 'SplashScreen.scale-200.png';                         Svg = 'inkling-wide.svg';       W = 1240; H = 600 }
    # gallery 投稿用:microsoft/CmdPal-Extensions 要 PNG/JPEG、≤100 KB、建議 256x256。
    @{ Name = 'icon.png';                                           Svg = 'inkling-tile.svg';       W = 256;  H = 256; Dir = $galleryDir; MaxKB = 100 }

    # 五個頂層命令的圖示,每個兩張(淺色主題用深色前景、深色主題用白色前景)。
    # 48x48 而不是 24x24:CmdPal 清單列上大約 20px,但這台是 144 DPI(150%),
    # 24px 的來源會被放大到 30px 而糊掉。48 往下縮乾淨,往上也還撐得住 200%。
    # 檔名裡不要出現句點分段(例如 CommandList.light.png)—— 那是 MRT 的限定詞語法,
    # 不想讓打包工具去猜。
    @{ Name = 'CommandListLight.png';    Svg = 'inkling-cmd-list.svg';    W = 48; H = 48; Fg = '#1A1A1A' }
    @{ Name = 'CommandListDark.png';     Svg = 'inkling-cmd-list.svg';    W = 48; H = 48; Fg = '#FFFFFF' }
    @{ Name = 'CommandCaptureLight.png'; Svg = 'inkling-cmd-capture.svg'; W = 48; H = 48; Fg = '#1A1A1A' }
    @{ Name = 'CommandCaptureDark.png';  Svg = 'inkling-cmd-capture.svg'; W = 48; H = 48; Fg = '#FFFFFF' }
    @{ Name = 'CommandNewLight.png';     Svg = 'inkling-cmd-new.svg';     W = 48; H = 48; Fg = '#1A1A1A' }
    @{ Name = 'CommandNewDark.png';      Svg = 'inkling-cmd-new.svg';     W = 48; H = 48; Fg = '#FFFFFF' }
    @{ Name = 'CommandDeleteLight.png';  Svg = 'inkling-cmd-delete.svg';  W = 48; H = 48; Fg = '#1A1A1A' }
    @{ Name = 'CommandDeleteDark.png';   Svg = 'inkling-cmd-delete.svg';  W = 48; H = 48; Fg = '#FFFFFF' }
    @{ Name = 'CommandScratchpadLight.png'; Svg = 'inkling-cmd-scratchpad.svg'; W = 48; H = 48; Fg = '#1A1A1A' }
    @{ Name = 'CommandScratchpadDark.png';  Svg = 'inkling-cmd-scratchpad.svg'; W = 48; H = 48; Fg = '#FFFFFF' }
)

$work = Join-Path ([System.IO.Path]::GetTempPath()) "inkling-icons-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    foreach ($target in $targets) {
        $svgPath = Join-Path $svgDir $target.Svg
        if (-not (Test-Path $svgPath)) { throw "找不到原始檔:$svgPath" }

        # 包一層 HTML,而且把 svg 的 CSS 尺寸寫死成目標像素。
        # 不能用 100vw / 100vh —— headless 的版面視窗寬度不等於 --window-size,
        # SVG 會被 preserveAspectRatio 置中在一個更寬的框裡,截出來就是偏移又放大的半張圖。
        # 寫死尺寸之後,圖必定貼齊左上角,截圖範圍剛好蓋住它。
        # Fg 有設就覆蓋 SVG 自己的 color —— 單色的命令圖示靠 currentColor 上色,
        # 同一份原始檔要出淺 / 深兩張就差在這一行。套件磚沒有 Fg,這個規則不影響它們。
        $svg = Get-Content $svgPath -Raw
        # !important 是必要的:SVG 檔案自己帶 style="color:..."(讓它單獨開起來看得見),
        # 而行內樣式的優先權高過選擇器 —— 少了 !important 這一行完全沒作用,
        # 而且不會報錯,只會兩張 PNG 長得一模一樣。
        $fgRule = if ($target.Fg) { "svg{color:$($target.Fg) !important}" } else { '' }
        $html = @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;overflow:hidden;background:transparent}
svg{display:block;width:$($target.W)px;height:$($target.H)px}
$fgRule</style>
$svg
"@
        $htmlPath = Join-Path $work ($target.Name + '.html')
        Set-Content -Path $htmlPath -Value $html -Encoding UTF8

        $targetDir = if ($target.Dir) { $target.Dir } else { $outDir }
        if (-not (Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
        $outPath = Join-Path $targetDir $target.Name

        # 路徑用 [uri] 轉成 file:/// 形式,免得自己處理反斜線。
        # --default-background-color=00000000 讓沒被圖蓋到的地方保持透明。
        # --force-device-scale-factor=1 避免跟著系統 DPI 放大(這台是 144 DPI)。
        & $browser --headless --disable-gpu --hide-scrollbars `
            --force-device-scale-factor=1 `
            --default-background-color=00000000 `
            --screenshot="$outPath" `
            --window-size="$($target.W),$($target.H)" `
            ([uri]$htmlPath).AbsoluteUri 2>$null | Out-Null

        if (-not (Test-Path $outPath)) { throw "渲染失敗:$($target.Name)" }

        $bytes = [System.IO.File]::ReadAllBytes($outPath)
        $width = [BitConverter]::ToInt32($bytes[19..16], 0)
        $height = [BitConverter]::ToInt32($bytes[23..20], 0)

        if ($width -ne $target.W -or $height -ne $target.H) {
            throw "$($target.Name) 尺寸不對:實際 ${width}x${height},預期 $($target.W)x$($target.H)"
        }

        if ($target.MaxKB) {
            $kb = $bytes.Length / 1KB
            if ($kb -gt $target.MaxKB) {
                throw "$($target.Name) 太大:$([math]::Round($kb, 1)) KB,超過 gallery 上限 $($target.MaxKB) KB"
            }
            Write-Host ("  {0,-52} {1}x{2}  ({3:N1} KB)" -f $target.Name, $width, $height, $kb) -ForegroundColor DarkGray
        } else {
            Write-Host ("  {0,-52} {1}x{2}" -f $target.Name, $width, $height) -ForegroundColor DarkGray
        }
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n圖示已更新:$outDir(套件資產)+ $galleryDir(gallery 投稿用)" -ForegroundColor Green
