#requires -Version 7.0
<#
.SYNOPSIS
    把 publish 輸出與 build 佈局併成一個可註冊 / 可打包的套件佈局。

.DESCRIPTION
    這件事本來在兩個地方各寫了一份 —— tools\deploy.ps1(本機註冊)與
    .github\workflows\release.yml(CI 打包)。兩份會漂:release.yml 的 makeappx
    是「從沒跑過」的那條路(見 docs/development.md〈CI 覆蓋到哪裡〉),
    真的漂了也不會有人發現，直到某次打 tag。

    為什麼要併:**publish 輸出裡沒有 AppxManifest.xml**，那是 build 佈局才會產生的
    (由 Package.appxmanifest 經 MSIX 目標展開，身分、版本、Extensions 全在裡面)。
    而 trimming 只在 publish 生效，所以套件內容必須取自 publish。兩邊各有一半。

.PARAMETER PublishDir
    dotnet publish 的輸出目錄(trimmed 的套件內容)。

.PARAMETER BuildLayoutDir
    dotnet build 的佈局目錄，只從這裡取 AppxManifest.xml。

.PARAMETER StageDir
    要組出來的佈局目錄。已存在的話**整個刪掉重建** —— 殘留的舊檔會被 makeappx
    一起打進套件，而那種多餘檔案不會讓任何一步失敗。

.OUTPUTS
    組好的佈局目錄的絕對路徑。

.EXAMPLE
    .\tools\stage-layout.ps1 -PublishDir ... -BuildLayoutDir ... -StageDir ...
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDir,

    [Parameter(Mandatory)]
    [string]$BuildLayoutDir,

    [Parameter(Mandatory)]
    [string]$StageDir
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $PublishDir)) {
    throw "找不到 publish 輸出:$PublishDir`n(dotnet publish 有跑過嗎?)"
}

$manifestSource = Join-Path $BuildLayoutDir 'AppxManifest.xml'
if (-not (Test-Path $manifestSource)) {
    throw "找不到 AppxManifest.xml:$manifestSource`n(那是 dotnet build 才會產生的，publish 不會)"
}

if (Test-Path $StageDir) { Remove-Item $StageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $StageDir | Out-Null

Copy-Item "$PublishDir\*" $StageDir -Recurse -Force
Copy-Item $manifestSource $StageDir -Force

$staged = (Resolve-Path $StageDir).Path

# 兩個必要成分各自確認一次。少了 resources.pri，套件註冊得起來但所有本地化資源
# 都讀不到;少了 Assets，圖示全變成 Windows 的預設灰方塊 —— 兩種都不會報錯。
foreach ($required in @('AppxManifest.xml', 'resources.pri', 'Assets', 'Inkling.exe')) {
    if (-not (Test-Path (Join-Path $staged $required))) {
        throw "佈局少了 ${required}:$staged"
    }
}

$size = [math]::Round(((Get-ChildItem $staged -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "    $staged  ($size MB)" -ForegroundColor DarkGray

$staged
