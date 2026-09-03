#requires -Version 7.0
<#
.SYNOPSIS
    把 docs/images 的面板截圖處理成兩個通路要的圖:Store 的 1920x1080 合成圖，
    與 gallery 投稿用的裸面板。

.DESCRIPTION
    **兩個通路要的東西不一樣，因為只有 Store 有尺寸下限。**

    Store 的桌面截圖至少要 1366x768,而 `tools/cmdpal-ui.ps1` 的 `shot` 走的是
    PrintWindow，拍到的就是 CmdPal 面板本身 —— 固定 1200x720，永遠不夠。放大會糊
    (那是文字截圖)，所以改成把原尺寸的面板**原封不動**放到一張更大的畫布中央，
    補一層背景與陰影。

    **gallery 沒有下限，所以 `-Bare` 只裁黑邊、不做畫布。** 上游的 schema 與
    validator 只管格式、每張 ≤1 MB、最多 5 張，一個尺寸或比例的規定都沒有;而 52 個
    有截圖的既有擴展裡有 31 個用面板尺寸的裁切圖 —— 包括官方的
    `microsoft/sample-extension`(960x640)與 CmdPal 開發者自己的
    `zadjii/virtual-desktops`(766x555)。gallery 的卡片本來就只有面板那麼大，
    鋪一張桌面背景等於把面板縮到更小。

    也就是說兩種輸出裡的每一個文字像素都跟 docs/images 那張一模一樣，
    差別只在外面有沒有多一圈留白。

    **來源刻意沿用 docs/images。** 那三張是在 Windows 顯示語言切成英文時拍的
    (Store listing 是英文)，而重拍要再登出登入一次;內容也已經是安排過的 demo 筆記，
    不是真的筆記。要換內容就重拍 docs/images 再跑這支，不要另外做一套。

    PrintWindow 會在面板外圍留下純黑邊(實測左右各 11px、下 11px，每張不一定一樣),
    所以先掃出「不是純黑」的邊界再裁 —— 不要寫死數字，換一次 Windows 版本就會漂。
    **兩種模式共用同一段偵測**，那是它只實作一次的理由。

    **背景是 Windows 自己的桌布，不是我們畫的漸層。** Command Palette 本來就是浮在
    桌面上的一塊面板，配桌布才是它真正的樣子;純灰漸層看起來像一張去背圖。
    預設讀 `%SystemRoot%\Web\Wallpaper\Windows\img0.jpg`(Windows 11 的預設桌布，
    每台機器都有)，所以**不必把桌布檔案放進 repo** —— 那是微軟的美術資源，
    MIT 的 repo 不該散佈它。檔案不在就自動退回漸層，腳本不會失敗。

    **輸出一律 PNG。** Store **只收 PNG**(2026-08-23 實測上傳 .jpg 被擋下來，訊息是
    「is not a valid .png file」)，而裸面板約 70-90 KB，離 gallery 的 1 MB/張 上限
    遠得很。這支腳本曾經有一條 JPEG 路，存在的唯一理由是「帶桌布的 PNG 一張要
    ~1000 KB，離上限只剩不到 3%」—— `-Bare` 之後那個問題不存在了，那條路已經移除。

.EXAMPLE
    pwsh -NoProfile -File tools\make-store-screenshots.ps1
    # -> assets/store/01-top-level-commands.png 等三張，1920x1080(Store listing)

.EXAMPLE
    pwsh -NoProfile -File tools\make-store-screenshots.ps1 -Bare
    # -> assets/gallery/01-top-level-commands.png 等三張，約 1178x709(gallery 投稿)

.EXAMPLE
    pwsh -NoProfile -File tools\make-store-screenshots.ps1 -Background 'D:\shots\my-desktop.png'
    # 換一張背景;任何比畫布大的圖都行，會等比放大到蓋滿再置中裁掉多的
#>
[CmdletBinding()]
param(
    # 只裁掉 PrintWindow 的黑邊，不做畫布、不鋪背景、不畫陰影 —— gallery 投稿用。
    # 見上面的說明:那裡沒有尺寸下限，鋪一張桌面背景只會讓面板在卡片裡變小。
    [switch] $Bare,

    # 輸出資料夾。預設跟著模式走:Store 的合成圖進 assets/store,gallery 的裸面板進
    # assets/gallery —— 後者跟 icon.png 放一起，投稿時整包複製過去就好。
    # 兩個都**不進 MSIX**(套件圖示在 src/Inkling/Assets)。
    [string] $OutputDirectory,

    # 背景圖。預設是 Windows 11 的預設桌布 —— 讀機器上的檔案，不進 repo(見上面的說明)。
    # 給空字串就強制走漸層。**-Bare 時整個忽略。**
    [string] $Background = (Join-Path $env:SystemRoot 'Web\Wallpaper\Windows\img0.jpg'),

    # 背景上壓一層黑的透明度(0-255)。桌布比漸層亮也比較花，壓一點淺色面板才跳得出來;
    # 0 = 不壓。**-Bare 時整個忽略。**
    [ValidateRange(0, 255)]
    [int] $Scrim = 34,

    # 畫布尺寸。**-Bare 時整個忽略** —— 那時輸出就是裁完的面板本身。
    [int] $Width = 1920,
    [int] $Height = 1080
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$imagesDir = Join-Path $repoRoot 'docs\images'

# 檔名前綴決定 Partner Center 上的順序(它按上傳順序，但檔名有序比較不會弄錯),
# 也決定 gallery 投稿時 screenshots/ 的字母序。順序是「有什麼命令 → 怎麼記 → 怎麼找」。
$sources = [ordered]@{
    '01-top-level-commands' = 'top-level-commands.png'
    '02-quick-capture'      = 'quick-capture.png'
    '03-note-list'          = 'note-list.png'
}

$cornerRadius = 10

function New-RoundedPath {
    param([int] $X, [int] $Y, [int] $W, [int] $H, [int] $Radius)

    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $d = $Radius * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $H - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    return $path
}

function Get-ContentBounds {
    param([System.Drawing.Bitmap] $Bitmap)

    # 純黑 = PrintWindow 的外框。用中線掃四個方向就夠 —— 面板本身沒有整條純黑的列或行。
    $isBlack = { param($c) $c.R -lt 12 -and $c.G -lt 12 -and $c.B -lt 12 }

    $midX = [int]($Bitmap.Width / 2)
    $midY = [int]($Bitmap.Height / 2)

    $left = 0
    while ($left -lt $midX -and (& $isBlack $Bitmap.GetPixel($left, $midY))) { $left++ }

    $right = $Bitmap.Width - 1
    while ($right -gt $midX -and (& $isBlack $Bitmap.GetPixel($right, $midY))) { $right-- }

    $top = 0
    while ($top -lt $midY -and (& $isBlack $Bitmap.GetPixel($midX, $top))) { $top++ }

    $bottom = $Bitmap.Height - 1
    while ($bottom -gt $midY -and (& $isBlack $Bitmap.GetPixel($midX, $bottom))) { $bottom-- }

    return [System.Drawing.Rectangle]::new($left, $top, $right - $left + 1, $bottom - $top + 1)
}

function New-Backdrop {
    <#
        畫布大小的背景。有桌布就用桌布(等比放大到蓋滿再置中裁)，沒有就退回漸層。

        **一定要 cover 而不是 stretch** —— 桌布是 16:10、畫布是 16:9，直接拉伸會把
        那朵花壓扁，而那是每個 Windows 使用者每天都看得到的圖，變形一眼就認得出來。
    #>
    param([int] $W, [int] $H, [string] $Path, [int] $ScrimAlpha)

    $canvas = [System.Drawing.Bitmap]::new($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'

        $full = [System.Drawing.Rectangle]::new(0, 0, $W, $H)
        $drawn = $false

        if ($Path -and (Test-Path -LiteralPath $Path)) {
            $wall = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Path).Path)
            try {
                $scale = [math]::Max($W / $wall.Width, $H / $wall.Height)
                $sw = [int][math]::Ceiling($wall.Width * $scale)
                $sh = [int][math]::Ceiling($wall.Height * $scale)
                $g.DrawImage($wall, [int](($W - $sw) / 2), [int](($H - $sh) / 2), $sw, $sh)
                $drawn = $true
            }
            finally {
                $wall.Dispose()
            }
        }

        if (-not $drawn) {
            $gradient = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $full,
                [System.Drawing.Color]::FromArgb(246, 247, 249),
                [System.Drawing.Color]::FromArgb(224, 228, 235),
                90.0)
            $g.FillRectangle($gradient, $full)
            $gradient.Dispose()
        }
        elseif ($ScrimAlpha -gt 0) {
            $scrim = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($ScrimAlpha, 8, 14, 26))
            $g.FillRectangle($scrim, $full)
            $scrim.Dispose()
        }
    }
    finally {
        $g.Dispose()
    }

    return @{ Bitmap = $canvas; UsedWallpaper = $drawn }
}

function New-Composite {
    <#
        把裁好的面板原尺寸貼到背景畫布中央，補圓角、陰影與亮邊。

        **只有 Store 那條路會走到這裡** —— gallery 沒有尺寸下限，-Bare 直接輸出面板。
    #>
    param(
        [System.Drawing.Bitmap] $Panel,
        [System.Drawing.Bitmap] $Backdrop,
        [int] $W,
        [int] $H)

    # 每張都從同一份背景複製一次 —— 陰影會畫進去，共用同一個 Bitmap 會愈疊愈黑。
    $canvas = [System.Drawing.Bitmap]::new($Backdrop)
    $g = [System.Drawing.Graphics]::FromImage($canvas)
    try {
        $g.SmoothingMode = 'AntiAlias'
        $g.InterpolationMode = 'HighQualityBicubic'
        $g.PixelOffsetMode = 'HighQuality'

        $x = [int](($W - $Panel.Width) / 2)
        $y = [int](($H - $Panel.Height) / 2)

        # 陰影:一層層放大的半透明圓角矩形。GDI+ 沒有模糊，這是最便宜的近似。
        #
        # **每一層都要很淡。** 它們是疊加的，所以「層數多 + 每層淡」才會平滑;
        # 反過來(層數少 + 每層濃)會在畫面上留下一圈圈看得見的同心環 ——
        # 深色桌布上尤其明顯，第一版就是那樣。
        $shadowSpread = 44
        for ($i = $shadowSpread; $i -ge 1; $i--) {
            $alpha = [int][math]::Max(1, 1 + ($shadowSpread - $i) * 0.22)
            $shadowPath = New-RoundedPath ($x - $i) ($y - $i + 12) ($Panel.Width + $i * 2) ($Panel.Height + $i * 2) ($cornerRadius + $i)
            $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, 6, 12, 24))
            $g.FillPath($shadowBrush, $shadowPath)
            $shadowBrush.Dispose()
            $shadowPath.Dispose()
        }

        $clip = New-RoundedPath $x $y $Panel.Width $Panel.Height $cornerRadius
        try {
            $g.SetClip($clip)
            # 原尺寸貼上，不縮放 —— 這是整支腳本的重點。
            $g.DrawImage($Panel, $x, $y, $Panel.Width, $Panel.Height)
            $g.ResetClip()

            # 面板本身是淺色的，在深一點的桌布上需要一圈亮邊界(CmdPal 真的有)。
            $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(90, 255, 255, 255), 1)
            $g.DrawPath($pen, $clip)
            $pen.Dispose()
        }
        finally {
            $clip.Dispose()
        }
    }
    finally {
        $g.Dispose()
    }

    return $canvas
}

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot ($Bare ? '..\assets\gallery' : '..\assets\store')
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputDir = Resolve-Path $OutputDirectory

$backdrop = $null
if ($Bare) {
    Write-Host "模式:裸面板 —— 只裁黑邊，不鋪背景"
}
else {
    $backdrop = New-Backdrop -W $Width -H $Height -Path $Background -ScrimAlpha $Scrim
    if ($backdrop.UsedWallpaper) {
        Write-Host "背景:$Background"
    }
    else {
        Write-Host "背景:找不到桌布，退回漸層"
    }
}

foreach ($name in $sources.Keys) {
    $sourcePath = Join-Path $imagesDir $sources[$name]
    if (-not (Test-Path $sourcePath)) {
        throw "找不到來源截圖:$sourcePath(先跑 docs/development.md〈重拍截圖與 GIF〉那一節)"
    }

    $source = [System.Drawing.Bitmap]::new($sourcePath)
    try {
        $crop = Get-ContentBounds $source
        if (-not $Bare -and ($crop.Width -gt $Width -or $crop.Height -gt $Height)) {
            throw "$($sources[$name]) 裁完是 $($crop.Width)x$($crop.Height)，放不進 ${Width}x${Height} 的畫布"
        }

        $panel = $source.Clone($crop, $source.PixelFormat)
        $canvas = if ($Bare) { $panel.Clone() }
                  else { New-Composite -Panel $panel -Backdrop $backdrop.Bitmap -W $Width -H $Height }

        $target = Join-Path $outputDir "$name.png"
        $canvas.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)

        $canvas.Dispose()
        $panel.Dispose()

        $kb = [math]::Round((Get-Item $target).Length / 1KB, 1)
        if ($Bare -and $kb -gt 1024) {
            Write-Warning "$(Split-Path $target -Leaf) 是 $kb KB —— 超過 CmdPal gallery 的 1 MB/張 上限"
        }
        $shape = if ($Bare) { '' } else { "，置中於 ${Width}x${Height}" }
        Write-Host ("  {0,-24} 裁掉黑邊 -> {1}x{2}{3}  ({4} KB)" -f `
            $sources[$name], $crop.Width, $crop.Height, $shape, $kb)
    }
    finally {
        $source.Dispose()
    }
}

if ($backdrop) {
    $backdrop.Bitmap.Dispose()
}

Write-Host ""
Write-Host "輸出:$outputDir"
if ($Bare) {
    Write-Host "gallery 的 screenshots/ 每張上限 1 MB、最多 5 張、不收 GIF;沒有尺寸下限。"
}
else {
    Write-Host "Store 桌面截圖下限是 1366x768,而且只收 PNG。"
}
