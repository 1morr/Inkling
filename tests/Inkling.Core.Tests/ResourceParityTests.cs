using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Inkling.Core.Tests;

/// <summary>
/// 三份 .resx 的一致性。
///
/// **為什麼這個測試在 Core 的測試專案裡** —— 那三個檔案屬於 <c>src/Inkling</c>(UI 層),
/// 而那一層沒有測試專案，因為它整個掛在 CmdPal 的型別上、跑不起來。但這裡完全不碰
/// 那些型別:.resx 就是 XML，拿 <see cref="XDocument"/> 讀就好。真正要擋的東西
/// (加了一條字串只翻了一半、佔位符數目對不上)也不需要載入組件才驗得到。
///
/// 擋的是三件事，每一件都真的會在正式版上壞掉，而且編譯器一句話都不會說:
///
/// 1. **key 少了一條** —— 那個語言的使用者會看到英文夾在中文裡。
/// 2. **佔位符對不上** —— <c>string.Format</c> 會丟 <c>FormatException</c>。
///    多一個 <c>{2}</c> 就足以讓刪除頁整個炸掉。
/// 3. **值是空的** —— 畫面上出現一列沒有標題的東西，看起來像壞掉。
/// 4. **中性那份自己的佔位符索引不連續** —— 第 2 點比的是「三份一不一致」,
///    三份一起寫成 <c>{0} … {2}</c>(缺 <c>{1}</c>)是一致的，測試全綠，而
///    <c>string.Format</c> 在執行期照樣丟 <c>FormatException</c>。
/// </summary>
public class ResourceParityTests
{
    private const string NeutralFileName = "Resources.resx";

    /// <summary>翻譯的那幾份。檔名裡的文化名就是 .NET 認的那個。</summary>
    public static TheoryData<string> TranslatedFileNames =>
        new() { "Resources.zh-Hant.resx", "Resources.zh-Hans.resx" };

    [Theory]
    [MemberData(nameof(TranslatedFileNames))]
    public void Translation_HasExactlyTheSameKeys(string fileName)
    {
        var neutral = Load(NeutralFileName);
        var translated = Load(fileName);

        // 兩個方向都要查:少了一條會退回英文，多了一條代表中性那份漏了
        // (而中性是唯一有註解、翻譯要照著看的那一份)。
        Assert.Empty(neutral.Keys.Except(translated.Keys, StringComparer.Ordinal));
        Assert.Empty(translated.Keys.Except(neutral.Keys, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TranslatedFileNames))]
    public void Translation_UsesTheSamePlaceholders(string fileName)
    {
        var neutral = Load(NeutralFileName);
        var translated = Load(fileName);

        foreach (var (key, value) in neutral)
        {
            // 比對的是「用到哪幾個索引」，不是出現幾次:QuickCaptureHint 那條
            // 中文裡 {0} 出現兩次、英文也是兩次，但別的語言把它寫成一次也還是對的。
            var expected = Placeholders(value);
            var actual = Placeholders(translated[key]);

            Assert.True(
                expected.SetEquals(actual),
                FormattableString.Invariant(
                    $"{fileName} 的 {key} 佔位符對不上:中性是 {Describe(expected)}，這裡是 {Describe(actual)}"));
        }
    }

    /// <summary>
    /// 中性那份每條字串用到的佔位符索引必須是 <c>0..n-1</c> 連續的。
    ///
    /// 上面那條比的是「翻譯跟中性一不一致」，擋不到這個:中性自己寫成
    /// <c>{0} … {2}</c>、翻譯照抄，三份完全一致 —— 而 <c>string.Format</c> 拿到兩個引數
    /// 配一個 <c>{2}</c> 就丟 <c>FormatException</c>，那一頁整個炸掉。
    /// 只驗中性那一份就夠:翻譯跟它一致是上一條的責任。
    /// </summary>
    [Fact]
    public void Neutral_PlaceholderIndexesAreContiguous()
    {
        foreach (var (key, value) in Load(NeutralFileName))
        {
            var used = Placeholders(value);
            if (used.Count == 0)
            {
                continue;
            }

            var expected = Enumerable.Range(0, used.Max() + 1).ToHashSet();

            Assert.True(
                used.SetEquals(expected),
                FormattableString.Invariant(
                    $"{NeutralFileName} 的 {key} 佔位符索引不連續:用了 {Describe(used)}，最大的是 {Describe([used.Max()])}，那就必須把 {Describe(expected)} 都用上"));
        }
    }

    [Theory]
    [MemberData(nameof(TranslatedFileNames))]
    public void Translation_HasNoEmptyValues(string fileName) => AssertNoEmptyValues(fileName);

    [Fact]
    public void Neutral_HasNoEmptyValues() => AssertNoEmptyValues(NeutralFileName);

    /// <summary>
    /// 中性那一份是英文。這條擋的是「照著繁中改了英文那一份」——
    /// 中性同時也是所有非中文使用者看到的東西，混進中文不會有人回報。
    /// 表意文字之外，中文標點(。、「」)與全形英數混進去一樣是漏翻。
    /// </summary>
    [Fact]
    public void Neutral_IsNotChinese()
    {
        foreach (var (key, value) in Load(NeutralFileName))
        {
            if (!MayContainCjkCharacters.Contains(key))
            {
                Assert.DoesNotMatch(
                    @"[\p{IsCJKUnifiedIdeographs}\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}]",
                    value);
            }

            Assert.NotEqual(string.Empty, key);
        }
    }

    /// <summary>
    /// 翻譯那份不能整份留英文 —— 其他檢查只看 key 與佔位符，一條都沒翻也過得了，
    /// 而那正是這份清單要擋的「只翻一半」的極端形狀。
    /// 不要求每條都含中文(快速鍵組合、專有名詞可以留英文)，但多數該含。
    /// </summary>
    [Theory]
    [MemberData(nameof(TranslatedFileNames))]
    public void Translation_IsActuallyTranslated(string fileName)
    {
        var values = Load(fileName).Values;
        var withCjk = values.Count(v => Regex.IsMatch(v, @"\p{IsCJKUnifiedIdeographs}"));

        Assert.True(
            withCjk * 2 >= values.Count,
            FormattableString.Invariant($"{fileName} 只有 {withCjk}/{values.Count} 條含中文，疑似整份沒翻"));
    }

    /// <summary>
    /// 合理含有全形 / CJK 字元的 key:SettingSeparatorDescription 整條就是在向使用者
    /// 解釋「全形與半形分隔符視為同一個」，例子非用全形字元不可。
    /// </summary>
    private static readonly HashSet<string> MayContainCjkCharacters = new(StringComparer.Ordinal)
    {
        "SettingSeparatorDescription",
    };

    private static void AssertNoEmptyValues(string fileName)
    {
        foreach (var (key, value) in Load(fileName))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(value),
                FormattableString.Invariant($"{fileName} 的 {key} 是空的"));
        }
    }

    /// <summary>字串裡用到的佔位符索引，例如 <c>"{0} / {2}"</c> 得到 <c>{0, 2}</c>。</summary>
    private static HashSet<int> Placeholders(string value) =>
        [.. Regex.Matches(value, @"\{(\d+)(?::[^}]*)?\}")
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))];

    private static string Describe(HashSet<int> placeholders) =>
        placeholders.Count == 0
            ? "(沒有)"
            : string.Join(", ", placeholders.Order().Select(i => FormattableString.Invariant($"{{{i}}}")));

    private static Dictionary<string, string> Load(string fileName)
    {
        var path = Path.Combine(ResourcesDirectory(), fileName);
        Assert.True(File.Exists(path), FormattableString.Invariant($"找不到資源檔:{path}"));

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// 從**這個原始碼檔案的路徑**回推到資源檔資料夾，而不是從組件的位置往上爬 ——
    /// bin 的層數會跟著組態與 RID 變，爬幾層是猜的;原始碼的相對位置則是固定的。
    /// </summary>
    private static string ResourcesDirectory([CallerFilePath] string callerPath = "")
    {
        // tests/Inkling.Core.Tests/ResourceParityTests.cs -> repo 根目錄
        var repositoryRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerPath)!, "..", ".."));

        return Path.Combine(repositoryRoot, "src", "Inkling", "Properties");
    }
}
