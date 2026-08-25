using System.Globalization;

namespace Inkling;

/// <summary>
/// 介面字串的格式化。字串本身在 <see cref="Properties.Resources"/>(三份 .resx),
/// 這裡只負責把佔位符填起來。
///
/// 為什麼要包一層:<c>string.Format</c> 不給 <see cref="IFormatProvider"/> 的多載
/// 會被 CA1305 擋下來(這個 repo 全域開著 <c>TreatWarningsAsErrors</c>)，而每個呼叫點
/// 都寫一次 <c>CultureInfo.CurrentCulture</c> 只是噪音。
///
/// 用 <see cref="CultureInfo.CurrentCulture"/> 而不是 <c>CurrentUICulture</c>:
/// 前者管的是數字與日期的格式(使用者的地區設定)，後者管的是拿哪個語言的字串
/// (資源查找，由 <c>ResourceManager</c> 自己處理)。兩者在 Windows 上可以不一樣 ——
/// 介面是英文、日期照台灣格式，是合法的組合。
///
/// **寫進檔案的東西不能走這裡。** 檔名的時間戳、front matter 的日期一律
/// <see cref="CultureInfo.InvariantCulture"/>，那些是資料格式，不是給人看的字。
/// 那條線在 Inkling.Core 裡，那一層完全不碰這個類別。
/// </summary>
internal static class Strings
{
    /// <param name="format">來自 <see cref="Properties.Resources"/> 的格式字串。</param>
    /// <param name="arguments">要填進去的值。</param>
    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);
}
