namespace Notelet.Core;

/// <summary>
/// 筆記的過濾與排序。純函式,沒有狀態,所以測試很直接。
/// </summary>
public static class NoteSearch
{
    // 分數只用來排序,絕對值沒有意義,拉開級距是為了讓標題命中永遠壓過內文命中。
    private const int TitlePrefixScore = 1000;
    private const int TitleContainsScore = 100;
    private const int BodyContainsScore = 10;

    private static readonly char[] TermSeparators = [' ', '\t', '　'];

    /// <summary>
    /// 依關鍵字過濾。多個詞之間是 AND:每個詞都必須在標題或內文裡出現。
    /// 空字串代表全部。結果依相關度遞減,同分則依最後更新時間遞減。
    /// </summary>
    public static IReadOnlyList<Note> Filter(IReadOnlyList<Note> notes, string query)
    {
        ArgumentNullException.ThrowIfNull(notes);

        var terms = (query ?? string.Empty).Split(TermSeparators, StringSplitOptions.RemoveEmptyEntries);

        if (terms.Length == 0)
        {
            return [.. notes.OrderByDescending(n => n.Updated)];
        }

        var scored = new List<(Note Note, int Score)>();

        foreach (var note in notes)
        {
            var score = Score(note, terms);
            if (score > 0)
            {
                scored.Add((note, score));
            }
        }

        return [.. scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Note.Updated)
            .Select(x => x.Note)];
    }

    /// <summary>回傳 0 代表有某個詞完全沒命中,這則筆記就不該出現。</summary>
    private static int Score(Note note, string[] terms)
    {
        var total = 0;

        foreach (var term in terms)
        {
            var termScore = 0;

            if (note.Title.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = TitlePrefixScore;
            }
            else if (note.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = TitleContainsScore;
            }
            else if (note.Body.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                termScore = BodyContainsScore;
            }

            if (termScore == 0)
            {
                return 0;
            }

            total += termScore;
        }

        return total;
    }
}
