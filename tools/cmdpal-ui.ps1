<#
.SYNOPSIS
    驅動 Command Palette 的 UI,做 Notelet 的實機驗證。

.DESCRIPTION
    CmdPal 沒有 UI 自動化介面,擴展又跑在獨立的 COM 進程裡 —— 這個腳本用 Windows
    內建的 UI Automation 讀它的畫面,用 SendInput 打字與按鍵,補上
    docs\manual-test-checklist.md 裡那些「只能靠眼睛」的項目。

    **一整串動作一定要在同一次呼叫裡跑完。** CmdPal 一失焦就自我隱藏,每啟動一個新的
    PowerShell 進程都可能把它打斷 —— 那正是要用 -Steps "a|b|c" 而不是連續呼叫三次的原因。

    要用 pwsh(PowerShell 7)跑。這個檔案是無 BOM 的 UTF-8,Windows PowerShell 5.1
    會照系統 ANSI 讀,中文全部變亂碼。

.PARAMETER Steps
    用 | 串起來的動作序列。動作與參數之間用第一個 : 分開:

      show            叫出 CmdPal(熱鍵從 CmdPal 自己的 settings.json 讀,不寫死)
      esc             送 Esc(退一層頁面;在主頁等於關掉面板)
      type:<文字>     打字,走 Unicode 注入,中文與全形符號都可以
      key:<組合>      按鍵,例如 key:Enter / key:Ctrl+D / key:Ctrl+Shift+C
      wait:<毫秒>     等待
      tree[:<深度>]   dump UI Automation 樹(預設深度 14)
      shot:<路徑>     截圖(PrintWindow,不受遮擋影響)
      toast           列出 CmdPal 的 toast 視窗狀態 —— 驗證「一個 toast 都不發」
      notes           列出目前設定的筆記資料夾內容
      log[:<行數>]    diagnostic.log 的尾巴(預設 20 行)
      state           兩份 settings.json 的摘要(Notelet 自己的 + CmdPal 那邊的)

.PARAMETER Retries
    整串動作在 CmdPal 中途失焦時重跑幾次。預設 4。

.EXAMPLE
    pwsh -NoProfile -File tools\cmdpal-ui.ps1 -Steps "show|type:Notelet|wait:800|tree:6"

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

# ---------------------------------------------------------------- 路徑與常數

$CmdPalLocalState = Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.CommandPalette_8wekyb3d8bbwe\LocalState'
$NoteletLocalState = Join-Path $env:LOCALAPPDATA 'Packages\Notelet_bf0n0751x5hse\LocalState'

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

    不能用 (Get-Process ...).MainWindowHandle —— CmdPal 是 WinUI 3 應用,那個屬性
    永遠是 0,連視窗開著的時候也是。同樣的原因,orca computer list-apps 整個看不到
    CmdPal,--app pid:<CmdPal> 會回 app_not_found。
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

function Test-CmdPalForeground {
    $hwnd = Find-CmdPalWindow -VisibleOnly
    if ($hwnd -eq [IntPtr]::Zero) { return $false }
    return ([CmdPalNative]::GetForegroundWindow() -eq $hwnd)
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
                default { Write-Output "  !! 不認得的修飾鍵:$p" }
            }
        }
    }
    if (-not $VirtualKeys.ContainsKey($main)) {
        Write-Output "  !! 不認得的按鍵:$main"
        return
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
    [CmdPalNative]::Send($seq)
    Start-Sleep -Milliseconds 900

    # 熱鍵可能剛把 CmdPal 拉起來(PowerToys 重啟之後第一次就是這樣),那時候快取住的
    # pid 還是 0 —— 不清掉的話後面每一步都會說「視窗不可見」。
    $script:CmdPalPid = $null
    if (-not (Get-CmdPalPid)) {
        Start-Sleep -Milliseconds 1500
        $script:CmdPalPid = $null
    }

    $hwnd = Find-CmdPalWindow -VisibleOnly
    $names = $modifiers | ForEach-Object {
        switch ($_) { 0x5B { 'Win' } 0x11 { 'Ctrl' } 0x12 { 'Alt' } 0x10 { 'Shift' } }
    }
    $chord = (@($names) + @('0x{0:X2}' -f $hotkey.code)) -join '+'
    Write-Output "  熱鍵=$chord HWND=$hwnd"
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
    看 toast 視窗在不在。

    README〈刪除成功時一個 toast 都不發〉那條規矩就是靠這個驗的:CmdPal 的 toast 是
    **另一個頂層視窗**,它一出現就搶焦點,而主視窗一失焦就自我隱藏 —— 也就是
    「做完之後整個面板消失」的成因。要驗證某條路徑沒有發 toast,就在那個動作之後
    立刻跑這個。
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
    if ($visible) {
        Write-Output '  !! 有 toast 跳出來 —— 主面板會跟著消失,見 README〈刪除成功時一個 toast 都不發〉'
    }
}

function Get-NotesDirectory {
    $settingsPath = Join-Path $NoteletLocalState 'settings.json'
    if (-not (Test-Path $settingsPath)) { return $null }
    try {
        $json = Get-Content $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        return $json.'Notelet.NotesDirectory'
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

    $logPath = Join-Path $NoteletLocalState 'diagnostic.log'
    $switchPath = Join-Path $NoteletLocalState 'diagnostic.on'
    if (-not (Test-Path $switchPath)) {
        Write-Output "  !! 診斷日誌沒開。建一個空檔 $switchPath 再 Reload。"
    }
    if (-not (Test-Path $logPath)) { Write-Output '  (還沒有 diagnostic.log)'; return }

    # 一定要指定 UTF8:日誌是擴展用 UTF-8 寫的,不指定會照系統 ANSI 讀成亂碼。
    Get-Content $logPath -Tail $Lines -Encoding UTF8 | ForEach-Object { "    $_" }
}

function Write-SettingsState {
    $noteletSettings = Join-Path $NoteletLocalState 'settings.json'
    Write-Output '  -- Notelet --'
    if (Test-Path $noteletSettings) {
        Get-Content $noteletSettings -Raw -Encoding UTF8 | ForEach-Object { "    $_" }
    } else {
        Write-Output '    (還沒有 settings.json —— 擴展從來沒存過設定)'
    }

    Write-Output '  -- CmdPal(只挑跟 Notelet 有關的)--'
    $cmdPalSettings = Join-Path $CmdPalLocalState 'settings.json'
    if (-not (Test-Path $cmdPalSettings)) { Write-Output '    (找不到 CmdPal 的 settings.json)'; return }

    try {
        $json = Get-Content $cmdPalSettings -Raw -Encoding UTF8 | ConvertFrom-Json

        Write-Output '    aliases:'
        $json.Aliases.PSObject.Properties |
            Where-Object { $_.Value.CommandId -like 'Notelet*' } |
            ForEach-Object { "      '$($_.Name)' -> $($_.Value.CommandId)" }

        Write-Output '    provider settings:'
        $json.ProviderSettings.PSObject.Properties |
            Where-Object { $_.Name -like '*Notelet*' } |
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

for ($attempt = 1; $attempt -le $Retries; $attempt++) {
    if ($attempt -gt 1) {
        Write-Output "~~~ CmdPal 中途失焦,整串重跑(第 $attempt 次)~~~"
        Start-Sleep -Milliseconds 800
    }

    $lostFocus = $false

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

        switch ($verb) {
            'show' { Show-CmdPal }
            'esc' { Send-Chord 'Escape'; Start-Sleep -Milliseconds 500 }
            'type' { Send-Text $arg; Start-Sleep -Milliseconds 500 }
            'key' { Send-Chord $arg; Start-Sleep -Milliseconds 600 }
            'wait' { Start-Sleep -Milliseconds ([int]$arg.Trim()) }
            'tree' { Write-UiaTree -Depth $(if ($arg.Trim()) { [int]$arg } else { 14 }) -MaxText $MaxText }
            'shot' { Save-CmdPalScreenshot -Path $arg }
            'toast' { Write-ToastState }
            'notes' { Write-NotesFolder }
            'log' { Write-DiagnosticLog -Lines $(if ($arg.Trim()) { [int]$arg } else { 20 }) }
            'state' { Write-SettingsState }
            default { Write-Output "  !! 不認得的動作:$verb" }
        }

        # 只有「還需要 CmdPal 在畫面上」的動作才檢查焦點 —— notes / log / state 純粹
        # 讀檔案,esc 本來就可能把面板關掉。
        $needsWindow = ($verb -in @('type', 'key', 'tree', 'shot'))
        if ($needsWindow -and -not (Test-CmdPalForeground)) {
            Write-Output "  !! CmdPal 在 '$verb' 這一步失去焦點"
            $lostFocus = $true
            break
        }
    }

    if (-not $lostFocus) { break }
}
