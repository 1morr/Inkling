<#
.SYNOPSIS
    驅動 Command Palette 的 UI,做 Inkling 的實機驗證。

.DESCRIPTION
    CmdPal 沒有 UI 自動化介面,擴展又跑在獨立的 COM 進程裡 —— 這個腳本用 Windows
    內建的 UI Automation 讀它的畫面,用 SendInput 打字與按鍵,補上
    docs\manual-test-checklist.md 裡那些「只能靠眼睛」的項目。

    **一整串動作一定要在同一次呼叫裡跑完。** CmdPal 一失焦就自我隱藏,每啟動一個新的
    PowerShell 進程都可能把它打斷 —— 那正是要用 -Steps "a|b|c" 而不是連續呼叫三次的原因。

    **不會把按鍵送到別的視窗。** SendInput 指定不了目標視窗,它送到的永遠是
    當下的前景視窗 —— 所以每一個會送按鍵的步驟在送出**之前**都先確認
    CmdPal 在前景,不在就中止整串,並把當時的前景視窗是誰印出來。

    要用 pwsh(PowerShell 7)跑。這個檔案是無 BOM 的 UTF-8,Windows PowerShell 5.1
    會照系統 ANSI 讀,中文全部變亂碼。

.PARAMETER Steps
    用 | 串起來的動作序列。動作與參數之間用第一個 : 分開:

      show            叫出 CmdPal(熱鍵從 CmdPal 自己的 settings.json 讀,不寫死)
      esc             送 Esc(退一層頁面;在主頁等於關掉面板)
      type:<文字>     打字,走 Unicode 注入,中文與全形符號都可以
      key:<組合>      按鍵,例如 key:Enter / key:Ctrl+D / key:Ctrl+Shift+C
                      (一次一組;不認得的按鍵會**中止整串**並以非零結束 —— 印個警告
                      繼續跑的話,後面的 Enter 會落在沒預期的地方)
      wait:<毫秒>     等待
      tree[:<深度>]   dump UI Automation 樹(預設深度 14)
      shot:<路徑>     截圖(PrintWindow,不受遮擋影響;**拍不到 Ctrl+K 的選單 popup**
                      —— 那是獨立的頂層視窗,不在主視窗的內容裡,平台限制無解)
      toast           toast 視窗的狀態,可見的話連內容一起讀出來(兩種預期都有,見函式說明)
      notes           列出目前設定的筆記資料夾內容
      log[:<行數>]    diagnostic.log 的尾巴(預設 20 行)
      state           兩份 settings.json 的摘要(Inkling 自己的 + CmdPal 那邊的)

.PARAMETER Retries
    整串動作最多嘗試幾次(含第一次),只有在 CmdPal 中途失焦時才會重跑。預設 4。
    **重跑是從第一步開始**,已經送出的按鍵會再送一遍 —— 序列裡有存檔、
    刪除這類有副作用的步驟時,重跑等於再做一次(真的重跑了會在輸出裡警告)。
    全部試完還是沒跑完的話,腳本以**非零結束**。

.EXAMPLE
    pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:Inkling|wait:800|tree:6"

.EXAMPLE
    # 快速記下:打字 → Enter → 確認檔案真的落地,而且沒有跳 toast
    pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:! |wait:600|type:測試想法|wait:600|tree:8|key:Enter|wait:900|toast|notes"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Steps,
    [int]$Retries = 4,
    [int]$MaxText = 120
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;

public class CmdPalNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    // INPUT 在 x64 下是 40 bytes,union 從 offset 8 開始。
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] p, int cb);

    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);

    // 判斷「CmdPal 有沒有焦點」要看**前景視窗屬於哪個進程**,不是比對某一個 HWND。
    // CmdPal 的 Ctrl+K 選單、確認框都是獨立的頂層視窗(標題不是 'Command Palette'),
    // 比對 HWND 會把它們當成失焦 —— 然後整串白白重跑,而重跑會把按鍵再送一次。
    public static uint ForegroundPid() {
        uint pid = 0; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid;
    }

    public static string ForegroundTitle() {
        StringBuilder b = new StringBuilder(256);
        GetWindowText(GetForegroundWindow(), b, 256);
        return b.ToString();
    }

    // 這個進程必須是 per-monitor DPI aware,否則截圖會缺一塊。
    //
    // 螢幕縮放不是 100% 時(這台是 150%),DPI-unaware 的進程拿到的 GetWindowRect 是
    // Windows 虛擬化過的**邏輯**座標(1200x720 的視窗回報成 800x480),而 PrintWindow
    // 畫的是**實體**像素 —— 於是點陣圖只有 800x480、內容卻是照 1200 寬排的,
    // 右邊與下面整片被切掉。而且它不會報錯,截出來的圖乍看還很正常,
    // 只有拿去跟畫面對照才會發現少了東西。
    //
    // 用 SetProcessDpiAwarenessContext(Win10 1703+);拿不到就退回 SetProcessDPIAware
    // (舊版 API,system-aware 而非 per-monitor,單螢幕情境夠用)。兩者都必須在
    // 進程碰到任何視窗座標**之前**呼叫,設定之後不能改。
    public const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    public static void MakeDpiAware() {
        try {
            if (SetProcessDpiAwarenessContext((IntPtr)DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) { return; }
        } catch (EntryPointNotFoundException) { }
        try { SetProcessDPIAware(); } catch (EntryPointNotFoundException) { }
    }

    // PrintWindow 抓的是視窗自己的內容,不是螢幕像素 —— CopyFromScreen 會抓到蓋在
    // 上面的那個視窗,而且在多螢幕 / 高 DPI 下座標還會對不上。
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static INPUT Vk(ushort vk, bool up) {
        INPUT i = new INPUT(); i.type = 1; i.ki.wVk = vk; i.ki.dwFlags = up ? 2u : 0u; return i;
    }

    // KEYEVENTF_UNICODE(0x4):碼位直接放 wScan,不經過鍵盤配置 ——
    // 中文與全形符號只有這條路打得出來。
    public static INPUT Unicode(char c, bool up) {
        INPUT i = new INPUT(); i.type = 1; i.ki.wScan = (ushort)c; i.ki.dwFlags = up ? (4u | 2u) : 4u; return i;
    }

    public static void Send(INPUT[] a) { SendInput((uint)a.Length, a, Marshal.SizeOf(typeof(INPUT))); }
}
"@

# 進程一啟動就宣告 DPI 感知 —— 必須早於任何視窗座標的讀取(見 CmdPalNative.MakeDpiAware)。
[CmdPalNative]::MakeDpiAware()

# ---------------------------------------------------------------- 路徑與常數

$CmdPalLocalState = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState'

# Package family name 不寫死:它由套件身分( Name + Publisher )推出,換身分之一個字
# 就全變。寫死的話換完身分之後這裡會指向不存在的目錄,而 notes / log / state 讀不到
# 檔案只會安靜跳過 —— 「看起來沒壞」比報錯更糟,所以動態取、取不到就直接中止。
$InklingLocalState = $null
$inklingPackage = @(Get-AppxPackage -Name Inkling -ErrorAction SilentlyContinue)
if ($inklingPackage.Count -gt 1) {
    throw "找到不只一個 Inkling 套件($($inklingPackage.PackageFamilyName -join ', ')),請先清掉重複的。"
}
if ($inklingPackage.Count -eq 1) {
    $InklingLocalState = Join-Path $env:LOCALAPPDATA "Packages\$($inklingPackage[0].PackageFamilyName)\LocalState"
} else {
    Write-Output '  !! 找不到已註冊的 Inkling 套件(Get-AppxPackage -Name Inkling 是空的)——'
    Write-Output '     notes / log / state 會讀不到東西。先跑 tools\deploy.ps1 註冊再來。'
}

# 視窗標題是寫死的英文,不跟著 Windows 顯示語言走(在 zh-TW 機器上實測仍是英文)。
$MainWindowTitle = 'Command Palette'
$ToastWindowTitle = 'Command Palette Toast'

# ---------------------------------------------------------------- 視窗

$script:CmdPalPid = $null

function Get-CmdPalPid {
    if ($null -eq $script:CmdPalPid) {
        $p = Get-Process Microsoft.CmdPal.UI -ErrorAction SilentlyContinue
        $script:CmdPalPid = if ($p) { $p.Id } else { 0 }
    }
    return $script:CmdPalPid
}

<#
    照標題找 CmdPal 的頂層視窗。

    不能用 (Get-Process ...).MainWindowHandle 找主面板 —— CmdPal 是 WinUI 3 應用,
    主面板不是它的「主視窗」,那個屬性平常是 0,連面板開著的時候也是。
    (唯一的例外:設定視窗開著時 MainWindowHandle 會指向設定視窗 —— 拿它當依據
    只會找到設定視窗,永遠找不到主面板。)同樣的原因,orca computer list-apps
    整個看不到 CmdPal,--app pid:<CmdPal> 會回 app_not_found。
#>
function Find-CmdPalWindow {
    param([string]$Title = $MainWindowTitle, [switch]$VisibleOnly)

    $targetPid = Get-CmdPalPid
    if (-not $targetPid) { return [IntPtr]::Zero }

    $script:foundWindow = [IntPtr]::Zero
    $callback = [CmdPalNative+EnumProc] {
        param($hwnd, $lparam)
        $ownerPid = 0
        [CmdPalNative]::GetWindowThreadProcessId($hwnd, [ref]$ownerPid) | Out-Null
        if ($ownerPid -ne $targetPid) { return $true }
        if ($VisibleOnly -and -not [CmdPalNative]::IsWindowVisible($hwnd)) { return $true }

        $buf = New-Object System.Text.StringBuilder 256
        [CmdPalNative]::GetWindowText($hwnd, $buf, 256) | Out-Null
        if ($buf.ToString() -eq $Title) { $script:foundWindow = $hwnd; return $false }
        return $true
    }
    [CmdPalNative]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $script:foundWindow
}

<#
    CmdPal 有沒有焦點。

    看的是**前景視窗的擁有進程**,不是某一個 HWND —— Ctrl+K 的選單、確認框都是
    CmdPal 自己的獨立頂層視窗,比對 HWND 會把它們當成失焦(見 CmdPalNative.ForegroundPid)。
    順帶一提這樣也不必每次都 EnumWindows。
#>
function Test-CmdPalForeground {
    $targetPid = Get-CmdPalPid
    if (-not $targetPid) { return $false }
    if ([CmdPalNative]::ForegroundPid() -ne [uint32]$targetPid) { return $false }

    # 前景屬於 CmdPal 還**不夠**:x-cmdpal:// 把進程拉起來之後有一段時間是
    # 「進程在、面板還沒出來」,那時候前景視窗確實屬於 CmdPal,但打字會落在一個
    # 看不見的視窗上,而且後面的 tree 只讀得到根節點。所以還要確認面板真的在。
    return ((Find-CmdPalWindow -VisibleOnly) -ne [IntPtr]::Zero)
}

# 失焦時要能一眼看出「那串字跑到哪去了」,所以把前景視窗是誰印出來。
function Get-ForegroundDescription {
    $fgPid = [CmdPalNative]::ForegroundPid()
    $name = (Get-Process -Id $fgPid -ErrorAction SilentlyContinue).ProcessName
    if (-not $name) { $name = '?' }
    return "$name (pid=$fgPid) '$([CmdPalNative]::ForegroundTitle())'"
}

<#
    面板「可以用了」,而不只是「視窗在而且前景是 CmdPal」。

    面板收起來的那一小段時間裡,前景進程還是 CmdPal、視窗也還 IsWindowVisible ——
    純視窗層級的檢查分辨不出「開著」跟「正在關」。實測踩過兩次:show 因此判定
    「已經開著」直接返回,然後打字打進一個正在關閉的面板;SetForegroundWindow 也
    因此把一個正在關閉的面板拉到前景。兩次的症狀都是 tree 只讀得到根節點。

    UIA 讀不讀得到子節點才是真的判準 —— 那也正是 Write-UiaTree 自己用的判準。
#>
function Test-CmdPalReady {
    if (-not (Test-CmdPalForeground)) { return $false }

    $hwnd = Find-CmdPalWindow -VisibleOnly
    if ($hwnd -eq [IntPtr]::Zero) { return $false }

    try {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        if ($null -eq $el) { return $false }
        return ($null -ne [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetFirstChild($el))
    } catch {
        # 畫面更新中 UIA 元素會失效 —— 那就是「還不能用」。
        return $false
    }
}

<#
    等 CmdPal 的面板變成可用。輪詢而不是睡固定時間 —— 機器快慢差很多,
    睡太短會讓後面的按鍵打進別的視窗,睡太長是白等。
#>
function Wait-CmdPalReady {
    param([int]$TimeoutMs = 2000)

    $script:CmdPalPid = $null   # 進程可能剛重啟過,快取住的 pid 會是舊的
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (Test-CmdPalReady) { return $true }
        if ($sw.ElapsedMilliseconds -ge $TimeoutMs) { return $false }
        Start-Sleep -Milliseconds 100
    }
}

<#
    **送任何按鍵之前**都要先過這一關。

    SendInput 送到的是當下的前景視窗,不能指定目標。所以「CmdPal 不在前景」等於
    「這串字會打進使用者正在用的別的視窗」—— 這個腳本曾經是先送再檢查,
    檢查只是事後報告,而且失焦會整串重跑,同一串字被打進錯的地方好幾遍。

    結果放在 $script:FocusOk,**不要改成回傳 $true / $false**。PowerShell 函式的
    「回傳值」是它**整條輸出管線** —— 函式裡的 Write-Output 會跟 return 的布林值
    併成一個陣列,`if (-not (Assert-CmdPalFocus ...))` 拿到的是非空陣列,判定永遠是
    $false,守門等於完全放行,而且那幾行訊息還會被當成回傳值吞掉、根本印不出來。
    這個坑真的踩過,症狀是「守門看起來在、實際上一次都沒擋」。

    也不能改用 Write-Host:PS7 的 Write-Host 走 information stream,
    `pwsh -File x.ps1 | Select-String` 看不到它 —— 而這份輸出就是拿來 grep 的。
#>
function Assert-CmdPalFocus {
    param([string]$Verb)

    $script:FocusOk = $true
    if (Test-CmdPalReady) { return }

    # 可能只是還在轉場(剛按完鍵、頁面正在切),給它一點時間再判定。
    if (Wait-CmdPalReady -TimeoutMs 700) { return }

    # 視窗還在、只是被壓在後面的話,拉一次看看。CmdPal 一失焦通常會自己隱藏,
    # 所以這條多半救不回來,但成本只有一次呼叫。
    $hwnd = Find-CmdPalWindow -VisibleOnly
    if ($hwnd -ne [IntPtr]::Zero) {
        [CmdPalNative]::SetForegroundWindow($hwnd) | Out-Null
        if (Wait-CmdPalReady -TimeoutMs 500) { return }
    }

    Write-Output "  !! CmdPal 不在前景,'$Verb' **沒有送出**(送了會打進別的視窗)"
    Write-Output "     目前前景:$(Get-ForegroundDescription)"
    $script:FocusOk = $false
}

# ---------------------------------------------------------------- 鍵盤

$VirtualKeys = @{
    'Enter' = 0x0D; 'Return' = 0x0D; 'Escape' = 0x1B; 'Esc' = 0x1B; 'Tab' = 0x09
    'Space' = 0x20; 'Backspace' = 0x08; 'Delete' = 0x2E
    'Up' = 0x26; 'Down' = 0x28; 'Left' = 0x25; 'Right' = 0x27
    'Home' = 0x24; 'End' = 0x23; 'PageUp' = 0x21; 'PageDown' = 0x22
    'Comma' = 0xBC; 'Period' = 0xBE; 'Semicolon' = 0xBA
}
0..9 | ForEach-Object { $VirtualKeys["$_"] = 0x30 + $_ }
[char[]]'ABCDEFGHIJKLMNOPQRSTUVWXYZ' | ForEach-Object { $VirtualKeys["$_"] = [int][char]$_ }
1..12 | ForEach-Object { $VirtualKeys["F$_"] = 0x6F + $_ }

function Send-Chord {
    param([string]$Chord)

    $parts = $Chord -split '\+'
    $main = $parts[-1]
    $modifiers = @()
    if ($parts.Length -gt 1) {
        foreach ($p in $parts[0..($parts.Length - 2)]) {
            switch ($p.ToLowerInvariant()) {
                'ctrl' { $modifiers += 0x11 }
                'shift' { $modifiers += 0x10 }
                'alt' { $modifiers += 0x12 }
                'win' { $modifiers += 0x5B }
                # 不認得就整串中止(throw 會讓腳本以非零結束):繼續跑等於把後面的
                # 按鍵送進沒預期的狀態,那比直接失敗更難查。
                default { throw "不認得的修飾鍵:$p(整串中止)" }
            }
        }
    }
    if (-not $VirtualKeys.ContainsKey($main)) {
        throw "不認得的按鍵:$main(整串中止)。key: 一次只吃一組組合,連按請拆成多個 step。"
    }

    $vk = $VirtualKeys[$main]
    $seq = @()
    foreach ($m in $modifiers) { $seq += [CmdPalNative]::Vk([ushort]$m, $false) }
    $seq += [CmdPalNative]::Vk([ushort]$vk, $false)
    $seq += [CmdPalNative]::Vk([ushort]$vk, $true)
    for ($i = $modifiers.Length - 1; $i -ge 0; $i--) {
        $seq += [CmdPalNative]::Vk([ushort]$modifiers[$i], $true)
    }
    [CmdPalNative]::Send($seq)
}

function Send-Text {
    param([string]$Value)
    if ($Value.Length -eq 0) { return }
    $seq = @()
    foreach ($c in $Value.ToCharArray()) {
        $seq += [CmdPalNative]::Unicode($c, $false)
        $seq += [CmdPalNative]::Unicode($c, $true)
    }
    [CmdPalNative]::Send($seq)
}

<#
    叫出 CmdPal。

    熱鍵**從 CmdPal 自己的 settings.json 讀**,不寫死 —— 那是使用者可以改的設定,
    寫死的話換一台機器就靜靜地什麼都不會發生。讀不到才退回 Alt+Space。
#>
function Show-CmdPal {
    # CmdPal 的進程不在的話,熱鍵是叫不動的 —— PowerToys 剛重啟之後就是這個狀態。
    # 先用它自己註冊的 protocol 把進程拉起來,再走熱鍵。
    if (-not (Get-CmdPalPid)) {
        Write-Output '  CmdPal 沒在跑,先用 x-cmdpal:// 把它拉起來'
        Start-Process 'x-cmdpal://' -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 3
        $script:CmdPalPid = $null
    }

    $hotkey = [pscustomobject]@{ win = $false; ctrl = $false; alt = $true; shift = $false; code = 0x20 }
    $settingsPath = Join-Path $CmdPalLocalState 'settings.json'
    if (Test-Path $settingsPath) {
        try {
            $json = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($json.Hotkey) { $hotkey = $json.Hotkey }
        } catch {
            Write-Output "  !! 讀不到 CmdPal 的熱鍵設定,退回 Alt+Space:$($_.Exception.Message)"
        }
    }

    $modifiers = @()
    if ($hotkey.win) { $modifiers += 0x5B }
    if ($hotkey.ctrl) { $modifiers += 0x11 }
    if ($hotkey.alt) { $modifiers += 0x12 }
    if ($hotkey.shift) { $modifiers += 0x10 }

    $seq = @()
    foreach ($m in $modifiers) { $seq += [CmdPalNative]::Vk([ushort]$m, $false) }
    $seq += [CmdPalNative]::Vk([ushort]$hotkey.code, $false)
    $seq += [CmdPalNative]::Vk([ushort]$hotkey.code, $true)
    for ($i = $modifiers.Length - 1; $i -ge 0; $i--) {
        $seq += [CmdPalNative]::Vk([ushort]$modifiers[$i], $true)
    }
    $names = $modifiers | ForEach-Object {
        switch ($_) { 0x5B { 'Win' } 0x11 { 'Ctrl' } 0x12 { 'Alt' } 0x10 { 'Shift' } }
    }
    $chord = (@($names) + @('0x{0:X2}' -f $hotkey.code)) -join '+'

    <#
        每一輪重新看狀態、做對應的動作,不是「送完熱鍵再看結果」。

        1. 已經開著而且有焦點 -> **什麼都不做**。熱鍵是 toggle,這時候送等於把它關掉
           (序列裡有第二個 show、或前一個動作已經把面板叫出來時就會踩到)。
        2. 完全沒開 -> 送熱鍵。
        3. 開著但焦點在別的視窗 -> 拉到前景,**不能送熱鍵**(那會關掉它)。

        第 3 種還有一個陷阱:面板收起來的那一小段時間裡它仍然 visible,會被誤判成
        「開著」。實測踩過 —— 拉到前景的是一個正在關閉的面板,UIA 只讀得到根節點。
        所以要迴圈:下一輪重新看,那時候它已經真的不見了,就會走第 2 種送熱鍵。
    #>
    for ($round = 1; $round -le 3; $round++) {
        if (Test-CmdPalReady) {
            Write-Output "  熱鍵=$chord HWND=$(Find-CmdPalWindow -VisibleOnly)"
            # 結果走 $script:FocusOk,理由見 Assert-CmdPalFocus 的註解。
            $script:FocusOk = $true
            return
        }

        # 這裡不能只看 IsWindowVisible:正在關閉的面板也還是 visible,
        # 對它 SetForegroundWindow 拉到的是一個空殼。UIA 讀得到子節點才算真的開著。
        $hwnd = Find-CmdPalWindow -VisibleOnly
        if ($hwnd -ne [IntPtr]::Zero) {
            try {
                $probe = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
                if ($null -eq [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetFirstChild($probe)) {
                    $hwnd = [IntPtr]::Zero
                }
            } catch { $hwnd = [IntPtr]::Zero }
        }

        if ($hwnd -eq [IntPtr]::Zero) {
            if ($round -gt 1) { Write-Output '  面板還是沒開,再送一次熱鍵' }
            [CmdPalNative]::Send($seq)
            $null = Wait-CmdPalReady -TimeoutMs 2500
        } else {
            Write-Output '  面板開著但焦點在別的視窗,把它拉到前景'
            [CmdPalNative]::SetForegroundWindow($hwnd) | Out-Null
            $null = Wait-CmdPalReady -TimeoutMs 1200
        }
    }

    $state = if ((Find-CmdPalWindow -VisibleOnly) -eq [IntPtr]::Zero) { '沒開' } else { '開著但拿不到焦點' }
    Write-Output "  !! 試了 3 輪($chord),CmdPal 還是沒到前景(面板$state)"
    Write-Output "     目前前景:$(Get-ForegroundDescription)"
    $script:FocusOk = $false
}

# ---------------------------------------------------------------- 觀察

<#
    把過長的節點名字截短。

    詳細面板整則筆記的內文都在一個 Text 節點裡 —— 原樣印出來,一則長筆記就能把整份
    輸出淹掉。要看完整內文的話直接讀那個 .md 檔,不要靠這裡。
#>
function Format-NodeText {
    param([string]$Value, [int]$Max)

    if ($null -eq $Value) { return '' }
    $flat = $Value -replace '\r?\n', '⏎'
    if ($flat.Length -le $Max) { return $flat }
    return $flat.Substring(0, $Max) + "…(共 $($flat.Length) 字)"
}

function Write-UiaTree {
    param([int]$Depth = 14, [int]$MaxText = 120)

    # 先確認 CmdPal 還在前景。面板一隱藏,UIA 就只回得到根節點 —— 那半截樹看起來像
    # 「畫面上什麼都沒有」,其實只是焦點跑掉了。不擋的話會照著它做出錯誤判斷。
    if (-not (Test-CmdPalForeground)) { Write-Output '  !! CmdPal 不在前景,這棵樹讀不到內容'; return }

    $hwnd = Find-CmdPalWindow -VisibleOnly
    if ($hwnd -eq [IntPtr]::Zero) { Write-Output '  !! CmdPal 視窗不可見'; return }

    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $script:nodeCount = 0
    $script:readFailed = $false

    function Write-Node($element, $indent, $level) {
        if ($level -gt $Depth) { return }
        try {
            $name = Format-NodeText $element.Current.Name $MaxText
            $type = $element.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''

            $value = ''
            try {
                $pattern = $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
                if ($pattern) { $value = " value='" + (Format-NodeText $pattern.Current.Value $MaxText) + "'" }
            } catch { }

            $selected = ''
            try {
                $pattern = $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($pattern -and $pattern.Current.IsSelected) { $selected = ' [SELECTED]' }
            } catch { }

            $offscreen = if ($element.Current.IsOffscreen) { ' [off]' } else { '' }
            $focus = ''
            try { if ($element.Current.HasKeyboardFocus) { $focus = ' [FOCUS]' } } catch { }

            # 沒有名字的 Pane 只是版面容器,印出來全是雜訊。
            $isNoiseContainer = ($type -eq 'Pane' -and $name -eq '')
            if (-not $isNoiseContainer) {
                Write-Output ("{0}{1}: '{2}'{3}{4}{5}{6}" -f $indent, $type, $name, $value, $selected, $offscreen, $focus)
            }
            $script:nodeCount++
        } catch {
            # 畫面正在更新時 UIA 元素會失效(ElementNotAvailableException)。靜靜跳過的話
            # 整棵子樹就這樣消失,看起來像「畫面上什麼都沒有」—— 記下來,讓外面重試。
            $script:readFailed = $true
            return
        }

        try {
            $child = $walker.GetFirstChild($element)
            while ($child) {
                Write-Node $child ($indent + '  ') ($level + 1)
                $child = $walker.GetNextSibling($child)
            }
        } catch {
            $script:readFailed = $true
        }
    }

    # 讀到一半元素失效、或者整棵樹只剩根節點,都代表這次讀取沒讀到東西。重來一次 ——
    # 打完字之後清單重建那一瞬間很容易踩到。
    #
    # **每一輪都要重新 FromHandle。** 拿舊的那個 root 重試是沒有用的:實測過畫面明明
    # 好好的(截圖是滿的),同一個 root 物件卻怎麼問都回不出子節點 —— 失效的是那個
    # AutomationElement 本身,不是畫面。
    for ($try = 1; $try -le 3; $try++) {
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        if (-not $root) { Write-Output '  !! UIA FromHandle 失敗'; return }

        $script:nodeCount = 0
        $script:readFailed = $false
        $output = Write-Node $root '' 0
        if (-not $script:readFailed -and $script:nodeCount -gt 1) { $output; return }
        if ($try -lt 3) { Start-Sleep -Milliseconds 500 }
    }

    $output
    if ($script:nodeCount -le 1) {
        Write-Output '  !! 樹只讀到根節點 —— 畫面可能還在轉場,或者面板正在收起來'
    }
}

function Save-CmdPalScreenshot {
    param([string]$Path)

    if (-not (Test-CmdPalForeground)) { Write-Output '  !! CmdPal 不在前景,截出來會是空白'; return }

    $hwnd = Find-CmdPalWindow -VisibleOnly
    if ($hwnd -eq [IntPtr]::Zero) { Write-Output '  !! CmdPal 視窗不可見'; return }

    # 相對路徑要先轉成絕對的 —— GDI+ 的 Save 是拿當前目錄去解的,而那個目錄跟你以為的
    # 不一定一樣,失敗訊息又只會說 "A generic error occurred in GDI+"。
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path (Get-Location).Path $Path
    }

    $rect = New-Object CmdPalNative+RECT
    [CmdPalNative]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

    # 視窗還在轉場時取到的 rect 會是中間狀態,截出來只有部分視窗。等 rect 連續兩次
    # 讀到一樣的值再拍 —— 展開/收起動畫跑完之前尺寸一直在變。
    for ($i = 0; $i -lt 10; $i++) {
        Start-Sleep -Milliseconds 120
        $again = New-Object CmdPalNative+RECT
        [CmdPalNative]::GetWindowRect($hwnd, [ref]$again) | Out-Null
        if ($again.Left -eq $rect.Left -and $again.Top -eq $rect.Top -and
            $again.Right -eq $rect.Right -and $again.Bottom -eq $rect.Bottom) { break }
        $rect = $again
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -le 0 -or $height -le 0) { Write-Output '  !! 視窗尺寸不合法'; return }

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $hdc = $graphics.GetHdc()
        # 旗標 2 = PW_RENDERFULLCONTENT,WinUI 3 的 DirectComposition 內容要靠它才抓得到。
        [CmdPalNative]::PrintWindow($hwnd, $hdc, 2) | Out-Null
        $graphics.ReleaseHdc($hdc)
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "  已存 $Path (${width}x${height})"
    } catch {
        Write-Output "  !! 截圖存不進去($Path):$($_.Exception.Message)"
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

<#
    看 toast 視窗在不在,可見的話順便把裡面的字讀出來。

    CmdPal 的 toast 是**另一個頂層視窗**,它一出現就搶焦點,而主視窗一失焦就自我隱藏 ——
    也就是「做完之後整個面板消失」的成因。

    **「有 toast」本身不是對或錯,要看那條路徑的預期是什麼**,兩種都有:

      預期沒有 —— 使用者接下來還要看著面板(複製內文、刪除成功、記下並預覽頁停留期間)。
                  跳了就是 bug,見 docs/design-notes.md〈刪除成功時一個 toast 都不發〉。
      預期有   —— 收工那一下,面板本來就要關(記下並預覽頁的「完成」、隨手草稿的存檔與
                  「捨棄變更」)。這幾條**沒跳才是 bug**:面板消失分不出「存好了」跟「沒存」。

    所以可見時會把內容一起印出來 —— 「有沒有跳」跟「跳的是哪一句」是兩件事,
    預期會跳的路徑要對的正是後者(例如記下那句要跟關掉「記下後先看一眼」時一模一樣)。
    toast 只活約 2.5 秒,所以這一步要緊接在動作之後,中間的 wait 不要超過 1 秒。
#>
function Write-ToastState {
    $targetPid = Get-CmdPalPid
    if (-not $targetPid) { Write-Output '  !! CmdPal 沒在跑'; return }

    $toast = Find-CmdPalWindow -Title $ToastWindowTitle
    if ($toast -eq [IntPtr]::Zero) {
        Write-Output '  toast 視窗:不存在'
        return
    }
    $visible = [CmdPalNative]::IsWindowVisible($toast)
    $mainVisible = (Find-CmdPalWindow -VisibleOnly) -ne [IntPtr]::Zero
    Write-Output "  toast 視窗:HWND=$toast 可見=$visible / 主視窗還在=$mainVisible"
    if (-not $visible) { return }

    # 這個視窗跟主面板是分開的,所以讀它不受「主面板不在前景」那道守門影響。
    $text = ''
    try {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($toast)
        if ($el) {
            $found = $el.FindAll(
                [System.Windows.Automation.TreeScope]::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)
            $parts = @()
            foreach ($node in $found) {
                $name = $node.Current.Name
                if ($name -and $parts -notcontains $name) { $parts += $name }
            }
            $text = $parts -join ' / '
        }
    } catch { }

    if ($text) {
        Write-Output "     內容:$text"
    } else {
        Write-Output '     內容:讀不到(轉場中的話把前面的 wait 拉長一點再試)'
    }
    Write-Output '     ※ 主面板會跟著隱藏。這一條是對是錯要看那條路徑的預期,見函式上方說明。'
}

function Get-NotesDirectory {
    if (-not $InklingLocalState) { return $null }
    $settingsPath = Join-Path $InklingLocalState 'settings.json'
    if (-not (Test-Path $settingsPath)) { return $null }
    try {
        $json = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        return $json.'Inkling.NotesDirectory'
    } catch { return $null }
}

function Write-NotesFolder {
    $dir = Get-NotesDirectory
    if (-not $dir) { Write-Output '  !! 讀不到筆記資料夾設定'; return }
    Write-Output "  資料夾:$dir"
    if (-not (Test-Path $dir)) { Write-Output '  !! 資料夾不存在'; return }

    Get-ChildItem $dir -Recurse -Filter *.md -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 15 |
        ForEach-Object {
            $rel = $_.FullName.Substring($dir.Length).TrimStart('\')
            Write-Output ("    {0,6}  {1:HH:mm:ss}  {2}" -f $_.Length, $_.LastWriteTime, $rel)
        }
}

function Write-DiagnosticLog {
    param([int]$Lines = 20)

    if (-not $InklingLocalState) { Write-Output '  !! Inkling 套件沒註冊,讀不到 diagnostic.log'; return }
    $logPath = Join-Path $InklingLocalState 'diagnostic.log'
    $switchPath = Join-Path $InklingLocalState 'diagnostic.on'
    if (-not (Test-Path $switchPath)) {
        Write-Output "  !! 診斷日誌沒開。建一個空檔 $switchPath 再 Reload。"
    }
    if (-not (Test-Path $logPath)) { Write-Output '  (還沒有 diagnostic.log)'; return }

    # 一定要指定 UTF8:日誌是擴展用 UTF-8 寫的,不指定會照系統 ANSI 讀成亂碼。
    Get-Content $logPath -Tail $Lines -Encoding UTF8 | ForEach-Object { "    $_" }
}

function Write-SettingsState {
    Write-Output '  -- Inkling --'
    if (-not $InklingLocalState) {
        Write-Output '    !! Inkling 套件沒註冊,讀不到設定'
    } else {
        $inklingSettings = Join-Path $InklingLocalState 'settings.json'
        if (Test-Path $inklingSettings) {
            Get-Content $inklingSettings -Raw -Encoding UTF8 | ForEach-Object { "    $_" }
        } else {
            Write-Output '    (還沒有 settings.json —— 擴展從來沒存過設定)'
        }
    }

    Write-Output '  -- CmdPal(只挑跟 Inkling 有關的)--'
    $cmdPalSettings = Join-Path $CmdPalLocalState 'settings.json'
    if (-not (Test-Path $cmdPalSettings)) { Write-Output '    (找不到 CmdPal 的 settings.json)'; return }

    try {
        $json = Get-Content $cmdPalSettings -Raw -Encoding UTF8 | ConvertFrom-Json

        Write-Output '    aliases:'
        $json.Aliases.PSObject.Properties |
            Where-Object { $_.Value.CommandId -like 'Inkling*' } |
            ForEach-Object { "      '$($_.Name)' -> $($_.Value.CommandId)" }

        Write-Output '    provider settings:'
        $json.ProviderSettings.PSObject.Properties |
            Where-Object { $_.Name -like '*Inkling*' } |
            ForEach-Object {
                "      $($_.Name)"
                "        IsEnabled = $($_.Value.IsEnabled)"
                foreach ($fb in $_.Value.FallbackCommands.PSObject.Properties) {
                    "        fallback: $($fb.Name)"
                }
                foreach ($pin in $_.Value.PinnedCommandIds) {
                    "        pinned:   $pin"
                }
            }
    } catch {
        Write-Output "    !! 解析失敗:$($_.Exception.Message)"
    }
}

# ---------------------------------------------------------------- 主迴圈

# 這一輪有沒有真的送出過按鍵。重跑是把整串從頭再做一次 —— 已經送出的按鍵會再送一遍,
# 已經存檔的筆記會再存一則。擋不掉(腳本不知道哪一步有副作用),但至少要講出來。
$inputSent = $false
$lostFocus = $false

for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    if ($attempt -gt 1) {
        Write-Output "~~~ CmdPal 中途失焦,整串重跑(第 $attempt 次)~~~"
        if ($inputSent) {
            Write-Output '    !! 上一輪已經送出過按鍵 —— 重跑會再送一次,有副作用的步驟(存檔、刪除)會重複'
        }
        Start-Sleep -Milliseconds 800
    }

    $lostFocus = $false
    $inputSent = $false

    foreach ($step in ($Steps -split '\|')) {
        # 只切前導空白。**參數的尾隨空白不能動** —— alias 是「alias + 空白」才觸發的
        # (indirect alias 存的鍵就帶著那個空白),type:# 的尾隨空白 Trim 掉就等於
        # 送了一個永遠不會命中的查詢。
        $step = $step.TrimStart()
        if ($step.Trim() -eq '') { continue }

        $sep = $step.IndexOf(':')
        $verb = if ($sep -ge 0) { $step.Substring(0, $sep).Trim() } else { $step.Trim() }
        $arg = if ($sep -ge 0) { $step.Substring($sep + 1) } else { '' }

        Write-Output "### $verb $arg"

        <#
            **送出之前**先確認 CmdPal 在前景。

            SendInput 指定不了目標視窗,它送到的永遠是當下的前景視窗 —— CmdPal 不在前景
            就等於把這串字打進使用者正在用的別的視窗。這個腳本原本是先送再檢查,
            檢查只是事後報告,而且失焦會整串重跑最多 $Retries 次,同一串字會被打進
            錯的地方好幾遍。

            tree / shot 不送按鍵,但 CmdPal 不在前景時 UIA 只讀得到根節點、截圖也沒有意義
            (見 .claude/skills/verify-cmdpal-ui),所以一起擋。

            esc 是例外,見下面。
        #>
        if ($verb -in @('type', 'key', 'tree', 'shot')) {
            Assert-CmdPalFocus -Verb $verb
            if (-not $script:FocusOk) { $lostFocus = $true; break }
        }

        # esc 的目的就是「退出去」。CmdPal 已經不在前景 = 已經退出去了,這時候送 Escape
        # 只會打進別的視窗(關掉人家的對話框、取消人家編輯到一半的東西)。
        # 跳過就好,不算失焦 —— 算失焦會讓整串為了一個本來就達成的目的白白重跑。
        if ($verb -eq 'esc' -and -not (Test-CmdPalReady)) {
            Write-Output '  CmdPal 已經不在前景,esc 跳過(面板應該已經收起來了)'
            continue
        }

        switch ($verb) {
            'show' { Show-CmdPal; if (-not $script:FocusOk) { $lostFocus = $true } }
            'esc' { Send-Chord 'Escape'; $inputSent = $true; Start-Sleep -Milliseconds 500 }
            'type' { Send-Text $arg; $inputSent = $true; Start-Sleep -Milliseconds 500 }
            'key' { Send-Chord $arg; $inputSent = $true; Start-Sleep -Milliseconds 600 }
            'wait' { Start-Sleep -Milliseconds ([int]$arg.Trim()) }
            'tree' { Write-UiaTree -Depth $(if ($arg.Trim()) { [int]$arg } else { 14 }) -MaxText $MaxText }
            'shot' { Save-CmdPalScreenshot -Path $arg }
            'toast' { Write-ToastState }
            'notes' { Write-NotesFolder }
            'log' { Write-DiagnosticLog -Lines $(if ($arg.Trim()) { [int]$arg } else { 20 }) }
            'state' { Write-SettingsState }
            # 不認得就整串中止(throw 會讓腳本以非零結束),跟 key: 同一個理由:
            # 印個警告繼續跑的話,後面的步驟會落在沒預期的地方,那比直接失敗更難查。
            default { throw "不認得的動作:$verb(整串中止)" }
        }

        # show 失敗要在這裡收掉(它的結果寫在 $lostFocus,不是靠事後檢查)。
        if ($lostFocus) { break }
    }

    if (-not $lostFocus) { break }
}

# 全部重試都沒跑完就**以非零結束**。原本這裡什麼都不做,腳本照樣 exit 0 ——
# 呼叫端(人或 agent)會以為驗證通過了,而實際上整串根本沒跑完。
if ($lostFocus) {
    throw "跑了 $Retries 次都沒能讓 CmdPal 保持在前景,整串沒有完成(沒有任何按鍵被送到別的視窗)。" +
        "常見原因:有別的視窗一直在搶焦點(工作管理員、通知、另一個自動化腳本)," +
        "或 CmdPal 的熱鍵被改掉了。上面每一次失敗都印了當時的前景視窗是誰。"
}
