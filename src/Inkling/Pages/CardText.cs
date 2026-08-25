using System.Text.Json.Nodes;

namespace Inkling.Pages;

/// <summary>
/// Adaptive Cards 的樣板是手拼的 JSON 字串，而現在填進去的字來自資源檔 ——
/// 翻譯裡出現一個雙引號或反斜線就會把整張卡片變成不合法的 JSON。
/// 所有要進卡片的字串一律經過這裡。
/// </summary>
internal static class CardText
{
    /// <summary>把字串變成帶引號的 JSON 字面值，連跳脫一起處理。</summary>
    public static string Json(string text) => JsonValue.Create(text)!.ToJsonString();
}
