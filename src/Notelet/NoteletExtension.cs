using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.CommandPalette.Extensions;

namespace Notelet;

// 這個 GUID 必須與 Package.appxmanifest 的 com:Class Id 及 CreateInstance ClassId 完全一致。
[Guid("4D628A0F-6610-4043-9E1E-D696EADCB6BB")]
public sealed partial class NoteletExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _extensionDisposedEvent;
    private readonly NoteletCommandsProvider _provider = new();

    public NoteletExtension(ManualResetEvent extensionDisposedEvent)
    {
        _extensionDisposedEvent = extensionDisposedEvent;
    }

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null,
    };

    public void Dispose()
    {
        // provider 那份 Dispose 有完整的退訂與 ProviderState 釋放
        // (見 NoteletCommandsProvider.Dispose)。目前不叫也不會出事 —— Program.Main
        // 收到這個 event 之後整個進程就結束了 —— 但那份 Dispose 是按「會被呼叫」寫的,
        // 宿主生命週期哪天變了(同進程重建擴展實例),漏叫就會留下還在聽設定事件的
        // 死頁面與 FileSystemWatcher。
        _provider.Dispose();
        _extensionDisposedEvent.Set();
    }
}
