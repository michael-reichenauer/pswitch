using System.Runtime.InteropServices;

namespace pswitch;


// Inspired by https://github.com/Lutonet/SpectreConsoleFileBrowser/tree/master
public class FileBrowser
{
    static readonly string MoreChoicesText = "Use arrows Up and Down to select, Enter to step into folder or select file";
    public int PageSize { get; set; } = 15;

    public string SelectFileText { get; set; } = "Select file";

    static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    record Item(string Name, string Path);

    public string GetFilePath(string? folder = null!)
    {
        string currentFolder = string.IsNullOrEmpty(folder) || !Directory.Exists(folder)
             ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
             : folder;

        while (true)
        {
            var items = new List<Item>();

            if (currentFolder == "")
            {
                items.AddRange(GetDriveItems());
            }
            else
            {
                items.AddRange(
                    GetParentItems(currentFolder)
                    .Concat(GetDirectoryItems(currentFolder))
                    .Concat(GetFileItems(currentFolder)));
            }

            var selectedItem = Utils.SelectionPrompt(
                $"{SelectFileText} in: \n[blue]{currentFolder}[/]",
                items,
                i => i.Name,
                MoreChoicesText);

            if (selectedItem.Path == "" || Directory.Exists(selectedItem.Path))
            {
                currentFolder = selectedItem.Path;
                continue;
            }

            return selectedItem.Path;
        }
    }


    static IReadOnlyList<Item> GetDriveItems()
    {
        if (!IsWindows) return [new Item(":computer_disk: /", "/")];

        var items = new List<Item>();
        foreach (string drive in Directory.GetLogicalDrives())
        {
            items.Add(new("> " + drive, drive));
        }

        return items;
    }

    static IReadOnlyList<Item> GetParentItems(string currentFolder)
    {
        var parentFolderInfo = new DirectoryInfo(currentFolder).Parent;
        if (parentFolderInfo != null)
        {
            return [new("[green]> ..[/]", parentFolderInfo.FullName)];
        }

        if (IsWindows && currentFolder != "")
        {
            if (Directory.GetLogicalDrives().Length > 1)
            {
                return [new("[green]> ..[/]", "")];
            }
        }

        return [];
    }

    static IReadOnlyList<Item> GetDirectoryItems(string currentFolder)
    {
        var items = new List<Item>();
        foreach (var dirInfo in Directory.GetDirectories(currentFolder))
        {
            try { var _ = Directory.GetDirectories(currentFolder); } catch { continue; } // Skip if failed to read that directory 
            int cut = 0;
            if (new DirectoryInfo(currentFolder).Parent != null) cut = 1;
            string folderName = dirInfo.Substring(currentFolder.Length + cut);
            string folderPath = dirInfo;
            items.Add(new("> " + folderName, folderPath));
        }

        return items;
    }

    static IReadOnlyList<Item> GetFileItems(string currentFolder)
    {
        var items = new List<Item>();
        foreach (var file in Directory.GetFiles(currentFolder))
        {
            string filename = Path.GetFileName(file);
            items.Add(new("- " + filename, file));
        }

        return items;
    }
}
