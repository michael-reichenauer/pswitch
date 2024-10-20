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
        string currentFolder = folder ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        while (true)
        {
            var items = new List<Item>();

            if (currentFolder == "")
            {
                var drives = Directory.GetLogicalDrives();
                foreach (string drive in drives)
                {
                    items.Add(new($":computer_disk: {drive}", drive));
                }
            }
            else
            {
                string[] directoriesInFolder;
                try
                {
                    directoriesInFolder = Directory.GetDirectories(currentFolder);
                }
                catch
                {
                    // Current folder failed, lets try to get the user profile folder
                    currentFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    directoriesInFolder = Directory.GetDirectories(currentFolder);
                }

                // Add '..' folder to go to parent folder
                var parentFolderInfo = new DirectoryInfo(currentFolder).Parent;
                if (parentFolderInfo != null)
                {
                    items.Add(new("[green]:file_folder: ..[/]", parentFolderInfo.FullName));
                }
                else if (IsWindows && currentFolder != "")
                {
                    if (Directory.GetLogicalDrives().Count() > 1)
                    {
                        items.Add(new("[green]:file_folder: ..[/]", ""));
                    }
                }

                foreach (var dirInfo in directoriesInFolder)
                {
                    int cut = 0;
                    if (parentFolderInfo != null) cut = 1;
                    string folderName = dirInfo.Substring(currentFolder.Length + cut);
                    string folderPath = dirInfo;
                    items.Add(new(":file_folder: " + folderName, folderPath));
                }

                var fileList = Directory.GetFiles(currentFolder);
                foreach (string file in fileList)
                {
                    string filename = Path.GetFileName(file);
                    items.Add(new(":page_facing_up: " + filename, file));
                }
            }

            var selected = Utils.SelectionPrompt(
                $"{SelectFileText} in: \n[blue]{currentFolder}[/]",
                items,
                i => i.Name,
                MoreChoicesText);

            if (selected.Path == "" || Directory.Exists(selected.Path))
            {
                currentFolder = selected.Path;
                continue;
            }

            return selected.Path;
        }
    }
}
