using Microsoft.CommandPalette.Extensions.Toolkit;
using Inkling.Core;

namespace Inkling.Pages;

/// <summary>
/// 把「這一輪要列出來的筆記」對應到「上一輪已經在用的 <see cref="ListItem"/> 物件」。
///
/// <para><b>為什麼需要這個東西:CmdPal 的選取跟著物件識別走。</b></para>
///
/// CmdPal 收到 <c>ItemsChanged</c> 之後會重新 <c>GetItems()</c>,再拿回來的每一個
/// <c>IListItem</c> 去查它自己的 view model 快取(<c>ListViewModel._vmCache</c>,
/// 比較器是 <c>ProxyReferenceEqualityComparer</c> —— **參考相等**),然後用
/// <c>ListHelpers.InPlaceUpdateList</c> 去 diff 那份集合。最後
/// <c>ListItemsView.TrySetSelectionAfterUpdate</c> 只問一句:當下選中的那個 view model
/// **還在不在新的集合裡**?在就什麼都不做,不在就選第一列。
///
/// 也就是說,每次重建清單都 <c>new</c> 一批全新的 <see cref="ListItem"/>,等於告訴 CmdPal
/// 「整份清單都換人了」—— 選取必然回到第一列。**這才是「刪一則筆記,選取跳回最上面」
/// 的真正成因**,跟刪除本身無關:別台機器同步下來一則、或使用者拿別的編輯器改了任何一則,
/// 一樣會把選取踢走(真機實測過)。
///
/// <para><b>分配規則:身分優先,只有被移除的那一格讓位。</b></para>
///
/// <list type="number">
/// <item>還在清單裡的筆記**沿用自己上一輪的槽**。新增、修改、重新排序時,使用者選著的
/// 那一則因此原地不動 —— 選取跟著**筆記**走。</item>
/// <item>從清單上消失的筆記,把自己的槽**讓給後繼者**(上一輪排在它後面、這一輪還在的
/// 第一則;只少一則時才允許往前找,理由見 <see cref="Successor"/>)。
/// 後繼者原本那個槽變成孤兒,不再回傳。
/// 於是使用者選著的那一列即使被刪掉,那個槽仍然在集合裡 —— 選取因此留在**原位置**,
/// 顯示的是下一則,跟檔案總管刪檔案的手感一致。</item>
/// </list>
///
/// 兩條規則各自對應一種期待,而且不會互相干擾:刪除走第 2 條(位置語意),
/// 其餘所有變動走第 1 條(身分語意)。只走第 1 條的話刪除仍會跳第一列;
/// 只走第 2 條(單純按索引重用)的話,外部同步進來一則排在前面的筆記,
/// 使用者正在看的那一列會**默默換成別則筆記** —— 兩條都試過,這是實測出來的組合。
///
/// <para><b>身分認的是 <see cref="Note.FilePath"/>,不是 <c>Id</c>。</b></para>
///
/// 雲端硬碟的衝突副本是整檔複製,同一個 <c>id</c> 會出現在兩個檔案上 —— 拿 id 當鍵會讓
/// 兩列搶同一個槽。這跟 <c>Update</c> / <c>Delete</c> 認路徑是同一條理由,
/// 見 CLAUDE.md〈解析一則筆記認的是路徑,不是 id〉。
///
/// <para><b>光有這個還不夠,呼叫端要配 <see cref="CmdPalRefresh.KeepSelection"/>。</b></para>
///
/// CmdPal 預設把每一次 <c>ItemsChanged</c> 都當成「強制選第一列」,重用物件也救不回來。
/// 兩件事缺一不可,見 <see cref="CmdPalRefresh"/>。
/// </summary>
internal sealed class NoteItemSlots
{
    /// <summary>
    /// 路徑比較。這裡的路徑全部來自同一個 repository 的 <c>GetAll()</c>,已經是正規化過的
    /// 絕對路徑,所以不必再 <c>Path.GetFullPath</c> 一次,只要比對大小寫不敏感。
    /// </summary>
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// 上一輪的佈局。鍵與項目包成同一個不可變物件整個換掉,理由與
    /// <see cref="VersionedItemsCache{TKey}"/> 那個 snapshot 一樣:建清單是跨進程呼叫,
    /// 可能落在不同執行緒上,分成兩個欄位各寫各的會讀到「新鍵配舊槽」。
    /// </summary>
    private Layout? _previous;

    /// <summary>
    /// 依上面那兩條規則分配槽位。<paramref name="update"/> 是「把這一則筆記的內容
    /// 就地寫進這個槽」,<paramref name="create"/> 只在沒有槽可用時才會被呼叫。
    /// </summary>
    public ListItem[] Assign(
        IReadOnlyList<Note> notes,
        Func<Note, ListItem> create,
        Action<ListItem, Note> update)
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
            if (reusable.TryGetValue(keys[i], out var slot))
            {
                update(slot, notes[i]);
                items[i] = slot;
            }
            else
            {
                items[i] = create(notes[i]);
            }
        }

        Volatile.Write(ref _previous, new Layout(keys, items));
        return items;
    }

    /// <summary>
    /// 這一輪每一則筆記可以沿用哪一個槽。沒有出現在回傳值裡的筆記就是要新建的。
    /// </summary>
    private static Dictionary<string, ListItem> Reusable(Layout? previous, string[] keys)
    {
        var reusable = new Dictionary<string, ListItem>(PathComparer);

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
                reusable[previous.Keys[i]] = previous.Items[i];
            }
            else
            {
                removed++;
            }
        }

        // 規則 2:消失的筆記把槽讓給後繼者 —— **覆蓋**掉後繼者剛認領的那一個。
        // 順序不能反過來,規則 2 就是拿來壓過規則 1 的。
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

            reusable[successor] = previous.Items[i];
            handedOver.Add(successor);
        }

        return reusable;
    }

    /// <summary>
    /// 第 <paramref name="removed"/> 格消失之後,它的槽該讓給誰。
    ///
    /// 往後找第一個還在、而且還沒收過讓位的。收過的不收第二次 —— 一次消失好幾則時
    /// (批次刪除、別台機器同步),讓不出去的槽就當孤兒丟掉。
    ///
    /// <para><b><paramref name="allowBackward"/> 只在「整份清單只少了這一則」時是 true,
    /// 而那個限制是必要的。</b></para>
    ///
    /// 往前讓是為了「刪掉的是最後一列」——那時選取該落在新的最後一列上。但往前讓會
    /// **搶走一個根本沒被刪的項目的槽**(它自己的槽因此變孤兒),而使用者很可能正選著它。
    /// 只少一則時這不成問題:那一則就是使用者剛按下刪除的那一列,被搶的是它前一列,
    /// 而使用者沒選著前一列。一次少好幾則就不一樣了 —— 那是同步或批次刪除,
    /// 使用者選著的可能是任何一列,為了用掉一個孤兒槽而把它的身分弄丟不划算。
    /// (踩過:<c>[A,B,C,D,E]</c> 只剩 <c>[A,E]</c> 時,<c>C</c> 的槽會往前搶走
    /// <c>A</c> 的位置,選著 <c>A</c> 的人就被丟回第一列。)
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

    private sealed record Layout(string[] Keys, ListItem[] Items);
}
