namespace Inkling;

/// <summary>
/// <c>RaiseItemsChanged</c> 的參數。那個 <c>totalItems</c> 看起來像「一共幾項」,
/// 實際上 CmdPal 拿它當旗標用。
/// </summary>
internal static class CmdPalRefresh
{
    /// <summary>
    /// 重整清單,但**不要把選取推回第一列**。
    ///
    /// CmdPal 的 <c>ListViewModel.Model_ItemsChanged</c> 只認一個值:
    /// <c>keepSelection: args.TotalItems == IncrementalRefresh</c>,而
    /// <c>IncrementalRefresh</c> 就是 <c>-2</c>。其餘任何值(包含 toolkit
    /// <c>RaiseItemsChanged</c> 的預設 <c>-1</c>)都會走 <c>forceFirstItem: true</c>,
    /// 也就是「更新完清單順便選第一列」。
    ///
    /// **這個常數自己寫死是刻意的**:那個 <c>-2</c> 住在 CmdPal 的
    /// <c>Microsoft.CmdPal.UI.ViewModels</c> 裡,不是 SDK 也不是 toolkit 的公開 API,
    /// 擴展引用不到。寫成有名字的常數至少讓下一個人看得到它是什麼、去哪裡查。
    ///
    /// <para><b>它不是萬靈丹 —— 要配 <see cref="Pages.NoteItemSlots"/> 一起用。</b></para>
    ///
    /// 「保留選取」保留的是**當下選中的那個 view model 還在不在新集合裡**
    /// (<c>ListItemsView.TrySetSelectionAfterUpdate</c>)。每次重建都給一批全新的
    /// <c>ListItem</c> 物件的話,那個判斷必然為假,選取照樣回第一列。
    /// 兩件事缺一不可,實測過。
    ///
    /// <para><b>什麼時候不該用它。</b></para>
    ///
    /// 使用者**打字**造成的清單變動要維持預設值:搜尋結果換了一批,選取本來就該回到
    /// 最上面那一列。快速記下頁整頁都是這種情況(第一列是「記下這句話」,
    /// 那才是使用者要按的),所以那一頁一個都不用改。
    /// </summary>
    public const int KeepSelection = -2;
}
