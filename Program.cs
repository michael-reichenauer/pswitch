using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;



class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultTargetSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");



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

            AnsiConsole.MarkupLine($"\nSelected package: [blue]{selectedPackage.Name}[/] [gray]({selectedVersions})[/] in solution [green]{workSolution.Name}[/] [grey]({workSolution.AbsolutePath})[/]");

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
            AnsiConsole.MarkupLine($"Adding external projects to solution in {solutionVirtualFolder}:");
            AnsiConsole.MarkupLine($"  {selectedTargetProject.Name} [grey]({selectedTargetProject.AbsolutePath})[/]");
            foreach (var project in selectedTargetProject.ProjectReferences)
            {
                AnsiConsole.MarkupLine($"  {project.Name} [grey]({project.AbsolutePath})[/]");
            }

            AnsiConsole.MarkupLine($"\nSwitching package to project reference in projects:");
            foreach (var project in workSolution.Projects.Where(p => p.PackageReferences.Any(r => r.Name == selectedPackage.Name)))
            {
                AnsiConsole.MarkupLine($"  {project.Name}: [blue]{selectedPackage.Name}[/] [grey]({project.PackageReferences.First(r => r.Name == selectedPackage.Name).Version})[/] => [blue]{selectedTargetProject.Name}[/] [grey]({selectedTargetProject.AbsolutePath})[/]");
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
        var output = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{project.AbsolutePath}\" ");
        AnsiConsole.MarkupLine($"   Added: [blue]{project.Name}[/] [grey]({project.AbsolutePath})[/]");
        AnsiConsole.WriteLine(output);

        // Add project references to the solution
        foreach (var reference in project.ProjectReferences)
        {
            output = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" add --solution-folder \"{solutionFolderName}\" \"{reference.AbsolutePath}\" ");
            AnsiConsole.MarkupLine($"  Added: [blue]{reference.Name}[/] [grey]({reference.AbsolutePath})[/]");
            AnsiConsole.WriteLine(output);
        }
    }



    static void SwitchProjectReferenceToProject(Project project, PackageReference selectedPackage, Project selectedTargetProject)
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

            Console.WriteLine($"Added project reference to {selectedTargetProject.Name} in {absoluteProjectPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating project file: {ex.Message}");
        }
    }
}

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

    static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        string result = Cmd.Execute("dotnet", $"sln \"{solutionPath}\" list");
        var lines = result.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        return lines.Where(line => line.EndsWith(".csproj")).Select(line => line.Trim()).ToList();
    }
}


record Project(
    string Name,
    string SpecifiedPath,
    string AbsolutePath,
    IReadOnlyList<ProjectReference> ProjectReferences,
    IReadOnlyList<PackageReference> PackageReferences)

{
    public static Project Parse(string absolutePath, string specifiedPath)
    {
        var name = Path.GetFileNameWithoutExtension(absolutePath);

        var packageReferences = GetPackageReferences(absolutePath);
        var projectReferences = GetProjectReferences(name, absolutePath);

        return new(name, specifiedPath, absolutePath, projectReferences, packageReferences);
    }

    static List<PackageReference> GetPackageReferences(string path)
    {
        var packageReferences = new List<PackageReference>();

        var xdoc = XDocument.Load(path);
        var packageReferenceElements = xdoc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference");

        foreach (var element in packageReferenceElements)
        {
            var name = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value ?? "";

            if (string.IsNullOrEmpty(name)) continue;
            packageReferences.Add(new PackageReference(name, version));
        }

        return packageReferences;
    }


    static List<ProjectReference> GetProjectReferences(string projectPath)
    {
        var projectReferences = new List<ProjectReference>();

        var projectFolder = Path.GetDirectoryName(projectPath) ?? "";


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
                if (!File.Exists(absolutePath))
                {
                    AnsiConsole.MarkupLine($"[yellow]Project '{projectPath}' reference to '{specifiedPath}' not found at '{absolutePath}'[/]");
                    continue;
                }
                projectReferences.Add(new ProjectReference(name, specifiedPath, absolutePath));
            }
        }


        return projectReferences;
    }

}

record PackageReference(string Name, string Version);
record ProjectReference(string Name, string SpecifiedPath, string AbsolutePath);



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

    public static bool Confirm(string prompt, bool defaultValue = true)
    {
        try
        {
            return AnsiConsole.Confirm(prompt, defaultValue);
        }
        catch (InvalidOperationException)
        {   // Invalid terminal (debugging in Visual Studio Code)
            return !defaultValue;
        }
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