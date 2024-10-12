using System.Diagnostics;
using System.Xml.Linq;

namespace pswitch;

class Program
{
    record PackageReference(string Name, string RequestedVersion);

    record ProjectReference(string RelativePath, string AbsolutePath);

    static void Main(string[] args)
    {
        string solutionPath = @"/workspaces/Dependinator/Dependinator.sln"; // Hardcoded solution path

        if (!File.Exists(solutionPath))
        {
            Console.WriteLine("Solution file not found.");
            return;
        }

        var projects = GetProjectsFromSolution(solutionPath);

        foreach (var project in projects)
        {
            Console.WriteLine($"\nProject: {project.RelativePath} (Absolute: {project.AbsolutePath})");
            var packageReferences = ListPackageReferences(project.AbsolutePath);

            foreach (var package in packageReferences)
            {
                Console.WriteLine($"Package: {package.Name}, Requested: {package.RequestedVersion}");
            }
        }
    }

    static List<ProjectReference> GetProjectsFromSolution(string solutionPath)
    {
        var projects = new List<ProjectReference>();
        var solutionDirectory = Path.GetDirectoryName(solutionPath);

        string result = ExecuteCommand("dotnet", $"sln \"{solutionPath}\" list");
        var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.EndsWith(".csproj"))
            {
                var relativePath = line.Trim();
                var absolutePath = Path.GetFullPath(relativePath, solutionDirectory);
                projects.Add(new ProjectReference(relativePath, absolutePath));
            }
        }

        return projects;
    }

    static string ExecuteCommand(string fileName, string arguments)
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

        using (var process = Process.Start(processStartInfo))
        {
            using (var reader = process.StandardOutput)
            using (var errorReader = process.StandardError)
            {
                string output = reader.ReadToEnd();
                string errors = errorReader.ReadToEnd();

                if (!string.IsNullOrEmpty(errors))
                {
                    Console.WriteLine($"Error executing command: {errors}");
                }

                return output;
            }
        }
    }

    static List<PackageReference> ListPackageReferences(string projectPath)
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
                var requestedVersion = element.Attribute("Version")?.Value;

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
}