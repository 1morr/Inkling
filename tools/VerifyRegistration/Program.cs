using Windows.ApplicationModel.AppExtensions;

// 驗證某個套件確實被 Windows 註冊成 Command Palette 擴展。
//
// 這正是 CmdPal 自己用來發現擴展的機制，所以「探針看得到」等於「CmdPal 看得到」。
// 有了它就不必靠肉眼判斷部署到底有沒有生效 —— 這在沒有 Visual Studio、
// 不能用 Build > Deploy 的環境下特別重要。
//
// 用法: VerifyRegistration [套件名稱的片段]
//
// 比對是「包含」而不是相等:Store 上架時會把 Identity 的 Name 重指派成
// 「<發行者>.<名稱>」，精確比對從第一個 Store 版本起就一律落空，而這支程式的
// 用途正是「確認部署真的生效」—— 它自己失明是最糟的失敗方式。
// 結束碼: 0 = 找到，1 = 沒找到

var expected = args.Length > 0 ? args[0] : "Inkling";

var catalog = AppExtensionCatalog.Open("com.microsoft.commandpalette");
var extensions = await catalog.FindAllAsync().AsTask();

Console.WriteLine($"已註冊的 Command Palette 擴展({extensions.Count} 個):");

var found = false;
foreach (var ext in extensions)
{
    var name = ext.Package.Id.Name;
    var isMatch = name.Contains(expected, StringComparison.OrdinalIgnoreCase);
    found |= isMatch;

    Console.WriteLine($"  {(isMatch ? "->" : "  ")} {name}  (Id={ext.Id}, Display={ext.DisplayName})");
}

Console.WriteLine();

if (found)
{
    Console.WriteLine($"OK: 「{expected}」已註冊，Command Palette 看得到它。");
    return 0;
}

Console.Error.WriteLine($"失敗:AppExtension 目錄裡找不到「{expected}」。");
return 1;
