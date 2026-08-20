using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;

namespace Inkling;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            global::Shmuelie.WinRTServer.ComServer server = new();

            ManualResetEvent extensionDisposedEvent = new(false);

            // 只建立一個擴展實例並在每次 callback 時回傳同一個,
            // 讓 CmdPal 每次要 IExtension 拿到的都是同一個物件。
            InklingExtension extensionInstance = new(extensionDisposedEvent);
            server.RegisterClass<InklingExtension, IExtension>(() => extensionInstance);
            server.Start();

            // 擴展被 Dispose 時才收工。
            extensionDisposedEvent.WaitOne();
            server.Stop();
            server.UnsafeDispose();
        }
        else
        {
            Console.WriteLine("Not being launched as a Command Palette extension... exiting.");
        }
    }
}
