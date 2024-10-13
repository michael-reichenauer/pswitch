using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace pswitch;

class Program
{
    static readonly string DefaultWorkSolutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln");
    static readonly string DefaultOtherSolutionPath = Path.GetFullPath(@"/workspaces/Scrutor/Scrutor.sln");

    record Solution(string AbsolutePath, IReadOnlyList<Project> Projects);
    record Project(
        string SpecifiedPath,
        string AbsolutePath,
        IReadOnlyList<ProjectReference> ProjectReferences,
        IReadOnlyList<PackageReference> PackageReferences);
    record PackageReference(string Name, string Version);
    record ProjectReference(string SpecifiedPath, string AbsolutePath);


    static void Main(string[] args)
    {
        string workSolutionPath = DefaultWorkSolutionPath;
        string otherSolutionPath = DefaultOtherSolutionPath;

        if (args.Length >= 2)
        {
            workSolutionPath = Path.GetFullPath(args[0]);
            otherSolutionPath = Path.GetFullPath(args[1]);
        }

        if (!File.Exists(workSolutionPath))
        {
            Console.WriteLine($"Solution file not found '{workSolutionPath}'");
            return;
        }
        if (!File.Exists(otherSolutionPath))
        {
            Console.WriteLine($"Solution file not found '{otherSolutionPath}'");
            return;
        }

        var workSolution = ParseSolution(workSolutionPath);
        var packages = workSolution.Projects.SelectMany(p => p.PackageReferences).ToList();

        Console.WriteLine($"\nWork Solution: {workSolution.AbsolutePath}");
        Console.WriteLine($"  Packages to switch:");

        foreach (var package in packages.DistinctBy(p => p.Name))
        {
            var multipleVersions = packages.Where(p => p.Name == package.Name);
            var versions = string.Join(". ", multipleVersions.Select(p => p.Version).Distinct());
            Console.WriteLine($"    Package: {package.Name} ({versions})");
        }

        var selectedPage = packages.First(p => p.Name == "Scrutor");

        var otherSolution = ParseSolution(otherSolutionPath);
        Console.WriteLine("\n\n------------------------------------");
        Console.WriteLine($"Other Solution: {otherSolution.AbsolutePath}");
        Console.WriteLine($"  Projects to switch to:");
        foreach (var project in otherSolution.Projects)
        {
            Console.WriteLine($"    {project.SpecifiedPath} (path: {project.AbsolutePath})");
            var projectReferences = project.ProjectReferences;
            foreach (var projectReference in projectReferences)
            {
                var path = otherSolution.Projects.FirstOrDefault(p => p.AbsolutePath == projectReference.AbsolutePath)?.SpecifiedPath ?? projectReference.SpecifiedPath;
                Console.WriteLine($"        ({path})");
            }
        }
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