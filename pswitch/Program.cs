using Spectre.Console;
using static System.Environment;

namespace pswitch;

class State
{
    public string WorkParentPath { get; set; } = "";
    public string TargetParentPath { get; set; } = "";
}


class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");
    static readonly string SolutionVirtualFolder = "ExternalProjects";
    static readonly string StateFilePath = Path.Join(GetFolderPath(SpecialFolder.UserProfile), ".pswitch.json");

    static void Main()
    {
        try
        {
            AnsiConsole.WriteLine();

            // Prompt user to select a package from the solution to switch to a target solution project
            var workSolutionPath = GetWorkSolutionPath();
            var workSolution = Solution.Parse(workSolutionPath);

            var selectedPackage = PromptPackage(workSolution);

            if (selectedPackage.IsSwitched)
            {
                RestorePackage(workSolution, selectedPackage);
            }
            else
            {
                SwitchPackage(workSolution, selectedPackage);
            }

            AnsiConsole.MarkupLine("\n[green]Done![/]\n");
        }
        catch (TaskCanceledException ex)
        {
            AnsiConsole.MarkupLine($"\n[red]{ex.Message}[/]\n");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Error[/]:");
            Console.WriteLine($"{ex}\n");
        }
    }


    static void SwitchPackage(Solution workSolution, Package selectedPackage)
    {
        AnsiConsole.MarkupLine("");

        // Prompt user to select a target project from the target solution to be referenced instead of the selected package
        var targetSolutionPath = GetTargetSolutionPath();
        var targetSolution = Solution.Parse(targetSolutionPath);

        Project selectedTargetProject = PromptTargetProject(targetSolution);

        AnsiConsole.MarkupLine("\n[grey]-----------------------------------------------------------[/]");
        AnsiConsole.MarkupLine($"Adding projects to [green]{workSolution.Name}[/]/[darkgreen]{SolutionVirtualFolder}[/]:");
        workSolution.AddExternalProjectsToSolution(selectedTargetProject, SolutionVirtualFolder);

        AnsiConsole.MarkupLine("\nSwitching package references to project references:");
        var projectsToSwitch = workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name));
        foreach (var project in projectsToSwitch)
        {
            project.SwitchPackageToProjectReference(selectedPackage.Name, selectedTargetProject);
        }
    }

    static void RestorePackage(Solution workSolution, Package selectedPackage)
    {
        var projectsToRestore = workSolution.Projects
            .Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name && r.IsSwitched))
            .ToList();

        AnsiConsole.MarkupLine("\n[grey]-----------------------------------------------------------[/]");
        AnsiConsole.MarkupLine("Restoring Package references from project references:");
        foreach (var project in projectsToRestore)
        {
            project.RestorePackageToPackageReference(selectedPackage.Name);
        }

        AnsiConsole.MarkupLine($"\nRemoving projects from [green]{workSolution.Name}[/]/[darkgreen]{SolutionVirtualFolder}[/]:");
        workSolution.RemoveExternalProjectsFromSolution(selectedPackage.Name);
    }

    static string GetWorkSolutionPath()
    {
        if (!Utils.IsConsoleInteractive()) return DefaultWorkSolutionPath;
        var fileBrowser = new FileBrowser() { SelectFileText = "Select a solution file", FileFilter = f => Path.GetExtension(f)?.ToLower() == ".sln" };

        var parentFolder = FileStore.Get<State>(StateFilePath)?.WorkParentPath;
        var path = fileBrowser.GetFilePath(parentFolder);

        parentFolder = Path.GetDirectoryName(Path.GetDirectoryName(path) ?? "");
        if (parentFolder != null && Directory.Exists(parentFolder))
        {
            FileStore.Set<State>(StateFilePath, s => s.WorkParentPath = parentFolder);
        }
        return path;
    }

    static string GetTargetSolutionPath()
    {
        if (!Utils.IsConsoleInteractive()) return DefaultTargetSolutionPath;
        var fileBrowser = new FileBrowser() { SelectFileText = "Select a target solution file", FileFilter = f => Path.GetExtension(f)?.ToLower() == ".sln" };

        var parentFolder = FileStore.Get<State>(StateFilePath)?.TargetParentPath;
        var path = fileBrowser.GetFilePath(parentFolder);

        parentFolder = Path.GetDirectoryName(Path.GetDirectoryName(path) ?? "");
        if (parentFolder != null && Directory.Exists(parentFolder))
        {
            FileStore.Set<State>(StateFilePath, s => s.TargetParentPath = parentFolder);
        }
        return path;
    }

    static Package PromptPackage(Solution workSolution)
    {
        var packages = workSolution.GetReferencedPackages();
        if (!Utils.IsConsoleInteractive()) return packages.First(p => p.Name == "Scrutor");

        var switchablePackages = packages.DistinctBy(p => p.Name).Where(p => !p.IsSwitched).ToList();
        var switchedPackages = packages.DistinctBy(p => p.Name).Where(p => p.IsSwitched).ToList();

        var selectablePackages = switchedPackages.Any()
            ? switchedPackages.Prepend(null!).Concat(switchablePackages)
            : switchablePackages.Prepend(null!);

        // Prompt user to select a package from the solution to switch to a target solution project 
        var selectedPackage = Utils.SelectionPrompt(
            $"\nSelect a package to switch or restore in [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]:",
           selectablePackages,
            p =>
            {
                if (p == null) return "";
                var multipleVersions = packages.Where(pp => pp.Name == p.Name);
                var versions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());

                var switchedReference = multipleVersions.FirstOrDefault(pp => pp.IsSwitched)?.SwitchReference;

                return switchedReference != null ?
                    $"[olive]Switched[/]: [purple]{p.Name}[/] [grey]({versions})[/] => [aqua]{switchedReference}[/]" :
                    $"{p.Name} [grey]({versions})[/]";
            });
        if (selectedPackage == null) throw new TaskCanceledException("Cancelled");

        var multipleVersions = packages.Where(p => p.Name == selectedPackage.Name);
        var selectedVersions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());

        var switchedReference = workSolution.Projects
            .SelectMany(p => p.ProjectReferences)
            .FirstOrDefault(p => p.SwitchReference == selectedPackage.Name);
        var referenceText = switchedReference != null ? $" => [aqua]{switchedReference.Name}[/]" : "";


        AnsiConsole.MarkupLine($"\nSelected package: [purple]{selectedPackage.Name}[/] [gray]({selectedVersions})[/]{referenceText} in [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]");
        return selectedPackage;
    }


    private static Project PromptTargetProject(Solution targetSolution)
    {
        if (!Utils.IsConsoleInteractive()) return targetSolution.Projects.First(p => p.SpecifiedPath == "src/Scrutor/Scrutor.csproj");

        var project = Utils.SelectionPrompt(
            $"Select a target project in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]:",
            targetSolution.Projects.Prepend(null!),
            p =>
            {
                if (p == null) return "";
                var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.Name ?? p.Name).ToList();
                var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                return $"{p.Name}{referencesText}";
            });
        if (project == null) throw new TaskCanceledException("Cancelled");

        AnsiConsole.MarkupLine($"Selected Project: [blue]{project.Name}[/] in [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]");
        return project;
    }
}