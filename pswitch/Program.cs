using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;



class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    static readonly string DefaultSelectedPackage = "Scrutor";
    static readonly string DefaultSelectedTargetProject = "src/Scrutor/Scrutor.csproj";


    static void Main(string[] args)
    {
        try
        {
            string solutionVirtualFolder = "ExternalProjects";
            string workSolutionPath = DefaultWorkSolutionPath;
            string targetSolutionPath = DefaultTargetSolutionPath;

            if (args.Length >= 2)
            {   // Temporary workaround while developing
                workSolutionPath = Path.GetFullPath(args[0]);
                targetSolutionPath = Path.GetFullPath(args[1]);
            }

            if (!File.Exists(workSolutionPath)) throw new FileNotFoundException($"Solution file not found '{workSolutionPath}'");
            if (!File.Exists(targetSolutionPath)) throw new FileNotFoundException($"Solution file not found '{targetSolutionPath}'");

            workSolutionPath = Path.GetFullPath(workSolutionPath);
            targetSolutionPath = Path.GetFullPath(targetSolutionPath);

            // Prompt user to select a package from the solution to switch to a target solution project 
            var workSolution = Solution.Parse(workSolutionPath);
            var packages = workSolution.Projects.SelectMany(p => p.PackageReferences).ToList();
            var selectedPackage = Utils.Prompt($"\nSelect a package to switch in [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]:", packages.DistinctBy(p => p.Name),
                p =>
                {
                    var multipleVersions = packages.Where(pp => pp.Name == p.Name);
                    var versions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());
                    return $"{p.Name}   [grey]({versions})[/]";
                }, packages.First(p => p.Name == "Scrutor"));

            var multipleVersions = packages.Where(p => p.Name == selectedPackage.Name);
            var selectedVersions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());

            AnsiConsole.MarkupLine($"\nSelected package: [purple]{selectedPackage.Name}[/] [gray]({selectedVersions})[/] in solution [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]");

            // Prompt user to select a target project from the target solution to be referenced instead of the selected package
            var targetSolution = Solution.Parse(targetSolutionPath);
            var selectedTargetProject = Utils.Prompt($"\nSelect a target project in [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]:", targetSolution.Projects,
                p =>
                {
                    var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.Name ?? p.Name).ToList();
                    var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                    return $"{p.Name}{referencesText}";
                }, targetSolution.Projects.First(p => p.SpecifiedPath == "src/Scrutor/Scrutor.csproj"));

            AnsiConsole.MarkupLine($"\nSelected Project: [blue]{selectedTargetProject.SpecifiedPath}[/] in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]");

            AnsiConsole.MarkupLine($"\n\n[grey]--------------------------------------------------------------[/]");
            AnsiConsole.MarkupLine("Summary of changes to be performed:\n");
            AnsiConsole.MarkupLine($"Adding external projects to solution in {solutionVirtualFolder} folder:");
            AnsiConsole.MarkupLine($"  [blue]{selectedTargetProject.Name}[/] [grey]({selectedTargetProject.AbsolutePath})[/]");
            foreach (var project in selectedTargetProject.ProjectReferences)
            {
                AnsiConsole.MarkupLine($"  [blue]{project.Name}[/] [grey]({project.AbsolutePath})[/]");
            }

            AnsiConsole.MarkupLine($"\nSwitching package to project reference in projects:");
            foreach (var project in workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name)))
            {
                AnsiConsole.MarkupLine($"  [blue]{project.Name}[/]: [purple]{selectedPackage.Name}[/] [grey]({project.PackageReferences.First(r => r.Name == selectedPackage.Name).Version})[/] => [blue]{selectedTargetProject.Name}[/] [grey]({selectedTargetProject.AbsolutePath})[/]");
            }

            AnsiConsole.MarkupLine("");
            if (!Utils.Confirm("Do you want to continue?", false))
            {
                AnsiConsole.MarkupLine("[red]Cancelled[/]");
                return;
            }

            AnsiConsole.MarkupLine("\n[green]Proceeding...[/]");

            AddProjectsToSolution(workSolutionPath, selectedTargetProject, solutionVirtualFolder);

            foreach (var project in workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name)))
            {
                SwitchProjectReferenceToProject(project, selectedPackage, selectedTargetProject);
            }

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



    static void AddProjectsToSolution(string solutionPath, Project project, string solutionFolderName)
    {
        AnsiConsole.MarkupLine($"Adding project to solution:");

        // Add the main project to the solution
        Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{project.AbsolutePath}\" ");
        AnsiConsole.MarkupLine($"   Added: [blue]{project.Name}[/] [grey]({project.AbsolutePath})[/]");

        // Add project references to the solution
        foreach (var reference in project.ProjectReferences)
        {
            Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{reference.AbsolutePath}\" ");
            AnsiConsole.MarkupLine($"  Added: [blue]{reference.Name}[/] [grey]({reference.AbsolutePath})[/]");
        }
    }



    static void SwitchProjectReferenceToProject(Project project, Package selectedPackage, Project selectedTargetProject)
    {
        try
        {
            var absoluteProjectPath = project.AbsolutePath;
            var projectReferencePath = Utils.GetRelativePath(absoluteProjectPath, selectedTargetProject.AbsolutePath);

            var xdoc = XDocument.Load(absoluteProjectPath);

            // Get the first PackageReference element with the selected package name
            var packageReferenceElement = xdoc.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference" &&
                    e.Attribute("Include")?.Value == selectedPackage.Name)
                .First();
            var originalPackageReferenceText = packageReferenceElement.ToString();

            // Inactivate the package reference using a condition to disable it
            packageReferenceElement.SetAttributeValue("Condition", $"'$(PSWITCH)' == '{projectReferencePath}'");
            var disabledPackageReferenceText = packageReferenceElement.ToString();

            // Create a new ProjectReference element with a condition that allows it to be active, but shows which package reference it replaces
            var projectReferenceElement = new XElement("ProjectReference");
            projectReferenceElement.SetAttributeValue("Include", projectReferencePath);
            projectReferenceElement.SetAttributeValue("Condition", $"'$(PSWITCH)' != '{selectedPackage.Name}'");
            var targetProjectReferenceText = projectReferenceElement.ToString();

            // Replace the original package reference with the disabled package reference and the target project reference
            var originalFileText = File.ReadAllText(absoluteProjectPath);
            var updatedFileText = originalFileText.Replace(originalPackageReferenceText, disabledPackageReferenceText + targetProjectReferenceText);
            File.WriteAllText(absoluteProjectPath, updatedFileText);

            AnsiConsole.MarkupLine($"[blue]{project.Name}[/]: Switched package [purple]{selectedPackage.Name}[/] reference to [blue]{selectedTargetProject.Name}[/]from  [grey]{selectedTargetProject.AbsolutePath}[/] ");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating project file: {ex.Message}");
        }
    }
}

record Package(string Name, string Version, string Condition)
{

};
