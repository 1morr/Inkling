using System.Reflection;
using Microsoft.CommandPalette.Extensions.Toolkit;

// 把 Command Palette Toolkit 某個型別的實際公開成員印出來。
//
// 存在的理由:Microsoft Learn 上的 API 參考頁面有些是 2025 年初寫的，跟 0.11 版的
// 實際簽章對不上。開發時至少踩到兩次:
//   FallbackCommandItem  文檔寫 (ICommand)，實際是 (ICommand, string displayTitle, string id)
//   KeyChordHelpers.FromModifiers  文檔寫 4 個參數，實際是 6 個
// 與其靠編譯錯誤一次次試，不如直接問組件本身。
//
// 用法:
//   dotnet run --project tools\ApiDump -- FallbackCommandItem CommandResult
//   dotnet run --project tools\ApiDump -- --paths

if (args.Length > 0 && args[0] == "--paths")
{
    Console.WriteLine($"IsPackaged       = {Utilities.IsPackaged()}");
    Console.WriteLine($"BaseSettingsPath = {Utilities.BaseSettingsPath("Inkling")}");
    return;
}

var assembly = typeof(ListItem).Assembly;

var wanted = args.Length > 0
    ? args
    : ["ListPage", "DynamicListPage", "ListItem", "ContentPage", "FormContent", "MarkdownContent", "CommandResult"];

foreach (var name in wanted)
{
    var type = assembly.GetTypes().FirstOrDefault(t => t.Name == name);
    if (type is null)
    {
        Console.WriteLine($"### {name}:找不到這個型別");
        continue;
    }

    Console.WriteLine($"### {type.FullName}  : {type.BaseType?.Name}");

    foreach (var ctor in type.GetConstructors())
    {
        Console.WriteLine($"    .ctor({Parameters(ctor)})");
    }

    // 連 protected 一起印:我們是在「繼承」這些型別，刷新 UI 的鉤子
    // (RaiseItemsChanged / OnPropertyChanged 之類)通常都是 protected,
    // 只看 public 的話會以為根本沒有辦法通知 UI 更新。
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    foreach (var member in type.GetMembers(Flags))
    {
        switch (member)
        {
            case EventInfo e:
                Console.WriteLine($"    event {Pretty(e.EventHandlerType!)} {e.Name}");
                break;
            case PropertyInfo p when IsVisible(p.GetMethod) || IsVisible(p.SetMethod):
                Console.WriteLine($"    prop {Pretty(p.PropertyType)} {p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}");
                break;
            case MethodInfo m when !m.IsSpecialName && IsVisible(m):
                var access = m.IsPublic ? "" : "protected ";
                var modifier = m.IsStatic ? "static " : m.IsVirtual ? "virtual " : "";
                Console.WriteLine($"    {access}{modifier}{Pretty(m.ReturnType)} {m.Name}({Parameters(m)})");
                break;
        }
    }

    Console.WriteLine();
}

// private 的東西對子類別沒意義，只留 public 與 protected。
static bool IsVisible(MethodBase? method) =>
    method is not null && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly);

static string Parameters(MethodBase method) =>
    string.Join(", ", method.GetParameters().Select(p => $"{Pretty(p.ParameterType)} {p.Name}"));

static string Pretty(Type type)
{
    if (!type.IsGenericType)
    {
        return type.Name;
    }

    var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
    return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Pretty))}>";
}
