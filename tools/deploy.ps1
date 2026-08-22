<#
.SYNOPSIS
    Build 並把 Inkling 擴展部署到本機 Command Palette,然後驗證註冊確實生效。

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

.PARAMETER Reload
    部署完自動叫 CmdPal 重新載入擴展(x-cmdpal://reload),不必手動去執行 Reload。
    需要先在 CmdPal 設定 > 一般 > For developers 打開 "Enable external reload"。

    順帶解掉「Reload 後出現兩個 Inkling」:重新註冊套件會讓 Windows 的套件目錄發出
    安裝事件,CmdPal 收到後會為同一個擴展再加一個 provider(它那邊沒有去重)。
    手動 Reload 如果搶在那個事件之前跑完,清空的是舊清單,之後補進來的就變成第二個。
    所以這裡先等事件落地再重新載入。

.PARAMETER ReloadDelaySeconds
    重新載入前等幾秒,讓套件目錄的安裝事件先處理完。預設 5 秒。

.EXAMPLE
    .\tools\deploy.ps1
    .\tools\deploy.ps1 -Configuration Release
    .\tools\deploy.ps1 -Configuration Release -Reload
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipBuild,

    [switch]$Reload,

    [ValidateRange(0, 60)]
    [int]$ReloadDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$extensionProject = Join-Path $repoRoot 'src\Inkling\Inkling.csproj'
$verifyProject = Join-Path $repoRoot 'tools\VerifyRegistration\VerifyRegistration.csproj'
$targetFramework = 'net10.0-windows10.0.26100.0'
# 進程名。Store 不會動它 —— 它來自 manifest 的 Application Executable,不是 Identity。
$processName = 'Inkling'

# **套件名要用萬用字元比對。** Store 上架時會重指派 Identity 的 Name(變成
# 「<發行者>.<名稱>」)與 Publisher(變成 CN=<GUID>)—— 本機兩個已上架的 CmdPal
# 擴展就是證據。`Get-AppxPackage -Name` 是精確比對,寫死 'Inkling' 的話上架之後
# 一律回 $null,而「找不到套件」在腳本裡多半只是安靜跳過,看起來像沒事。
$packageNamePattern = '*Inkling*'

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
$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    Write-Step "停止還在執行的 $processName 進程(共 $($running.Count) 個)"
    $running | Stop-Process -Force
    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Process -Name $processName -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
}

$buildLayout = Join-Path $repoRoot "src\Inkling\bin\x64\$Configuration\$targetFramework\win-x64"

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
    $publishOutput = Join-Path $repoRoot "src\Inkling\bin\Release\$targetFramework\win-x64\publish"
    $layout = Join-Path $repoRoot 'src\Inkling\bin\stage-Release'

    Write-Step "組出可註冊的 trimmed 佈局"
    # 與 release.yml 的打包共用同一支腳本,避免兩邊各寫一份然後漂掉。
    & (Join-Path $PSScriptRoot 'stage-layout.ps1') `
        -PublishDir $publishOutput -BuildLayoutDir $buildLayout -StageDir $layout | Out-Null
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

# 同一台機器上出現兩個同名套件是真的會發生的(重新註冊之後 CmdPal 沒有去重,
# 見 CLAUDE.md 第 6 條)。`Get-AppxPackage` 回陣列時,底下每一個 `.InstallLocation`
# 都會變成陣列 —— 比對永遠不相等,於是每次部署都先移除再註冊,而「移除的是哪一個」
# 不確定。cmdpal-ui.ps1 早就有這道守門,兩支腳本問的是同一件事,答案不該不一樣。
$installed = @(Get-AppxPackage -Name $packageNamePattern)
if ($installed.Count -gt 1) {
    throw "找到不只一個 Inkling 套件($($installed.PackageFullName -join ', ')),請先清掉重複的再部署。"
}

$existing = if ($installed.Count -eq 1) { $installed[0] } else { $null }

if ($existing -and $existing.InstallLocation -ne $targetLocation) {
    Write-Step "已註冊的位置不同,先移除舊註冊"
    Write-Host "    舊: $($existing.InstallLocation)" -ForegroundColor DarkGray
    Write-Host "    新: $targetLocation" -ForegroundColor DarkGray
    Remove-AppxPackage -Package $existing.PackageFullName -PreserveApplicationData
}

Write-Step "註冊套件"
Write-Host "    $manifest" -ForegroundColor DarkGray

try {
    Add-AppxPackage -Register $manifest -ErrorAction Stop
}
catch {
    # 0x80073CFB:「已安裝過提供的套件,不允許重新安裝」。位置與版本都沒變、但
    # **AppxManifest.xml 的內容變了**就會這樣 —— 上面那段只處理「位置不同」,
    # 同一個位置改 manifest(加一個屬性、動一個 Extension)照樣會撞。
    # 訊息叫人「遞增版本號碼」,但版本號是對外承諾,不該為了本機部署動它;
    # 移除註冊再登錄一次就好,-PreserveApplicationData 保住 LocalState 裡的設定。
    if ($_.Exception.Message -notmatch '0x80073CFB') { throw }

    Write-Host "    manifest 變了,先移除註冊再登錄一次" -ForegroundColor DarkGray
    $stale = @(Get-AppxPackage -Name $packageNamePattern)
    if ($stale.Count -eq 1) { Remove-AppxPackage -Package $stale[0].PackageFullName -PreserveApplicationData }
    Add-AppxPackage -Register $manifest
}

# 明確確認註冊真的指到我們要的佈局,別再讓靜默的 no-op 混過去。
$registered = (Get-AppxPackage -Name $packageNamePattern).InstallLocation
if ($registered -ne $targetLocation) {
    throw "註冊沒有生效。實際指向:`n  $registered`n預期:`n  $targetLocation"
}

# --- 驗證 ------------------------------------------------------------------
Write-Step "驗證 Windows AppExtension 目錄"
dotnet run --project $verifyProject -c Release -- $processName
if ($LASTEXITCODE -ne 0) { throw "驗證失敗:套件沒有註冊成 Command Palette 擴展" }

Write-Host ''
Write-Host "部署完成($Configuration)。" -ForegroundColor Green

# --- 重新載入 --------------------------------------------------------------
if (-not $Reload) {
    Write-Host "接著在 Command Palette 執行 'Reload'(選副標題是 'Reload Command Palette extensions' 那一個)," -ForegroundColor Yellow
    Write-Host "否則 CmdPal 會繼續用舊的擴展實例。或者下次加上 -Reload 讓腳本自己來。" -ForegroundColor Yellow
    return
}

# 外部重新載入沒開的話,x-cmdpal://reload 會被 CmdPal 靜靜丟掉(只寫進它自己的 log)。
# 與其讓人以為載入過了,不如在這裡明講。
$cmdPalSettings = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState\settings.json'
$externalReloadEnabled = $false
if (Test-Path $cmdPalSettings) {
    try {
        $externalReloadEnabled = [bool](Get-Content $cmdPalSettings -Raw | ConvertFrom-Json).AllowExternalReload
    }
    catch {
        # 設定檔讀不動不該讓部署失敗,退回「當作沒開」的提示。
        $externalReloadEnabled = $false
    }
}

if (-not $externalReloadEnabled) {
    Write-Host ''
    Write-Host "-Reload 需要先打開 CmdPal 設定 > 一般 > For developers > Enable external reload。" -ForegroundColor Yellow
    Write-Host "目前是關的,這次請手動執行 Reload。" -ForegroundColor Yellow
    return
}

Write-Step "等 $ReloadDelaySeconds 秒讓套件目錄的安裝事件落地"
Start-Sleep -Seconds $ReloadDelaySeconds

Write-Step "叫 CmdPal 重新載入擴展"
Start-Process 'x-cmdpal://reload'
Write-Host "    已送出 x-cmdpal://reload" -ForegroundColor DarkGray
