using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;

class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    record Solution(string Name, string AbsolutePath, IReadOnlyList<Project> Projects);
    record Project(
        string Name,
        string SpecifiedPath,
        string AbsolutePath,
        IReadOnlyList<ProjectReference> ProjectReferences,
        IReadOnlyList<PackageReference> PackageReferences);
    record PackageReference(string Name, string Version);
    record ProjectReference(string Name, string SpecifiedPath, string AbsolutePath);

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


            // Prompt user to select a package from the solution to switch to a target solution project 
            var workSolution = ParseSolution(workSolutionPath);
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

            AnsiConsole.MarkupLine($"\nSelected package: [blue]{selectedPackage.Name}[/] [gray]({selectedVersions})[/] in solution [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]");

            // Prompt user to select a target project from the target solution to be referenced instead of the selected package
            var targetSolution = ParseSolution(targetSolutionPath);
            var selectedProject = Utils.Prompt($"\nSelect a target project in [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]:", targetSolution.Projects,
                p =>
                {
                    var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.Name ?? p.Name).ToList();
                    var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                    return $"{p.Name}{referencesText}";
                }, targetSolution.Projects.First(p => p.SpecifiedPath == "src/Scrutor/Scrutor.csproj"));

            AnsiConsole.MarkupLine($"\nSelected Project: [blue]{selectedProject.SpecifiedPath}[/] in solution [green]{targetSolution.Name}[/] [grey]({targetSolution.AbsolutePath})[/]");

            AnsiConsole.MarkupLine($"\n\n[grey]--------------------------------------------------------------[/]");
            AnsiConsole.MarkupLine("Summary of changes to be performed:\n");
            AnsiConsole.MarkupLine($"Adding external projects to solution in {solutionVirtualFolder}:");
            AnsiConsole.MarkupLine($"  {selectedProject.Name} [grey]({selectedProject.AbsolutePath})[/]");
            foreach (var project in selectedProject.ProjectReferences)
            {
                AnsiConsole.MarkupLine($"  {project.Name} [grey]({project.AbsolutePath})[/]");
            }

            AnsiConsole.MarkupLine($"\nSwitching package to project reference in projects:");
            foreach (var project in workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name)))
            {
                AnsiConsole.MarkupLine($"  {project.Name}: [blue]{selectedPackage.Name}[/] [grey]({project.PackageReferences.First(r => r.Name == selectedPackage.Name).Version})[/] => [blue]{selectedProject.Name}[/] [grey]({selectedProject.AbsolutePath})[/]");
            }


            AnsiConsole.MarkupLine("");
            if (!AnsiConsole.Confirm("Do you want to continue?", false))
            {
                AnsiConsole.MarkupLine("[red]Cancelled[/]");
                return;
            }

            AnsiConsole.MarkupLine("\n[green]Proceeding...[/]");

            // AddProjectsToSolution(workSolutionPath, selectedProject, solutionVirtualFolder);

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
        // Add the main project to the solution
        Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{project.AbsolutePath}\" ");
        AnsiConsole.MarkupLine($"  Added: [blue]{project.Name}[/] [grey]({project.AbsolutePath})[/]");

        // Add project references to the solution
        foreach (var reference in project.ProjectReferences)
        {
            Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{reference.AbsolutePath}\" ");
            AnsiConsole.MarkupLine($"  Added: [blue]{reference.Name}[/] [grey]({reference.AbsolutePath})[/]");
        }
    }


    static Solution ParseSolution(string solutionPath)
    {
        var solutionName = Path.GetFileNameWithoutExtension(solutionPath);
        var solutionAbsolutePath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? "";

        var solutionProjectPaths = GetSolutionProjectPaths(solutionAbsolutePath);

        List<Project> solutionProjects = [];
        foreach (var projectPath in solutionProjectPaths)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var projectAbsolutePath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
            var projectPackageReferences = GetProjectPackageReferences(projectAbsolutePath);
            var projectProjectReferences = GetProjectProjectReferences(projectAbsolutePath);

            solutionProjects.Add(new(projectName, projectPath, projectAbsolutePath, projectProjectReferences, projectPackageReferences));
        }

        return new Solution(solutionName, solutionAbsolutePath, solutionProjects);
    }

    static IReadOnlyList<string> GetSolutionProjectPaths(string solutionPath)
    {
        string result = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" list");
        var lines = result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return lines.Where(line => line.EndsWith(".csproj")).Select(line => line.Trim()).ToList();
    }


    static List<PackageReference> GetProjectPackageReferences(string projectPath)
    {
        var packageReferences = new List<PackageReference>();

        if (!File.Exists(projectPath))
        {
            Console.WriteLine("Project file not found.");
            return packageReferences;
        }

        try
        {
            var xdoc = XDocument.Load(projectPath);
            var packageReferenceElements = xdoc.Descendants().Where(e => e.Name.LocalName == "PackageReference");

            foreach (var element in packageReferenceElements)
            {
                var name = element.Attribute("Include")?.Value;
                var requestedVersion = element.Attribute("Version")?.Value ?? "";

                if (!string.IsNullOrEmpty(name))
                {
                    packageReferences.Add(new PackageReference(name, requestedVersion));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading project file: {ex.Message}");
        }

        return packageReferences;
    }

    static List<ProjectReference> GetProjectProjectReferences(string projectPath)
    {
        var projectReferences = new List<ProjectReference>();

        if (!File.Exists(projectPath))
        {
            Console.WriteLine("Project file not found.");
            return projectReferences;
        }

        var projectFolder = Path.GetDirectoryName(projectPath) ?? "";

        try
        {
            var xdoc = XDocument.Load(projectPath);
            var projectReferenceElements = xdoc.Descendants().Where(e => e.Name.LocalName == "ProjectReference");

            foreach (var element in projectReferenceElements)
            {
                var specifiedPath = element.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(specifiedPath))
                {
                    var relativePath = specifiedPath.Trim();
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {   // Convert backslashes to forward slashes for Linux or macOS
                        relativePath = relativePath.Replace('\\', '/');
                    }

                    var absolutePath = Path.GetFullPath(Path.Combine(projectFolder, relativePath));
                    var name = Path.GetFileNameWithoutExtension(absolutePath);
                    projectReferences.Add(new ProjectReference(name, specifiedPath, absolutePath));
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading project file: {ex.Message}");
        }

        return projectReferences;
    }
}


class Utils
{
    record Selection<T>(string Text, T Value);

    public static string GetRelativePath(string basePath, string targetPath)
    {
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(targetPath);

        return baseUri.MakeRelativeUri(targetUri).ToString();
    }

    public static T Prompt<T>(string message, IEnumerable<T> choices, Func<T, string> textSelector, T defaultValue)
    {
        List<Selection<T>> selections = choices.Select(c => new Selection<T>(textSelector(c), c)).ToList();
        string selection;
        try
        {
            selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(message)
                .PageSize(10)
                .AddChoices(selections.Select(s => $"{s.Text}")));
        }
        catch (NotSupportedException)
        {   // Invalid terminal (debugging in Visual Studio Code)
            return defaultValue!;
        }

        var selected = selections.FirstOrDefault(s => s.Text == selection);
        if (selected == null) throw new TaskCanceledException("Selection cancelled.");

        return selected.Value;
    }
}

class Cmd
{
    public static string Execute(string fileName, string arguments)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo) ??
            throw new Exception($"Error starting process: {fileName} {arguments}");

        using var reader = process.StandardOutput;
        using var errorReader = process.StandardError;
        string output = reader.ReadToEnd();
        string errors = errorReader.ReadToEnd();

        if (!string.IsNullOrEmpty(errors))
            throw new Exception($"Error executing command: {fileName} {arguments}\nError: {errors}");

        return output;
    }
}