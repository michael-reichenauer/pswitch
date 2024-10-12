using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace pswitch;

class Program
{
    static readonly string solutionPath = Path.GetFullPath(@"/workspaces/Dependinator/Dependinator.sln"); // Normalized hardcoded solution path

    record Solution(string AbsolutePath, IReadOnlyList<Project> Projects);
    record Project(
        string RelativePath,
        string AbsolutePath,
        IReadOnlyList<ProjectReference> ProjectReferences,
        IReadOnlyList<PackageReference> PackageReferences);
    record PackageReference(string Name, string Version);
    record ProjectReference(string SpecifiedPath, string AbsolutePath);


    static void Main(string[] args)
    {
        if (!File.Exists(solutionPath))
        {
            Console.WriteLine("Solution file not found.");
            return;
        }

        var solution = ParseSolution(solutionPath);

        Console.WriteLine($"\nSolution: {solution.AbsolutePath}");

        foreach (var project in solution.Projects)
        {
            Console.WriteLine($"\n  Project: {project.RelativePath} (path: {project.AbsolutePath})");

            Console.WriteLine("     Packages:");
            foreach (var package in project.PackageReferences)
            {
                Console.WriteLine($"       {package.Name} ({package.Version})");
            }

            Console.WriteLine("     Project References:");
            foreach (var projectRef in project.ProjectReferences)
            {
                Console.WriteLine($"       {projectRef.SpecifiedPath}, Path: {projectRef.AbsolutePath}");
            }
        }
    }

    static Solution ParseSolution(string solutionPath)
    {
        var absolutePath = Path.GetFullPath(solutionPath);
        var solutionDirectory = Path.GetFullPath(Path.GetDirectoryName(solutionPath) ?? "");

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