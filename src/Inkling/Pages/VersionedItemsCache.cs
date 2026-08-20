using Microsoft.CommandPalette.Extensions;

namespace Inkling.Pages;

/// <summary>
/// 清單頁的項目快取:「一組鍵 → 建好的項目陣列」。
///
/// 三個清單頁(<see cref="NoteListPage"/>、<see cref="QuickCapturePage"/>、
/// <see cref="DeleteNotesPage"/>)共用這一份。它們的基底型別不同
/// (DynamicListPage × 2、ListPage × 1),抽不了共同基底,所以用組合。
///
/// 規則只有一條,但真的踩過坑:**鍵一定要帶上 repository 的 Version,以及每一個
/// 會影響項目內容的設定值。** 只看查詢字串的話,新增一則筆記之後回到清單頁,
/// 拿到的還是舊的那份結果 —— 表現出來就是「筆記明明存好了,清單卻說還沒有任何筆記」;
/// 設定值漏帶的話,改設定等於沒改(換了分隔符,切出來的還是舊標題與內文)。
///
/// 配套的「訂閱 repository.Changed → RaiseItemsChanged → Dispose 退訂」樣板還是三頁各一份:
/// RaiseItemsChanged 是頁面基底類別的 protected 方法,收不進這個類。
/// 改快取規則時三頁要一起看。
/// </summary>
internal sealed class VersionedItemsCache<TKey>
    where TKey : notnull
{
    /// <summary>
    /// 鍵與值包成同一個不可變物件、整個換掉,而不是分兩個欄位各寫各的:
    /// 建清單跟觸發重建是兩次不同的跨進程呼叫,可能落在不同執行緒上
    /// (頁面自己在別處的註釋也承認過這件事)。分開存的話會讀到「新值配舊鍵」,
    /// 撕裂的最壞情況是回傳一份對不上當下查詢的舊清單,而不只是多重建一次。
    /// </summary>
    private Snapshot? _snapshot;

    /// <summary>
    /// 鍵一樣就回傳快取,否則重建並記住。GetItems 會被頻繁呼叫,同一組鍵不重建。
    /// </summary>
    public IListItem[] Get(TKey key, Func<IListItem[]> rebuild)
    {
        var snapshot = Volatile.Read(ref _snapshot);

        if (snapshot is not null && EqualityComparer<TKey>.Default.Equals(snapshot.Key, key))
        {
            return snapshot.Items;
        }

        var items = rebuild();
        Volatile.Write(ref _snapshot, new Snapshot(key, items));
        return items;
    }

    private sealed record Snapshot(TKey Key, IListItem[] Items);
}
