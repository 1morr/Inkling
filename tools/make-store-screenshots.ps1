#requires -Version 7.0
<#
.SYNOPSIS
    把 docs/images 的面板截圖合成成 Microsoft Store listing 用的 1920x1080 圖。

.DESCRIPTION
    **這支腳本存在的唯一理由是尺寸下限。** Store 的桌面截圖至少要 1366x768,
    而 `tools/cmdpal-ui.ps1` 的 `shot` 走的是 PrintWindow,拍到的就是 CmdPal 面板
    本身 —— 固定 1200x720,永遠不夠。放大會糊(那是文字截圖),所以改成把原尺寸的
    面板**原封不動**放到一張更大的畫布中央,補一層背景與陰影。

    也就是說輸出裡的每一個文字像素都跟 docs/images 那張一模一樣,只是外面多了留白。

    **來源刻意沿用 docs/images。** 那三張是在 Windows 顯示語言切成英文時拍的
    (Store listing 是英文),而重拍要再登出登入一次;內容也已經是安排過的 demo 筆記,
    不是真的筆記。要換內容就重拍 docs/images 再跑這支,不要另外做一套。

    PrintWindow 會在面板外圍留下純黑邊(實測左右各 11px、下 11px,每張不一定一樣),
    所以先掃出「不是純黑」的邊界再裁 —— 不要寫死數字,換一次 Windows 版本就會漂。

.EXAMPLE
    pwsh -NoProfile -File tools\make-store-screenshots.ps1
    # -> assets/store/01-top-level-commands.png 等三張,1920x1080
#>
[CmdletBinding()]
param(
    # 輸出資料夾。預設 assets/store —— 那裡不進 MSIX(套件圖示在 src/Inkling/Assets)。
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\assets\store'),

    [int] $Width = 1920,
    [int] $Height = 1080
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$imagesDir = Join-Path $repoRoot 'docs\images'

# 檔名前綴決定 Partner Center 上的順序(它按上傳順序,但檔名有序比較不會弄錯),
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

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outputDir = Resolve-Path $OutputDirectory

foreach ($name in $sources.Keys) {
    $sourcePath = Join-Path $imagesDir $sources[$name]
    if (-not (Test-Path $sourcePath)) {
        throw "找不到來源截圖:$sourcePath(先跑 docs/development.md〈重拍截圖與 GIF〉那一節)"
    }

    $source = [System.Drawing.Bitmap]::new($sourcePath)
    try {
        $crop = Get-ContentBounds $source
        if ($crop.Width -gt $Width -or $crop.Height -gt $Height) {
            throw "$($sources[$name]) 裁完是 $($crop.Width)x$($crop.Height),放不進 ${Width}x${Height} 的畫布"
        }

        $panel = $source.Clone($crop, $source.PixelFormat)
        $canvas = [System.Drawing.Bitmap]::new($Width, $Height)
        $g = [System.Drawing.Graphics]::FromImage($canvas)
        try {
            $g.SmoothingMode = 'AntiAlias'
            $g.InterpolationMode = 'HighQualityBicubic'
            $g.PixelOffsetMode = 'HighQuality'

            $full = [System.Drawing.Rectangle]::new(0, 0, $Width, $Height)
            $bg = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
                $full,
                [System.Drawing.Color]::FromArgb(246, 247, 249),
                [System.Drawing.Color]::FromArgb(224, 228, 235),
                90.0)
            $g.FillRectangle($bg, $full)
            $bg.Dispose()

            $x = [int](($Width - $crop.Width) / 2)
            $y = [int](($Height - $crop.Height) / 2)

            # 陰影:一層層放大的半透明圓角矩形。GDI+ 沒有模糊,這是最便宜的近似。
            for ($i = 22; $i -ge 1; $i--) {
                $alpha = [int](2 + (22 - $i) * 0.5)
                $shadowPath = New-RoundedPath ($x - $i) ($y - $i + 8) ($crop.Width + $i * 2) ($crop.Height + $i * 2) ($cornerRadius + $i)
                $shadowBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb($alpha, 32, 38, 48))
                $g.FillPath($shadowBrush, $shadowPath)
                $shadowBrush.Dispose()
                $shadowPath.Dispose()
            }

            $clip = New-RoundedPath $x $y $crop.Width $crop.Height $cornerRadius
            try {
                $g.SetClip($clip)
                # 原尺寸貼上,不縮放 —— 這是整支腳本的重點。
                $g.DrawImage($panel, $x, $y, $crop.Width, $crop.Height)
                $g.ResetClip()

                $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(38, 0, 0, 0), 1)
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

        $target = Join-Path $outputDir "$name.png"
        $canvas.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
        $canvas.Dispose()
        $panel.Dispose()

        $kb = [math]::Round((Get-Item $target).Length / 1KB, 1)
        Write-Host ("  {0,-24} 裁掉黑邊 -> {1}x{2},置中於 {3}x{4}  ({5} KB)" -f `
            $sources[$name], $crop.Width, $crop.Height, $Width, $Height, $kb)
    }
    finally {
        $source.Dispose()
    }
}

Write-Host ""
Write-Host "輸出:$outputDir"
Write-Host "Store 桌面截圖下限是 1366x768;gallery 的 screenshots/ 每張上限 1 MB、不收 GIF。"
