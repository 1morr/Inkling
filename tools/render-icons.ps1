<#
.SYNOPSIS
    把 assets/icon 底下的 SVG 渲染成 MSIX 套件要的那幾張 PNG。

.DESCRIPTION
    圖示的原始檔是 SVG,不是 PNG。src\Notelet\Assets 底下那幾個檔全部由這支腳本產生,
    **不要手改**,改了下次執行就被蓋掉;要調圖示請改 assets\icon\*.svg 再跑一次。

    為什麼用瀏覽器當渲染器:這台機器沒有 ImageMagick / Inkscape / rsvg,而 .NET 本身
    不會解 SVG。Chromium 是唯一現成、而且結果可預期的向量渲染器 —— 它是以目標尺寸直接
    向量渲染(不是先畫大張再縮),所以 24px 的邊緣是乾淨的。

    兩份原始檔的分工(見 SVG 裡的註解):
      notelet-tile.svg        精細版,150x150 以上
      notelet-tile-small.svg  小尺寸版,88px 以下(工作列、CmdPal 清單)
      notelet-wide.svg        寬幅版,寬磚與啟動畫面

.EXAMPLE
    pwsh -NoProfile -File tools\render-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$svgDir = Join-Path $repoRoot 'assets\icon'
$outDir = Join-Path $repoRoot 'src\Notelet\Assets'

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
$targets = @(
    @{ Name = 'Square44x44Logo.scale-200.png';                      Svg = 'notelet-tile-small.svg'; W = 88;   H = 88 }
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Svg = 'notelet-tile-small.svg'; W = 24;   H = 24 }
    @{ Name = 'StoreLogo.png';                                      Svg = 'notelet-tile-small.svg'; W = 50;   H = 50 }
    @{ Name = 'LockScreenLogo.scale-200.png';                       Svg = 'notelet-tile-small.svg'; W = 48;   H = 48 }
    @{ Name = 'Square150x150Logo.scale-200.png';                    Svg = 'notelet-tile.svg';       W = 300;  H = 300 }
    @{ Name = 'Wide310x150Logo.scale-200.png';                      Svg = 'notelet-wide.svg';       W = 620;  H = 300 }
    @{ Name = 'SplashScreen.scale-200.png';                         Svg = 'notelet-wide.svg';       W = 1240; H = 600 }
)

$work = Join-Path ([System.IO.Path]::GetTempPath()) "notelet-icons-$PID"
New-Item -ItemType Directory -Force -Path $work | Out-Null

try {
    foreach ($target in $targets) {
        $svgPath = Join-Path $svgDir $target.Svg
        if (-not (Test-Path $svgPath)) { throw "找不到原始檔:$svgPath" }

        # 包一層 HTML,而且把 svg 的 CSS 尺寸寫死成目標像素。
        # 不能用 100vw / 100vh —— headless 的版面視窗寬度不等於 --window-size,
        # SVG 會被 preserveAspectRatio 置中在一個更寬的框裡,截出來就是偏移又放大的半張圖。
        # 寫死尺寸之後,圖必定貼齊左上角,截圖範圍剛好蓋住它。
        $svg = Get-Content $svgPath -Raw
        $html = @"
<!doctype html><meta charset="utf-8">
<style>html,body{margin:0;padding:0;overflow:hidden;background:transparent}
svg{display:block;width:$($target.W)px;height:$($target.H)px}</style>
$svg
"@
        $htmlPath = Join-Path $work ($target.Name + '.html')
        Set-Content -Path $htmlPath -Value $html -Encoding UTF8

        $outPath = Join-Path $outDir $target.Name

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

        Write-Host ("  {0,-52} {1}x{2}" -f $target.Name, $width, $height) -ForegroundColor DarkGray
    }
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "`n圖示已更新:$outDir" -ForegroundColor Green
