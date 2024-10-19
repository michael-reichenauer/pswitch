using Spectre.Console;

namespace pswitch;

record Solution(string Name, string AbsolutePath, IReadOnlyList<Project> Projects)
{
    public static Solution Parse(string absolutePath)
    {
        var name = Path.GetFileNameWithoutExtension(absolutePath);
        var solutionDirectory = Path.GetDirectoryName(absolutePath) ?? "";

        var projectPaths = GetProjectPaths(absolutePath);

        List<Project> solutionProjects = [];
        foreach (var specifiedProjectPath in projectPaths)
        {
            var projectAbsolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, specifiedProjectPath));
            if (!File.Exists(projectAbsolutePath))
            {
                AnsiConsole.MarkupLine($"[yellow]Project '{specifiedProjectPath}' not found at '{projectAbsolutePath}'[/]");
                continue;
            }

            var project = Project.Parse(projectAbsolutePath, specifiedProjectPath, "");
            solutionProjects.Add(project);
        }

        return new Solution(name, absolutePath, solutionProjects);
    }

    static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        string result = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" list");
        var lines = result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return lines.Where(line => line.EndsWith(".csproj")).Select(line => line.Trim()).ToList();
    }
}
