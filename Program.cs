using System.Diagnostics;
using Microsoft.Build.Construction;
using System.Text.RegularExpressions;

namespace pswitch;

class Program
{
    struct PackageReference
    {
        public string Name { get; set; }
        public string RequestedVersion { get; set; }
        public string ResolvedVersion { get; set; }
    }

    static void Main(string[] args)
    {
        string solutionPath = @"/workspaces/pswitch/pswitch.sln"; // Hardcoded solution path

        if (!File.Exists(solutionPath))
        {
            Console.WriteLine("Solution file not found.");
            return;
        }

        var projects = GetProjectsFromSolution(solutionPath);

        foreach (var projectPath in projects)
        {
            Console.WriteLine($"\nProject: {projectPath}");
            var packageReferences = ListPackageReferences(projectPath);

            foreach (var package in packageReferences)
            {
                Console.WriteLine($"Package: {package.Name}, Requested: {package.RequestedVersion}, Resolved: {package.ResolvedVersion}");
            }
        }
    }

    static List<string> GetProjectsFromSolution(string solutionPath)
    {
        var projects = new List<string>();
        var solution = SolutionFile.Parse(solutionPath);

        foreach (var project in solution.ProjectsInOrder)
        {
            if (project.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat)
            {
                projects.Add(project.AbsolutePath);
            }
        }

        return projects;
    }

    static List<PackageReference> ListPackageReferences(string projectPath)
    {
        var packageReferences = new List<PackageReference>();

        if (!File.Exists(projectPath))
        {
            Console.WriteLine("Project file not found.");
            return packageReferences;
        }

        var processStartInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"list \"{projectPath}\" package",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = Process.Start(processStartInfo))
        {
            using (var reader = process.StandardOutput)
            {
                string result = reader.ReadToEnd();
                var lines = result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                // Regex to match package information lines
                var regex = new Regex(@">\s+(?<Name>\S+)\s+(?<Requested>\S+)\s+(?<Resolved>\S+)\s*");

                foreach (var line in lines)
                {
                    var match = regex.Match(line);
                    if (match.Success)
                    {
                        var packageReference = new PackageReference
                        {
                            Name = match.Groups["Name"].Value,
                            RequestedVersion = match.Groups["Requested"].Value,
                            ResolvedVersion = match.Groups["Resolved"].Value
                        };
                        packageReferences.Add(packageReference);
                    }
                }
            }
        }

        return packageReferences;
    }
}