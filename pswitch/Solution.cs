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

            var project = Project.Parse(projectAbsolutePath, specifiedProjectPath);
            solutionProjects.Add(project);
        }

        return new Solution(name, absolutePath, solutionProjects);
    }

    public IReadOnlyList<Package> GetReferencedPackages() =>
        Projects.SelectMany(p => p.PackageReferences).ToList();

    public void AddExternalProjectsToSolution(Project project, string solutionFolderName)
    {
        // Add the main project to the solution
        AddProject(project.AbsolutePath, solutionFolderName);
        foreach (var projectReference in project.GetReferencedProjectIncludeTransitive())
        {
            AddProject(projectReference.AbsolutePath, solutionFolderName);
        }
    }

    internal void RemoveExternalProjectsFromSolution(string selectedPackageName)
    {
        var switchedProjectsReferences = Projects
            .Concat(Projects.SelectMany(p => p.GetReferencedProjectIncludeTransitive()))
            .Where(p => p.SwitchReference == selectedPackageName)
            .DistinctBy(p => p.AbsolutePath)
            .ToList();

        var allProject = switchedProjectsReferences
            .Concat(switchedProjectsReferences.SelectMany(p => p.GetReferencedProjectIncludeTransitive()))
            .ToList();

        var projectsToRemove = allProject
            .Where(p => null != allProject.FirstOrDefault(pp => pp.AbsolutePath == p.AbsolutePath))
            .ToList();

        foreach (var project in projectsToRemove)
        {
            RemoveProject(project.AbsolutePath);
        }
    }


    void AddProject(string projectFilePath, string solutionFolderName)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Cmd.Execute("dotnet", $"sln \"{AbsolutePath}\" add --solution-folder \"{solutionFolderName}\" \"{projectFilePath}\" ");
        AnsiConsole.MarkupLine($"   Added: [aqua]{projectName}[/] [grey]({projectFilePath})[/]");
    }

    void RemoveProject(string projectFilePath)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFilePath);
        Cmd.Execute("dotnet", $"sln \"{AbsolutePath}\" remove \"{projectFilePath}\" ");
        AnsiConsole.MarkupLine($"  Removed: [aqua]{projectName}[/] [grey]({projectFilePath})[/]");
    }


    static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        string result = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" list");
        var lines = result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return lines.Where(line => line.EndsWith(".csproj")).Select(line => line.Trim()).ToList();
    }


}
