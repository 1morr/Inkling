using Xunit;

namespace Inkling.Core.Tests;

/// <summary>
/// 會實際打磁碟、而且對時間敏感的測試共用這一個 collection。
///
/// xUnit 預設讓不同類別並行。效能那幾條量的是**含磁碟的牆鐘時間**，而同一個組件裡
/// 另有一整批建幾百個檔案、等 watcher 事件的 repository 測試 —— 兩邊同時跑就是在
/// 搶同一顆碟，警戒線因此會在程式完全沒壞的情況下紅掉(CI 用的還是共用機器)。
///
/// 放進同一個 collection 就不會同時跑;<c>DisableParallelization</c> 再擋掉它跟
/// 其他 collection 並行。代價是這兩類測試變成序列執行，多幾秒。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DiskBoundTests
{
    public const string Name = "disk-bound";
}
