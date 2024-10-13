using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;

class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    record Solution(string AbsolutePath, IReadOnlyList<Project> Projects);
    record Project(
        string SpecifiedPath,
        string AbsolutePath,
        IReadOnlyList<ProjectReference> ProjectReferences,
        IReadOnlyList<PackageReference> PackageReferences);
    record PackageReference(string Name, string Version);
    record ProjectReference(string SpecifiedPath, string AbsolutePath);

    record Selection<T>(string Text, T Value);

    static void Main(string[] args)
    {
        try
        {
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
            AnsiConsole.MarkupLine($"\nSolution: [green]{workSolution.AbsolutePath}[/]");

            var packages = workSolution.Projects.SelectMany(p => p.PackageReferences).ToList();
            var selectedPackage = Prompt("  Select a package to switch:", packages.DistinctBy(p => p.Name),
                p =>
                {
                    var multipleVersions = packages.Where(pp => pp.Name == p.Name);
                    var versions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());
                    return $"{p.Name}   [grey]({versions})[/]";
                });

            AnsiConsole.MarkupLine($"  Selected package: [blue]{selectedPackage.Name}[/]");

            AnsiConsole.MarkupLine($"\n\n[grey]--------------------------------------------------------------[/]");

            // Prompt user to select a target project from the target solution to be referenced instead of the selected package
            var targetSolution = ParseSolution(targetSolutionPath);
            AnsiConsole.MarkupLine($"Target Solution: [green]{targetSolution.AbsolutePath}[/]");

            var selectedProject = Prompt("  Select a target project:", targetSolution.Projects,
                p =>
                {
                    var references = p.ProjectReferences.Select(r => targetSolution.Projects.FirstOrDefault(p => p.AbsolutePath == p.AbsolutePath)?.SpecifiedPath ?? p.SpecifiedPath).ToList();
                    var referencesText = $"\n     [grey]Dependencies: {string.Join(", ", references)}[/]";
                    return $"{p.SpecifiedPath}{referencesText}";
                });

            AnsiConsole.MarkupLine($"  Selected Project: [blue]{selectedProject.SpecifiedPath}[/]");
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

    static T Prompt<T>(string message, IEnumerable<T> choices, Func<T, string> textSelector)
    {
        List<Selection<T>> selections = choices.Select(c => new Selection<T>(textSelector(c), c)).ToList();

        var selection = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(message)
                .PageSize(10)
                .AddChoices(selections.Select(s => $"{s.Text}")));

        var selected = selections.FirstOrDefault(s => s.Text == selection);
        if (selected == null) throw new TaskCanceledException("Selection cancelled.");

        return selected.Value;
    }

    static Solution ParseSolution(string solutionPath)
    {
        var absolutePath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetDirectoryName(solutionPath) ?? "";

        var projectPaths = GetSolutionProjectPaths(absolutePath);

        List<Project> projects = [];
        foreach (var projectPath in projectPaths)
        {
            var absoluteProjectPath = Path.GetFullPath(Path.Combine(solutionDirectory, projectPath));
            var packageReferences = GetProjectPackageReferences(absoluteProjectPath);
            var projectReferences = GetProjectProjectReferences(absoluteProjectPath);

            projects.Add(new Project(projectPath, absoluteProjectPath, projectReferences, packageReferences));
        }

        return new Solution(absolutePath, projects);
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
                    projectReferences.Add(new ProjectReference(specifiedPath, absolutePath));
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
    public static string GetRelativePath(string basePath, string targetPath)
    {
        var baseUri = new Uri(basePath);
        var targetUri = new Uri(targetPath);

        return baseUri.MakeRelativeUri(targetUri).ToString();
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

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            Console.WriteLine("Error starting process.");
            return string.Empty;
        }
        using var reader = process.StandardOutput;
        using var errorReader = process.StandardError;
        string output = reader.ReadToEnd();
        string errors = errorReader.ReadToEnd();

        if (!string.IsNullOrEmpty(errors))
        {
            Console.WriteLine($"Error executing command: {errors}");
        }

        return output;
    }
}