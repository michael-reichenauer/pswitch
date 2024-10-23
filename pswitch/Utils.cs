using Spectre.Console;

namespace pswitch;

class Utils
{
    record Selection<T>(string Text, T Value);

    public static bool IsConsoleInteractive() =>
         !Console.IsInputRedirected && !Console.IsOutputRedirected && !Console.IsErrorRedirected;

    public static string GetRelativePath(string basePath, string targetPath)
    {
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(targetPath);

        return baseUri.MakeRelativeUri(targetUri).ToString();
    }

    public static T SelectionPrompt<T>(string message, IEnumerable<T> choices, Func<T, string> textSelector, string? moreChoicesText = null)
    {
        List<Selection<T>> selections = choices.Select(c => new Selection<T>(textSelector(c), c)).ToList();
        string selection;
        moreChoicesText ??= "Use arrows Up and Down to select";

        selection = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title(message)
            .PageSize(10)
            .MoreChoicesText($"[grey]({moreChoicesText})[/]")
            .AddChoices(selections.Select(s => $"{s.Text}")));

        var selected = selections.FirstOrDefault(s => s.Text == selection);
        if (selected == null) throw new TaskCanceledException("Selection cancelled.");

        return selected.Value;
    }
}
