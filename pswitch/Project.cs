using System.Runtime.InteropServices;
using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;

record Project(
    string Name,
    string SpecifiedPath,
    string AbsolutePath,
    IReadOnlyList<Project> ProjectReferences,
    IReadOnlyList<Package> PackageReferences,
    string Condition)

{
    public static Project Parse(string projectFilePath, string specifiedPath, string condition)
    {
        var name = Path.GetFileNameWithoutExtension(projectFilePath);

        var packageReferences = GetPackageReferences(projectFilePath);
        var projectReferences = GetProjectReferences(projectFilePath);

        return new(name, specifiedPath, projectFilePath, projectReferences, packageReferences, condition);
    }

    static List<Package> GetPackageReferences(string projectFilePath)
    {
        var xdoc = XDocument.Load(projectFilePath);
        var packageReferenceElements = xdoc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference");

        var packageReferences = new List<Package>();
        foreach (var element in packageReferenceElements)
        {
            var name = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value ?? "";
            var condition = element.Attribute("Condition")?.Value ?? "";

            if (string.IsNullOrEmpty(name)) continue;  // Skip "Update" package references for now!!
            packageReferences.Add(new Package(name, version, condition));
        }

        return packageReferences;
    }


    static List<Project> GetProjectReferences(string projectFilePath)
    {
        var projectFolder = Path.GetDirectoryName(projectFilePath) ?? "";

        var xdoc = XDocument.Load(projectFilePath);
        var projectReferenceElements = xdoc.Descendants().Where(e => e.Name.LocalName == "ProjectReference");

        var projectReferences = new List<Project>();
        foreach (var element in projectReferenceElements)
        {
            var specifiedPath = element.Attribute("Include")?.Value;
            var condition = element.Attribute("Condition")?.Value ?? "";

            if (string.IsNullOrEmpty(specifiedPath)) throw new Exception($"Empty project reference named in project file '{projectFilePath}'");

            var relativePath = specifiedPath.Trim();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {   // Convert backslashes to forward slashes for Linux or macOS
                relativePath = relativePath.Replace('\\', '/');
            }

            var absolutePath = Path.GetFullPath(Path.Combine(projectFolder, relativePath));

            if (!File.Exists(absolutePath))
            {
                AnsiConsole.MarkupLine($"[yellow]Project '{projectFilePath}' reference to '{specifiedPath}' not found at '{absolutePath}'[/]");
                continue;
            }

            projectReferences.Add(Parse(absolutePath, specifiedPath, condition));

        }

        return projectReferences;
    }
}
