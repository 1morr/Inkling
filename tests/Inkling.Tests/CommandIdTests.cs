using Xunit;

namespace Inkling.Tests;

/// <summary>
/// 頂層命令的 Id 是**對外承諾**，跟資料格式同一個等級。
///
/// CmdPal 拿它當鍵存使用者設過的 alias、全域快速鍵、釘選與 fallback 規則
/// (見 <see cref="CommandIds"/>)。改一個字，那些設定全部靜靜失效，而且
/// **沒有任何一步會報錯** —— 編譯過、測試綠、部署成功，只有使用者發現他的 <c>!</c> 不見了。
///
/// 這一組測試在此之前不存在:<c>grep -rn "Notelet\.\|CommandIds" tests/</c> 零命中，
/// 唯一的閘門是發版流程第 0 步那個人工 <c>git diff</c>。
///
/// <b>釘住的基準是 <c>Inkling.*</c></b>，那是 2026-08-22 在第一個公開版本之前**刻意**
/// 換過去的(前綴原本是改名前的 <c>Notelet.</c>，見 <see cref="CommandIds"/>)。
/// 那次連 alias、釘選與啟用狀態一起重設，代價付得起是因為安裝基數只有作者一台機器。
/// **從第一個公開版本起就沒有下一次了**，所以這裡逐字比對:之後任何一個字的漂移
/// 都會在這裡紅掉，而不是在使用者的機器上。
/// </summary>
public class CommandIdTests
{
    [Theory]
    [InlineData("Inkling", nameof(CommandIds.Provider))]
    [InlineData("Inkling.List", nameof(CommandIds.List))]
    [InlineData("Inkling.NewNote", nameof(CommandIds.NewNote))]
    [InlineData("Inkling.QuickCapture", nameof(CommandIds.QuickCapture))]
    [InlineData("Inkling.QuickCapturePage", nameof(CommandIds.QuickCapturePage))]
    [InlineData("Inkling.DeleteAll", nameof(CommandIds.DeleteAll))]
    [InlineData("Inkling.Scratchpad", nameof(CommandIds.Scratchpad))]
    public void IdsAreFrozen(string expected, string member)
    {
        var actual = typeof(CommandIds)
            .GetField(member)
            ?.GetRawConstantValue() as string;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void EveryIdIsDistinct()
    {
        // 同一個 Id 掛在兩個命令上時，CmdPal 存的設定會互相蓋掉 ——
        // QuickCapture 與 QuickCapturePage 分開就是為了這件事。
        var ids = typeof(CommandIds)
            .GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheFrozenListCoversEveryConstant()
    {
        // 加了新的 Id 卻沒有進上面那張表的話，它就沒有被釘住 —— 這一條會紅。
        var declared = typeof(CommandIds)
            .GetFields()
            .Count(f => f.IsLiteral && f.FieldType == typeof(string));

        Assert.Equal(7, declared);
    }
}
