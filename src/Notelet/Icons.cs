using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Notelet;

/// <summary>
/// Segoe Fluent Icons 的字符。
///
/// 刻意寫成 \uXXXX 逸出而不是字面字元:這些碼位落在 Unicode 私用區,
/// 直接貼字元進原始碼的話,在編輯器、git diff、code review 裡通通顯示成空白方塊,
/// 根本看不出改了什麼。集中在這裡也讓呼叫端只出現 Icons.Note 這種有名字的東西。
///
/// 對照表:https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font
/// </summary>
internal static class Icons
{
    /// <summary>QuickNote — 擴展與清單頁。</summary>
    public static IconInfo Note => new("\uE70B");

    /// <summary>Add — 新增筆記。</summary>
    public static IconInfo Add => new("\uE710");

    /// <summary>Edit — 編輯筆記。</summary>
    public static IconInfo Edit => new("\uE70F");

    /// <summary>Page — Markdown 預覽。</summary>
    public static IconInfo Preview => new("\uE7C3");

    /// <summary>OpenWith — 在外部編輯器開啟。</summary>
    public static IconInfo OpenExternal => new("\uE7AC");

    /// <summary>Copy — 複製內文。</summary>
    public static IconInfo Copy => new("\uE8C8");
}