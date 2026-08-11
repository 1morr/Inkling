using Microsoft.CommandPalette.Extensions;

namespace Notelet;

/// <summary>
/// 詳細窗格寬度的存放處。
///
/// 清單頁只需要這麼一個屬性,不必認識整個設定管理員 —— 這樣它的相依關係看一眼就清楚,
/// 測試(如果哪天 UI 層也能測)也不必生出一份 settings.json。
/// </summary>
internal interface IDetailsWidthStore
{
    /// <summary>設下去就會立刻存檔,下次啟動照這個值。</summary>
    ContentSize DetailsWidth { get; set; }
}
