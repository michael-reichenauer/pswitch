using Spectre.Console;

namespace pswitch;


class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    static readonly string SolutionVirtualFolder = "ExternalProjects";

    static void Main(string[] args)
    {
        try
        {
            // Prompt user to select a package from the solution to switch to a target solution project
            var workSolutionPath = GetWorkSolutionPath(args);
            var workSolution = Solution.Parse(workSolutionPath);
            var (selectedPackageName, selectedPackageVersions) = PromptPackage(workSolution);

            // Prompt user to select a target project from the target solution to be referenced instead of the selected package
            var targetSolutionPath = GetTargetSolutionPath(args);
            var targetSolution = Solution.Parse(targetSolutionPath);
            Project selectedTargetProject = PromptTargetProject(targetSolution);

            ShowSummeryOfChanges(workSolution, selectedPackageName, selectedTargetProject);

            PromptToConfirmChanges();

            AddProjectsAndSwitchReferences(workSolution, selectedPackageName, selectedTargetProject);
            AnsiConsole.MarkupLine("\n[green]Done![/]");
        }
        catch (TaskCanceledException ex)
        {
            AnsiConsole.MarkupLine($"\n[red]{ex.Message}[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"\n[red]Error[/]:");
            Console.WriteLine($"{ex}");
        }
    }


    static string GetWorkSolutionPath(string[] args)
    {
        string path = args.Length >= 1 ? Path.GetFullPath(args[0]) : DefaultWorkSolutionPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"Solution file not found '{path}'");
        return path;
    }

    static string GetTargetSolutionPath(string[] args)
    {
        string path = args.Length >= 2 ? Path.GetFullPath(args[1]) : DefaultTargetSolutionPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"Solution file not found '{path}'");
        return path;
    }

    static (string, string) PromptPackage(Solution workSolution)
    {
        var packages = workSolution.Projects.SelectMany(p => p.PackageReferences).ToList();
        if (!Utils.IsConsoleInteractive())
        {
            var scrutorPackages = packages.Where(p => p.Name == "Scrutor");
            var versions = string.Join(". ", scrutorPackages.Select(p => p.Version).Distinct());
            return ("Scrutor", versions);
        }

        // Prompt user to select a package from the solution to switch to a target solution project 
        var selectedPackage = Utils.Prompt(
            $"\nSelect a package to switch in solution [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]:",
            packages.DistinctBy(p => p.Name),
            p =>
            {
                var multipleVersions = packages.Where(pp => pp.Name == p.Name);
                var versions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());
                return $"{p.Name}   [grey]({versions})[/]";
            });
        var multipleVersions = packages.Where(p => p.Name == selectedPackage.Name);
        var selectedVersions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());

        AnsiConsole.MarkupLine($"\nSelected package: [purple]{selectedPackage.Name}[/] [gray]({selectedVersions})[/]");
        return (selectedPackage.Name, selectedVersions);
    }


    private static Project PromptTargetProject(Solution targetSolution)
    {
        if (!Utils.IsConsoleInteractive()) return targetSolution.Projects.First(p => p.SpecifiedPath == "src/Scrutor/Scrutor.csproj");

        var project = Utils.Prompt(
            $"\nSelect a target project in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]:",
            targetSolution.Projects,
            p =>
            {
                var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.Name ?? p.Name).ToList();
                var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                return $"{p.Name}{referencesText}";
            });

        AnsiConsole.MarkupLine($"\nSelected Project: [blue]{project.SpecifiedPath}[/] in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]");
        return project;
    }

    private static void ShowSummeryOfChanges(Solution workSolution, string selectedPackageName, Project selectedTargetProject)
    {
        AnsiConsole.MarkupLine($"\n\n[grey]--------------------------------------------------------------[/]");
        AnsiConsole.MarkupLine("Summary of changes to be performed:\n");
        AnsiConsole.MarkupLine($"Adding external projects to [green]{workSolution.Name}[/]/[teal]{SolutionVirtualFolder}[/] solution folder:");
        AnsiConsole.MarkupLine($"  [aqua]{selectedTargetProject.Name}[/] [grey]({selectedTargetProject.AbsolutePath})[/]");
        foreach (var project in selectedTargetProject.GetReferencedProjectIncludeTransitive())
        {
            AnsiConsole.MarkupLine($"  [aqua]{project.Name}[/] [grey]({project.AbsolutePath})[/]");
        }

        AnsiConsole.MarkupLine($"\nSwitching package to project reference in projects:");
        foreach (var project in workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackageName)))
        {
            AnsiConsole.MarkupLine($"  [blue]{project.Name}[/]: [purple]{selectedPackageName}[/] [grey]({project.PackageReferences.First(r => r.Name == selectedPackageName).Version})[/] => [aqua]{selectedTargetProject.Name}[/] [grey]({selectedTargetProject.AbsolutePath})[/]");
        }
    }


    private static void PromptToConfirmChanges()
    {
        if (!Utils.IsConsoleInteractive()) return;

        AnsiConsole.MarkupLine("");

        if (!AnsiConsole.Confirm("Do you want to continue?", false))
        {
            throw new TaskCanceledException("Cancelled");
        }
    }


    static void AddProjectsAndSwitchReferences(Solution workSolution, string selectedPackageName, Project selectedTargetProject)
    {
        AnsiConsole.MarkupLine("\n[green]Proceeding...[/]");

        workSolution.AddProjectsToSolution(selectedTargetProject, SolutionVirtualFolder);

        AnsiConsole.MarkupLine("\nSwitching package references to project references:");
        var projectsToSwitch = workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackageName));
        foreach (var project in projectsToSwitch)
        {
            project.SwitchPackageToProjectReference(selectedPackageName, selectedTargetProject);
        }
    }
}