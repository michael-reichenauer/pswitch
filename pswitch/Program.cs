﻿using Spectre.Console;

namespace pswitch;


class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    static readonly string SolutionVirtualFolder = "ExternalProjects";

    static void Main()
    {
        try
        {
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

    private static void RestorePackage(Solution workSolution, Package selectedPackage)
    {
        var projectsToRestore = workSolution.Projects
            .Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name && r.IsSwitched))
            .ToList();

        var externalProjects = projectsToRestore
            .SelectMany(p => p.ProjectReferences.Where(r => r.SwitchReference == selectedPackage.Name))
            .DistinctBy(p => p.AbsolutePath)
            .ToList();

        AnsiConsole.MarkupLine("\nRestoring Package references from project references:");
        foreach (var project in projectsToRestore)
        {
            project.RestorePackageFromProjectReference(selectedPackage.Name);
        }

        AnsiConsole.MarkupLine($"\nRemoving projects from [green]{workSolution.Name}[/]/[teal]{SolutionVirtualFolder}[/]:");
        workSolution.RemoveProjectsToSolution(selectedPackage.Name);
    }

    private static void SwitchPackage(Solution workSolution, Package selectedPackage)
    {
        // Prompt user to select a target project from the target solution to be referenced instead of the selected package
        var targetSolutionPath = GetTargetSolutionPath();
        var targetSolution = Solution.Parse(targetSolutionPath);

        Project selectedTargetProject = PromptTargetProject(targetSolution);

        ShowSummeryOfSwitchChanges(workSolution, selectedPackage.Name, selectedTargetProject);
        PromptToConfirmChanges();

        AddProjectsAndSwitchReferences(workSolution, selectedPackage.Name, selectedTargetProject);
    }

    static void AddProjectsAndSwitchReferences(Solution workSolution, string selectedPackageName, Project selectedTargetProject)
    {
        AnsiConsole.MarkupLine($"\nAdding projects to [green]{workSolution.Name}[/]/[teal]{SolutionVirtualFolder}[/]:");
        workSolution.AddProjectsToSolution(selectedTargetProject, SolutionVirtualFolder);

        AnsiConsole.MarkupLine("\nSwitching package references to project references:");
        var projectsToSwitch = workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackageName));
        foreach (var project in projectsToSwitch)
        {
            project.SwitchPackageToProjectReference(selectedPackageName, selectedTargetProject);
        }
    }
    static string GetWorkSolutionPath()
    {
        var args = Environment.GetCommandLineArgs();
        string path = args.Length >= 2 ? Path.GetFullPath(args[1]) : DefaultWorkSolutionPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"Solution file not found '{path}'");
        return path;
    }

    static string GetTargetSolutionPath()
    {
        var args = Environment.GetCommandLineArgs();
        string path = args.Length >= 3 ? Path.GetFullPath(args[2]) : DefaultTargetSolutionPath;
        if (!File.Exists(path)) throw new FileNotFoundException($"Solution file not found '{path}'");
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
        var selectedPackage = Utils.Prompt(
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

        AnsiConsole.MarkupLine($"\nSelected package: [purple]{selectedPackage.Name}[/] [gray]({selectedVersions})[/]");
        return selectedPackage;
    }


    private static Project PromptTargetProject(Solution targetSolution)
    {
        if (!Utils.IsConsoleInteractive()) return targetSolution.Projects.First(p => p.SpecifiedPath == "src/Scrutor/Scrutor.csproj");

        var project = Utils.Prompt(
            $"\nSelect a target project in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]:",
            targetSolution.Projects.Prepend(null!),
            p =>
            {
                if (p == null) return "";
                var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.Name ?? p.Name).ToList();
                var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                return $"{p.Name}{referencesText}";
            });
        if (project == null) throw new TaskCanceledException("Cancelled");
        AnsiConsole.MarkupLine($"\nSelected Project: [blue]{project.SpecifiedPath}[/] in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]");
        return project;
    }

    private static void ShowSummeryOfSwitchChanges(Solution workSolution, string selectedPackageName, Project selectedTargetProject)
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
}