using Microsoft.CommandPalette.Extensions.Toolkit;

namespace Inkling;

/// <summary>
/// 「做完一件事之後說一句話」的唯一入口。三個方法對應三種收尾，**沒有第四種**。
///
/// <para><b>規則:通道由面板去留決定。</b></para>
///
/// <list type="bullet">
/// <item><see cref="Stay"/> —— 留在原來那一頁:底部的 InfoBar + 計數徽章。</item>
/// <item><see cref="Done"/> —— 收起面板:toast。</item>
/// <item><see cref="Home"/> —— 切回主搜尋框(面板還開著):toast。</item>
/// </list>
///
/// <para><b>為什麼非綁在一起不可。</b></para>
///
/// <see cref="ToastStatusMessage"/> 畫的 InfoBar **綁在當下這一頁的 view model 上**,
/// 導覽走的時候會連同訊息一起拆掉 —— 配 <c>GoHome</c> / <c>Dismiss</c> 就是「訊息一個字
/// 都不會出現」，而且**完全靜默**:檔案存好了、程式碼跑到了、沒有例外。這個 bug 在這個 repo
/// 發生過兩次(新增筆記表單、設定頁存檔)，兩次都撐了很久沒被發現，因為它看起來就像
/// 「本來就沒有提示」。反過來 <c>CommandResult.ShowToast</c> 是獨立視窗，導覽拆不掉它。
///
/// 把「訊息」與「收尾」寫成同一個呼叫，那種組合就**建構不出來** —— 這是這個型別存在的
/// 全部理由。散在各處手寫 <c>new ToastStatusMessage(...).Show()</c> 配一個回傳值時，
/// 兩者的相容性沒有任何東西在把關。
///
/// <para><b>還有一顆地雷在另一邊:</b></para>
///
/// <c>CommandResult.ShowToast("字串")</c> 那個簡寫吃的是 <see cref="ToastArgs"/> 的預設
/// <c>Result</c>，而那個預設是 <c>Dismiss</c>(把它 <c>new</c> 一個出來讀屬性讀到的)。
/// 也就是說「只想發個提示」的最順手寫法**附帶把面板收掉**。toolkit 幾個現成命令
/// (例如 <see cref="CopyTextCommand"/>)的預設 <c>Result</c> 也是這一種。
///
/// <para><b>約定:<c>ShowToast</c> 與 <c>ToastStatusMessage</c> 只准出現在這個檔案裡。</b></para>
///
/// <c>grep -rn "ShowToast\|ToastStatusMessage" src/</c> 應該只命中這裡。多命中一處就是
/// 有人繞過了這條規則，而繞過去不會有任何編譯或執行期訊號。
///
/// 考證見 <c>docs/design-notes.md</c>〈toast 不會把面板關掉〉與〈`ToastStatusMessage`
/// 不是那個 toast〉。**兩個都不會搶焦點** —— 面板去留完全由回傳值決定，那條「toast 會
/// 搶焦點所以面板必關」的舊規則是假的，量測與推翻經過寫在前一節。
/// </summary>
internal static class Feedback
{
    /// <summary>
    /// 說一句話，**留在原來那一頁**。
    ///
    /// 走底部的 InfoBar + 計數徽章(<see cref="ToastStatusMessage"/> → host 的
    /// <c>ShowStatus</c>，約 2.5 秒自己收掉)。它畫在面板裡，所以會壓住那一頁底部的內容 ——
    /// 代價，換來的是訊息就在使用者正在看的東西旁邊。
    ///
    /// ⚠ **前提是 <c>ExtensionHost</c> 接到了 host。** 那是靜態的，沒有在
    /// <c>CommandProvider.InitializeWithHost</c> 裡呼叫 <c>ExtensionHost.Initialize(host)</c>
    /// 的話，<c>Show()</c> 靜靜地什麼都不做 —— 這整條通道曾經是死的，而文檔一直寫成通的。
    /// 見 <see cref="InklingCommandsProvider.InitializeWithHost"/>。
    /// </summary>
    public static CommandResult Stay(string message)
    {
        Say(message);

        return CommandResult.KeepOpen();
    }

    /// <summary>
    /// 只說一句話，**不決定收尾** —— 給不在命令回傳路徑上的呼叫端。
    ///
    /// 目前只有資料夾挑選器的兩個回呼:它們跑在另一條 STA 執行緒上，那時
    /// <c>SubmitForm</c> 早就回傳完了，沒有 <c>CommandResult</c> 可以給。
    /// 有回傳值可用的地方一律走 <see cref="Stay"/>，別用這個 —— 它把「訊息」與「收尾」
    /// 拆開了，而那正是這個型別要防的事。
    /// </summary>
    /// <param name="durationMs">
    /// 留多久，0 = 用 toolkit 的預設 2500(<c>new</c> 一個出來讀到的)。
    /// 挑資料夾那條要撐長:面板在對話框拿到焦點的當下就藏起來了，使用者挑完回到 CmdPal 時
    /// 預設那 2.5 秒多半已經走完。
    /// </param>
    public static void Say(string message, int durationMs = 0)
    {
        // 分兩條寫是因為 `Duration` 是 init-only:建好之後指派不了，只能走物件初始設定式。
        if (durationMs > 0)
        {
            new ToastStatusMessage(message) { Duration = durationMs }.Show();
            return;
        }

        new ToastStatusMessage(message).Show();
    }

    /// <summary>
    /// 說一句話，然後**收起面板**。收工那一下用這個。
    ///
    /// 走 toast(獨立視窗，畫在面板**外面的下方**，面板收掉之後它還留在畫面上)。
    /// 這是唯一能在面板消失之後還講得到話的通道 —— InfoBar 畫在面板上，面板收了就跟著沒了。
    /// </summary>
    public static CommandResult Done(string message) =>
        Toast(message, CommandResult.Dismiss());

    /// <summary>
    /// 說一句話，然後**切回主搜尋框**(面板還開著)。
    ///
    /// 一樣走 toast:<c>GoHome</c> 是導覽，而導覽會把 InfoBar 連同那一頁拆掉。
    /// 目前只有設定頁存檔用得到 —— 存完不該還停在表單上，但也還不到收工。
    /// </summary>
    public static CommandResult Home(string message) =>
        Toast(message, CommandResult.GoHome());

    /// <summary>
    /// <c>Result</c> 一定要明著給，理由見型別註解最後那一段。
    /// **這個方法不對外開放**，所以呼叫端沒有機會把 <c>KeepOpen</c> 傳進來 ——
    /// 「toast + 留在原地」正是這條規則要排除的組合。
    /// </summary>
    private static CommandResult Toast(string message, CommandResult after) =>
        CommandResult.ShowToast(new ToastArgs
        {
            Message = message,
            Result = after,
        });
}
