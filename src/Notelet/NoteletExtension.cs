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

    public void Dispose() => _extensionDisposedEvent.Set();
}
