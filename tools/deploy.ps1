<#
.SYNOPSIS
    Build 並把 Notelet 擴展部署到本機 Command Palette,然後驗證註冊確實生效。

.DESCRIPTION
    官方文檔的部署流程是 Visual Studio 的 Build > Deploy。這台機器沒有 Visual Studio,
    所以走的是等價的 CLI 路徑:

        dotnet build / publish     產出套件內容
        Add-AppxPackage -Register  以 loose file 方式註冊(需要 Developer Mode)
        VerifyRegistration         查 Windows AppExtension 目錄確認 CmdPal 看得到

    最後一步是刻意加的:沒有它就只能靠肉眼開 CmdPal 確認,而那不算驗證。

    Debug 與 Release 走的路不一樣,原因是 trimming 只在 publish 時生效:

      Debug   直接註冊 build 佈局(bin\x64\Debug\...),約 106 MB。
              沒有 trimming,內層迴圈快,Debug.WriteLine 也有作用,適合開發。

      Release 先 publish(trimming 在這裡生效,約 30 MB),再把 build 佈局產生的
              AppxManifest.xml 併進 publish 輸出,組成一個可註冊的暫存佈局。
              少了這一步就只會註冊到未 trim 的 build 佈局 —— 那等於根本沒驗到 trimming
              有沒有把東西砍壞。日常使用建議用這個。

.PARAMETER Configuration
    Debug(預設)或 Release。

.PARAMETER SkipBuild
    跳過 build,只重新註冊既有的輸出。

.EXAMPLE
    .\tools\deploy.ps1
    .\tools\deploy.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionProject = Join-Path $repoRoot 'src\Notelet\Notelet.csproj'
$verifyProject = Join-Path $repoRoot 'tools\VerifyRegistration\VerifyRegistration.csproj'
$targetFramework = 'net10.0-windows10.0.26100.0'
$packageName = 'Notelet'

function Write-Step($message) {
    Write-Host ''
    Write-Host "==> $message" -ForegroundColor Cyan
}

# --- 前置檢查:Developer Mode ---------------------------------------------
# 沒開的話 Add-AppxPackage -Register 會失敗,而錯誤訊息不會告訴你原因。
$devModeKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
$devMode = (Get-ItemProperty -Path $devModeKey -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($devMode -ne 1) {
    throw "Developer Mode 沒有開啟,無法以 loose file 註冊套件。請到 設定 > 系統 > 開發人員專用 開啟。"
}

# --- 停掉還在跑的擴展進程 --------------------------------------------------
# CmdPal 會把擴展的 COM server 進程留著,不先停掉 build 會因檔案被佔用而失敗。
$running = Get-Process -Name $packageName -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "停止還在執行的 $packageName 進程(共 $($running.Count) 個)"
    $running | Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process -Name $packageName -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
}

$buildLayout = Join-Path $repoRoot "src\Notelet\bin\x64\$Configuration\$targetFramework\win-x64"

# --- Build / Publish -------------------------------------------------------
if (-not $SkipBuild) {
    if ($Configuration -eq 'Release') {
        Write-Step "Publish (Release, x64, 含 trimming)"
        dotnet publish $extensionProject -c Release -p:Platform=x64 -p:PublishProfile=win-x64
    }
    else {
        Write-Step "Build (Debug, x64)"
        dotnet build $extensionProject -c Debug -p:Platform=x64
    }

    if ($LASTEXITCODE -ne 0) { throw "建置失敗(結束碼 $LASTEXITCODE)" }
}

# --- 決定要註冊哪個佈局 ----------------------------------------------------
if ($Configuration -eq 'Release') {
    $publishOutput = Join-Path $repoRoot "src\Notelet\bin\Release\$targetFramework\win-x64\publish"
    $layout = Join-Path $repoRoot 'src\Notelet\bin\stage-Release'

    if (-not (Test-Path $publishOutput)) { throw "找不到 publish 輸出:$publishOutput" }

    Write-Step "組出可註冊的 trimmed 佈局"
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $layout | Out-Null

    Copy-Item "$publishOutput\*" $layout -Recurse -Force

    # publish 輸出沒有 AppxManifest.xml,那是 build 才會產生的。
    Copy-Item (Join-Path $buildLayout 'AppxManifest.xml') $layout -Force

    $size = [math]::Round(((Get-ChildItem $layout -Recurse -File | Measure-Object Length -Sum).Sum / 1MB), 1)
    Write-Host "    $layout  ($size MB)" -ForegroundColor DarkGray
}
else {
    $layout = $buildLayout
}

$manifest = Join-Path $layout 'AppxManifest.xml'
if (-not (Test-Path $manifest)) {
    throw "找不到 AppxManifest.xml:$manifest`n(建置有成功嗎?)"
}

# --- 註冊 ------------------------------------------------------------------
# 陷阱:同一個 identity + version 已經註冊時,Add-AppxPackage -Register 會靜默地
# 什麼都不做,舊的 InstallLocation 原封不動 —— 指令回報成功,但你根本沒部署到新東西。
# 在 Debug 與 Release 之間切換時這會讓人白忙很久,所以位置不同就先移除舊註冊。
# -PreserveApplicationData 是為了保住 LocalState 裡的擴展設定,不然每次部署設定都會被清掉。
$targetLocation = (Resolve-Path $layout).Path
$existing = Get-AppxPackage -Name $packageName

if ($existing -and $existing.InstallLocation -ne $targetLocation) {
    Write-Step "已註冊的位置不同,先移除舊註冊"
    Write-Host "    舊: $($existing.InstallLocation)" -ForegroundColor DarkGray
    Write-Host "    新: $targetLocation" -ForegroundColor DarkGray
    Remove-AppxPackage -Package $existing.PackageFullName -PreserveApplicationData
}

Write-Step "註冊套件"
Write-Host "    $manifest" -ForegroundColor DarkGray
Add-AppxPackage -Register $manifest

# 明確確認註冊真的指到我們要的佈局,別再讓靜默的 no-op 混過去。
$registered = (Get-AppxPackage -Name $packageName).InstallLocation
if ($registered -ne $targetLocation) {
    throw "註冊沒有生效。實際指向:`n  $registered`n預期:`n  $targetLocation"
}

# --- 驗證 ------------------------------------------------------------------
Write-Step "驗證 Windows AppExtension 目錄"
dotnet run --project $verifyProject -c Release -- $packageName
if ($LASTEXITCODE -ne 0) { throw "驗證失敗:套件沒有註冊成 Command Palette 擴展" }

Write-Host ''
Write-Host "部署完成($Configuration)。" -ForegroundColor Green
Write-Host "接著在 Command Palette 執行 'Reload'(選副標題是 'Reload Command Palette extensions' 那一個)," -ForegroundColor Yellow
Write-Host "否則 CmdPal 會繼續用舊的擴展實例。" -ForegroundColor Yellow
