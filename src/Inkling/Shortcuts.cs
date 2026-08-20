using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace Inkling;

/// <summary>
/// 清單頁與預覽頁上那些命令的快速鍵,集中在這一個檔案裡。
///
/// <b>能少一個修飾鍵就少一個</b> —— 這幾個動作每天按,`Ctrl+X` 比 `Ctrl+Shift+X` 順得多。
/// 但「哪些 <c>Ctrl+字母</c> 可以拿」不是隨便挑的,先看清楚誰已經佔著:
///
/// <para><b>一、搜尋框(WinUI <c>TextBox</c>)的標準編輯鍵,一個都不能碰。</b></para>
///
/// 清單頁的焦點永遠在搜尋框上,而 CmdPal 在 <c>ShellPage_OnPreviewKeyDown</c> 就把鍵
/// 送去比對快速鍵(<c>TryCommandKeybindingMessage</c> → <c>CheckKeybinding</c>)——
/// 那是 <b>tunneling</b> 階段,比 <c>TextBox</c> 早。綁走就等於從搜尋框拿掉:
/// <c>Ctrl+A</c>(全選)、<c>Ctrl+C</c> / <c>Ctrl+X</c> / <c>Ctrl+V</c>、
/// <c>Ctrl+Z</c> / <c>Ctrl+Y</c>(復原/重做)、<c>Ctrl+Backspace</c> / <c>Ctrl+Delete</c>
/// (刪一個詞)、<c>Ctrl+方向鍵</c> / <c>Home</c> / <c>End</c>。
///
/// <para><b>二、CmdPal 自己佔掉的。</b></para>
///
/// <c>Ctrl+K</c>(選單)、<c>Ctrl+Enter</c>(次要命令)、<c>Ctrl+,</c>(設定,在
/// <c>ShellPage_OnPreviewKeyDown</c> 的 switch 裡)、<c>Ctrl+I</c>(它自己攔下來的,
/// 註解寫著「TextBox 會插入 tab,所以壓掉留給別的用途」)。<c>Alt+Left</c> /
/// <c>Alt+Home</c> / <c>Alt+F</c> 也是它的,但那些不是我們的字母鍵。
///
/// <para><b>三、剩下的才是我們的。</b></para>
///
/// | 動作 | 鍵位 | 為什麼是它 |
/// |---|---|---|
/// | 編輯 | <c>Ctrl+E</c> | E = Edit |
/// | 新增筆記 | <c>Ctrl+N</c> | N = New,而且是各家編輯器共通的手勢 |
/// | 原始文字 | <c>Ctrl+U</c> | 見 README〈原始文字模式〉 |
/// | 在預設編輯器開啟 | <c>Ctrl+O</c> | O = Open,剪貼簿記錄擴展的 <c>KeyChords.OpenUrl</c> 也是它 |
/// | 開啟檔案位置 | <c>Ctrl+L</c> | L = Location。CmdPal 的慣例是 <c>Ctrl+Shift+E</c>
///     (<c>WellKnownKeyChords.OpenFileLocation</c>),這裡刻意讓位給少一個鍵 |
/// | 複製內文 | <c>Ctrl+Shift+C</c> | <b>唯一還帶 Shift 的</b>,見下面 |
/// | 刪除 | <c>Ctrl+D</c> | D = Delete。`Delete` 系列全是搜尋框的編輯鍵,碰不得 |
///
/// <para><b>複製為什麼留著 Shift。</b></para>
///
/// <c>Ctrl+C</c> 拿不得(搜尋框要拿它複製使用者剛打的字),所以複製只剩兩條路:
/// 借一個沒人要的字母(<c>B</c> = Body 試過一版),或是照 CmdPal 的慣例走
/// <c>Ctrl+Shift+C</c>(<c>WellKnownKeyChords.CopyFilePath</c>)。**選了後者**:
/// 那組鍵跟「複製」的關聯是手指本來就記得的,借來的字母得靠死記,
/// 省下的那一個 Shift 換不到。
///
/// 真要換成單一個 <c>Ctrl</c>,B / G / M / R / T 都還空著,改這一行就行。
/// <c>Ctrl+Insert</c>(Windows 的老牌複製鍵)則刻意不碰:沒查證到 WinUI 的
/// <c>TextBox</c> 吃不吃它,吃的話就等於又拿走搜尋框的一個複製鍵;
/// 而且筆電鍵盤上的 <c>Insert</c> 常常要配 <c>Fn</c>。
///
/// <para><b><c>Ctrl+D</c> 的代價要認。</b></para>
///
/// 它比 <c>Ctrl+Shift+Delete</c>(CmdPal 三個內建擴展的刪除鍵)好按,也就更容易誤按 ——
/// 這正是它上一次被整個拿掉的理由。現在換回來是使用者的決定,防線有兩道而且都還在:
/// <b>一定會跳確認框</b>,而且刪掉的檔案<b>進資源回收筒</b>。
///
/// <para><b>撞鍵不會報錯。</b></para>
///
/// 同一個項目的選單裡兩個命令掛同一個鍵時,CmdPal 用 <c>TryAdd</c>,第二個被靜靜丟掉
/// (只在它自己的 log 留一行 warning,我們看不到)。加新鍵位時自己對一遍上面那張表。
/// </summary>
internal static class Shortcuts
{
    /// <summary>編輯筆記(表單)。</summary>
    public static KeyChord Edit { get; } = Ctrl(VirtualKey.E);

    /// <summary>
    /// 開新增筆記的表單頁。清單頁專用 —— 它是這一組裡唯一跟「選中的那一則」無關的動作,
    /// 掛在每一則筆記的選單上只是因為 <b>CmdPal 的快速鍵只能掛在項目的命令上</b>
    /// (<c>CommandBarViewModel.CheckKeybinding</c> 比對的是當下選中項的那幾個命令),
    /// 頁面層級沒有掛鍵的地方。
    ///
    /// <c>Ctrl+N</c> 是安全的:<c>TextBox</c> 沒有拿它做編輯動作,CmdPal 自己的
    /// <c>ShellPage_OnPreviewKeyDown</c> 與所有 XAML 的 <c>KeyboardAccelerator</c> 裡
    /// 也都沒有 <c>VirtualKey.N</c>(對 PowerToys <c>main</c> 全文查過)。
    /// </summary>
    public static KeyChord NewNote { get; } = Ctrl(VirtualKey.N);

    /// <summary>詳細窗格在渲染與原始 Markdown 之間切換。</summary>
    public static KeyChord ToggleSource { get; } = Ctrl(VirtualKey.U);

    /// <summary>用系統預設的程式開啟這個 <c>.md</c>。</summary>
    public static KeyChord OpenExternal { get; } = Ctrl(VirtualKey.O);

    /// <summary>複製筆記內文(不含 front matter)。</summary>
    public static KeyChord CopyBody { get; } = CtrlShift(VirtualKey.C);

    /// <summary>在檔案總管裡開啟所在資料夾,並選中這個檔案。</summary>
    public static KeyChord OpenFileLocation { get; } = Ctrl(VirtualKey.L);

    /// <summary>刪除筆記(移到資源回收筒),按下去會先跳確認框。</summary>
    public static KeyChord Delete { get; } = Ctrl(VirtualKey.D);

    private static KeyChord Ctrl(VirtualKey vkey) => KeyChordHelpers.FromModifiers(
        ctrl: true, alt: false, shift: false, win: false, vkey: vkey, scanCode: 0);

    private static KeyChord CtrlShift(VirtualKey vkey) => KeyChordHelpers.FromModifiers(
        ctrl: true, alt: false, shift: true, win: false, vkey: vkey, scanCode: 0);
}
