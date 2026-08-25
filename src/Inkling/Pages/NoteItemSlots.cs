using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;

namespace Inkling.Pages;

/// <summary>
/// 把「這一輪要列出來的筆記」對應到「上一輪已經在用的 <see cref="ListItem"/> 物件」。
///
/// <para><b>為什麼需要這個東西:CmdPal 的選取跟著物件識別走。</b></para>
///
/// CmdPal 收到 <c>ItemsChanged</c> 之後會重新 <c>GetItems()</c>，再拿回來的每一個
/// <c>IListItem</c> 去查它自己的 view model 快取(<c>ListViewModel._vmCache</c>,
/// 比較器是 <c>ProxyReferenceEqualityComparer</c> —— **參考相等**)，然後用
/// <c>ListHelpers.InPlaceUpdateList</c> 去 diff 那份集合。最後
/// <c>ListItemsView.TrySetSelectionAfterUpdate</c> 只問一句:當下選中的那個 view model
/// **還在不在新的集合裡**?在就什麼都不做，不在就選第一列。
///
/// 也就是說，每次重建清單都 <c>new</c> 一批全新的 <see cref="ListItem"/>，等於告訴 CmdPal
/// 「整份清單都換人了」—— 選取必然回到第一列。**這才是「刪一則筆記，選取跳回最上面」
/// 的真正成因**，跟刪除本身無關:別台機器同步下來一則、或使用者拿別的編輯器改了任何一則，
/// 一樣會把選取踢走(真機實測過)。
///
/// <para><b>三條規則。第三條是拿命換來的，別拆。</b></para>
///
/// <list type="number">
/// <item><b>還在、而且內容一個字都沒變的筆記 → 沿用自己的槽，而且完全不碰它。</b>
/// 呼叫端的 <c>rebind</c> 不會被呼叫。新增、刪除別則、重新排序時，使用者選著的那一列
/// 因此原地不動 —— 選取跟著**筆記**走。</item>
/// <item><b>從清單上消失的筆記 → 把自己的槽讓給後繼者</b>(上一輪排在它後面、這一輪還在的
/// 第一則;只少一則時才允許往前找，見 <see cref="Successor"/>)。後繼者原本那個槽變成孤兒。
/// 這一條讓「刪掉選中那一列」之後選取留在**原位置**、顯示下一則，跟檔案總管同一個手感。
/// 換人坐了，所以 <c>rebind</c> 一定會被呼叫。</item>
/// <item><b>還在、但內容變了的筆記 → 給它一個全新的槽</b>(<c>create</c>)，舊的丟掉。</item>
/// </list>
///
/// <para><b>第三條為什麼不是「沿用舊槽、就地把新內容寫進去」。</b></para>
///
/// 因為那樣會**打壞使用者當下正在看的畫面**。就地改一個 CmdPal 已經建好 view model 的
/// 清單項(<c>Command</c> / <c>MoreCommands</c> / <c>Details</c> 任一個都算),CmdPal 會
/// 立刻把那一列重新渲染出來 —— 而使用者這時候多半**不在清單頁上**:內容會變，
/// 最常見的原因就是他正在編輯那一則。實測到的畫面是編輯表單旁邊多出一塊筆記預覽、
/// 底部工具列變成清單那一列的「預覽 / 編輯」，而人明明還在編輯頁。
/// (三個屬性一個一個關掉測出來的，而且**跟更新的時機無關** ——
/// 先自己重建再通知 CmdPal 也一樣。)
///
/// 代價很誠實:剛編輯過的那一則會失去物件識別，回到清單頁時選取可能不在它身上。
/// 那是這個類別出現**之前**的既有行為，不是新的退步;而換來的是「內容沒變的列
/// 一個屬性都不設」，連跨進程通知都省下來了。
///
/// <para><b>身分認的是 <see cref="Note.FilePath"/>，不是 <c>Id</c>。</b></para>
///
/// 雲端硬碟的衝突副本是整檔複製，同一個 <c>id</c> 會出現在兩個檔案上 —— 拿 id 當鍵會讓
/// 兩列搶同一個槽。這跟 <c>Update</c> / <c>Delete</c> 認路徑是同一條理由，
/// 見 CLAUDE.md〈解析一則筆記認的是路徑，不是 id〉。
///
/// <para><b>光有這個還不夠，呼叫端要配 <see cref="CmdPalRefresh.KeepSelection"/>。</b></para>
///
/// CmdPal 預設把每一次 <c>ItemsChanged</c> 都當成「強制選第一列」，重用物件也救不回來。
/// 兩件事缺一不可，見 <see cref="CmdPalRefresh"/>。
/// </summary>
internal sealed class NoteItemSlots
{
    /// <summary>
    /// 路徑比較。這裡的路徑全部來自同一個 repository 的 <c>GetAll()</c>，已經是正規化過的
    /// 絕對路徑，所以不必再 <c>Path.GetFullPath</c> 一次，只要比對大小寫不敏感。
    /// </summary>
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// 上一輪的佈局。鍵、筆記與項目包成同一個不可變物件整個換掉，理由與
    /// <see cref="VersionedItemsCache{TKey}"/> 那個 snapshot 一樣:建清單是跨進程呼叫，
    /// 可能落在不同執行緒上，分成幾個欄位各寫各的會讀到「新鍵配舊槽」。
    /// </summary>
    private Layout? _previous;

    /// <summary>
    /// 依上面那三條規則分配槽位。
    /// </summary>
    /// <param name="create">做一列新的出來。內容變了的筆記也走這裡(規則三)。</param>
    /// <param name="rebind">
    /// 把一則筆記完整寫進一個**別人讓出來的**槽 —— 命令、標題、副標、圖示、詳細窗格、
    /// 選單、標籤，**每一項都要設**，因為那個物件上一輪坐的是另一則筆記。
    /// 只有規則二會呼叫它。
    /// </param>
    public ListItem[] Assign(
        IReadOnlyList<Note> notes,
        Func<Note, ListItem> create,
        Action<ListItem, Note> rebind)
    {
        var keys = new string[notes.Count];

        for (var i = 0; i < notes.Count; i++)
        {
            keys[i] = notes[i].FilePath;
        }

        var reusable = Reusable(Volatile.Read(ref _previous), keys);
        var items = new ListItem[notes.Count];

        for (var i = 0; i < notes.Count; i++)
        {
            items[i] = Resolve(reusable, notes[i], keys[i], create, rebind);
        }

        Volatile.Write(ref _previous, new Layout(keys, [.. notes], items));
        return items;
    }

    private static ListItem Resolve(
        Dictionary<string, Reuse> reusable,
        Note note,
        string key,
        Func<Note, ListItem> create,
        Action<ListItem, Note> rebind)
    {
        if (!reusable.TryGetValue(key, out var reuse))
        {
            return create(note);
        }

        // 規則二:這個槽是別人讓出來的，整列重綁。
        if (reuse.TookOver)
        {
            rebind(reuse.Item, note);
            return reuse.Item;
        }

        // 規則一:同一則筆記，內容也沒變 —— 一個屬性都不要碰。
        if (SameContent(reuse.Note, note))
        {
            return reuse.Item;
        }

        // 規則三:內容變了。就地改會打到使用者當下看的畫面(見類別註解)，所以換新的。
        return create(note);
    }

    /// <summary>
    /// 這一列畫出來、以及按下去會做什麼，有沒有變。
    ///
    /// 比的是**兩個清單頁真正用到的欄位**:標題與內文(標題列、摘要、詳細窗格、
    /// 複製內文、刪除確認框的描述都從這兩個來)、外來旗標(圖示)、衝突副本旗標(標籤)。
    /// <c>Updated</c> 這種只影響排序、畫面上看不到的欄位刻意不比 ——
    /// 比了只會讓「外部 touch 一下檔案」平白換掉一列的物件。
    /// </summary>
    private static bool SameContent(Note previous, Note current) =>
        string.Equals(previous.Title, current.Title, StringComparison.Ordinal)
        && string.Equals(previous.Body, current.Body, StringComparison.Ordinal)
        && previous.IsExternal == current.IsExternal
        && previous.HasDuplicateId == current.HasDuplicateId;

    /// <summary>
    /// 這一輪每一則筆記可以沿用哪一個槽，以及那個槽是不是別人讓出來的。
    /// 沒有出現在回傳值裡的筆記就是要新建的。
    /// </summary>
    private static Dictionary<string, Reuse> Reusable(Layout? previous, string[] keys)
    {
        var reusable = new Dictionary<string, Reuse>(PathComparer);

        if (previous is null)
        {
            return reusable;
        }

        var wanted = new HashSet<string>(keys, PathComparer);
        var removed = 0;

        // 規則 1:還在的筆記先各自認領自己的槽。
        for (var i = 0; i < previous.Keys.Length; i++)
        {
            if (wanted.Contains(previous.Keys[i]))
            {
                reusable[previous.Keys[i]] = new Reuse(previous.Items[i], previous.Notes[i], TookOver: false);
            }
            else
            {
                removed++;
            }
        }

        // 規則 2:消失的筆記把槽讓給後繼者 —— **覆蓋**掉後繼者剛認領的那一個。
        // 順序不能反過來，規則 2 就是拿來壓過規則 1 的。
        var handedOver = new HashSet<string>(PathComparer);

        for (var i = 0; i < previous.Keys.Length; i++)
        {
            if (wanted.Contains(previous.Keys[i]))
            {
                continue;
            }

            var successor = Successor(previous.Keys, i, wanted, handedOver, allowBackward: removed == 1);

            if (successor is null)
            {
                continue;
            }

            reusable[successor] = new Reuse(previous.Items[i], previous.Notes[i], TookOver: true);
            handedOver.Add(successor);
        }

        return reusable;
    }

    /// <summary>
    /// 第 <paramref name="removed"/> 格消失之後，它的槽該讓給誰。
    ///
    /// 往後找第一個還在、而且還沒收過讓位的。收過的不收第二次 —— 一次消失好幾則時
    /// (批次刪除、別台機器同步)，讓不出去的槽就當孤兒丟掉。
    ///
    /// <para><b><paramref name="allowBackward"/> 只在「整份清單只少了這一則」時是 true,
    /// 而那個限制是必要的。</b></para>
    ///
    /// 往前讓是為了「刪掉的是最後一列」——那時選取該落在新的最後一列上。但往前讓會
    /// **搶走一個根本沒被刪的項目的槽**(它自己的槽因此變孤兒)，而使用者很可能正選著它。
    /// 只少一則時這不成問題:那一則就是使用者剛按下刪除的那一列，被搶的是它前一列，
    /// 而使用者沒選著前一列。一次少好幾則就不一樣了 —— 那是同步或批次刪除，
    /// 使用者選著的可能是任何一列，為了用掉一個孤兒槽而把它的身分弄丟不划算。
    /// (踩過:<c>[A,B,C,D,E]</c> 只剩 <c>[A,E]</c> 時，<c>C</c> 的槽會往前搶走
    /// <c>A</c> 的位置，選著 <c>A</c> 的人就被丟回第一列。)
    /// </summary>
    private static string? Successor(
        string[] keys,
        int removed,
        HashSet<string> wanted,
        HashSet<string> handedOver,
        bool allowBackward)
    {
        for (var i = removed + 1; i < keys.Length; i++)
        {
            if (wanted.Contains(keys[i]) && !handedOver.Contains(keys[i]))
            {
                return keys[i];
            }
        }

        if (!allowBackward)
        {
            return null;
        }

        for (var i = removed - 1; i >= 0; i--)
        {
            if (wanted.Contains(keys[i]) && !handedOver.Contains(keys[i]))
            {
                return keys[i];
            }
        }

        return null;
    }

    private sealed record Layout(string[] Keys, Note[] Notes, ListItem[] Items);

    /// <summary>
    /// 一個可以沿用的槽，連同它上一輪坐的是哪一則筆記。
    /// <paramref name="TookOver"/> 為真代表它是**別則筆記讓出來的**，那一列真的換人坐了。
    /// </summary>
    private readonly record struct Reuse(ListItem Item, Note Note, bool TookOver);
}
