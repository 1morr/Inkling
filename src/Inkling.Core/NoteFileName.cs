using System.Globalization;
using System.Text;

namespace Inkling.Core;

/// <summary>
/// 筆記的身分與檔名產生。
/// </summary>
public static class NoteFileName
{
    /// <summary>slug 的字元上限。夠長到看得懂,又不會把路徑撐爆。</summary>
    private const int MaxSlugLength = 40;

    private const string FallbackSlug = "note";

    public const string Extension = ".md";

    /// <summary>
    /// 產生筆記身分,格式 <c>yyyyMMdd-HHmmss-xxxx</c>。
    /// 後綴是隨機的:同一秒內連續記兩則想法並非罕見,光靠時間戳會撞。
    /// </summary>
    public static string CreateId(DateTimeOffset timestamp)
    {
        var suffix = Random.Shared.Next(0x10000).ToString("x4", CultureInfo.InvariantCulture);
        return $"{timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{suffix}";
    }

    /// <summary>
    /// 把標題轉成檔名安全的 slug。只保留字母與數字(Unicode 分類,所以中日韓照樣留著),
    /// 其餘一律變成連字號。這順帶也擋掉了 Windows 的非法檔名字元。
    ///
    /// **逐 Rune 走,不是逐 char。** 逐 char 的話代理對的兩半各自都不是字母
    /// (<c>char.IsLetterOrDigit</c> 對單一代理字元一律回 false),於是 BMP 以外的漢字
    /// —— 擴充區 B 之後,人名用字與異體字大量落在那裡 —— 會整串變成連字號,
    /// 上面那句「中日韓照樣留著」對它們並不成立。順帶讓底下截斷處的代理對保護
    /// 真的執行得到:逐 char 的版本永遠留不下代理字元,那段是不可能走到的死碼。
    /// </summary>
    public static string Slug(string title)
    {
        ArgumentNullException.ThrowIfNull(title);

        var builder = new StringBuilder(title.Length);
        var lastWasSeparator = false;

        // 一個 Rune 最多兩個 UTF-16 字元。
        Span<char> utf16 = stackalloc char[2];

        foreach (var rune in title.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(utf16[..rune.EncodeToUtf16(utf16)]);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && builder.Length > 0)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxSlugLength)
        {
            var cut = MaxSlugLength;

            // 別把代理對切成兩半,不然會生出無效的 UTF-16。
            // (上限算的是 UTF-16 字元數,不是 Rune 數 —— 那才是路徑長度真正在乎的東西。)
            if (char.IsHighSurrogate(slug[cut - 1]))
            {
                cut--;
            }

            slug = slug[..cut].TrimEnd('-');
        }

        return slug.Length == 0 ? FallbackSlug : slug;
    }

    /// <summary>
    /// 組出一個在 <paramref name="directory"/> 裡還沒被用掉的檔名。
    ///
    /// 檔名只是給人看的:身分是 front matter 裡的 id,所以之後改標題不會、也不該重新命名檔案
    /// —— 在雲端同步資料夾裡頻繁 rename 是產生重複檔與衝突檔的頭號原因。
    /// </summary>
    public static string CreateUniquePath(string directory, DateTimeOffset timestamp, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var prefix = timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var slug = Slug(title);
        var baseName = $"{prefix}-{slug}";

        var candidate = Path.Combine(directory, baseName + Extension);
        var counter = 2;

        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{baseName}-{counter.ToString(CultureInfo.InvariantCulture)}{Extension}");
            counter++;
        }

        return candidate;
    }
}
