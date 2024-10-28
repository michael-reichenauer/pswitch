using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Spectre.Console;

namespace pswitch;

record Package(string Name, string Version, bool IsSwitched, string SwitchReference);

record Project(
    string Name,
    string SpecifiedPath,
    string AbsolutePath,
    IReadOnlyList<Project> ProjectReferences,
    IReadOnlyList<Package> PackageReferences,
    bool IsSwitched = false, string SwitchReference = "")
{
    public static Project Parse(string projectFilePath, string specifiedPath, bool IsSwitched = false, string SwitchReference = "")
    {
        var name = Path.GetFileName(projectFilePath);

        var packageReferences = GetPackageReferences(projectFilePath);
        var projectReferences = GetProjectReferences(projectFilePath);

        return new(name, specifiedPath, projectFilePath, projectReferences, packageReferences, IsSwitched, SwitchReference);
    }

    public IReadOnlyList<Project> GetReferencedProjectIncludeTransitive()
    {
        var uniqueProjects = new HashSet<string>();
        var allProjects = new List<Project>();

        void AddProjectAndTransitives(Project project)
        {
            if (uniqueProjects.Add(project.AbsolutePath))
            {
                allProjects.Add(project);
                foreach (var reference in project.ProjectReferences)
                {
                    AddProjectAndTransitives(reference);
                }
            }
        }

        foreach (var project in ProjectReferences)
        {
            AddProjectAndTransitives(project);
        }

        return allProjects;
    }

    public void SwitchPackageToProjectReference(string packageName, Project targetProject)
    {
        var projectReferencePath = Utils.GetRelativePath(AbsolutePath, targetProject.AbsolutePath);

        var textFile = TextFile.Read(AbsolutePath);
        var originalFileText = textFile.Text;
        var xml = XDocument.Load(AbsolutePath);

        // Get the first PackageReference element with the selected package name
        var packageReferenceElement = xml.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference" &&
                e.Attribute("Include")?.Value == packageName)
            .First();
        var originalPackageReferenceText = packageReferenceElement.ToString();

        // Inactivate the package reference using a condition to disable it
        packageReferenceElement.SetAttributeValue("Condition", $"'$(PSWITCH)' == '{projectReferencePath}'");
        var disabledPackageReferenceText = packageReferenceElement.ToString();

        // Create a new ProjectReference element with a condition that allows it to be active, but shows which package reference it replaces
        var projectReferenceElement = new XElement("ProjectReference");
        projectReferenceElement.SetAttributeValue("Include", projectReferencePath);
        projectReferenceElement.SetAttributeValue("Condition", $"'$(PSWITCH)' != '{packageName}'");
        var targetProjectReferenceText = projectReferenceElement.ToString();

        // Replace the original package reference with the disabled package reference and the target project reference
        var updatedFileText = originalFileText.Replace(originalPackageReferenceText, disabledPackageReferenceText + targetProjectReferenceText);
        textFile.Write(updatedFileText);

        AnsiConsole.MarkupLine($"  Switched [blue]{Name}[/] package [purple]{packageName}[/] reference => [aqua]{targetProject.Name}[/] [grey]{targetProject.AbsolutePath}[/] ");
    }


    internal void RestorePackageToPackageReference(string packageName)
    {
        var selectedPackage = PackageReferences.First(p => p.Name == packageName);
        var targetProject = ProjectReferences.First(p => p.SwitchReference == packageName);

        var textFile = TextFile.Read(AbsolutePath);
        var originalFileText = textFile.Text;
        var xml = XDocument.Load(AbsolutePath);

        var packageReferenceElement = xml.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference" &&
                e.Attribute("Include")?.Value == selectedPackage.Name)
            .First();
        var originalPackageReferenceText = packageReferenceElement.ToString();

        var conditionsAttribute = packageReferenceElement.Attribute("Condition")
            ?? throw new Exception("Condition attribute not found in package reference element");
        conditionsAttribute.Remove();
        var restoredPackageReferenceText = packageReferenceElement.ToString();

        var projectReferenceElement = xml.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference" &&
                e.Attribute("Include")?.Value == selectedPackage.SwitchReference)
            .First();
        var originalProjectReferenceText = projectReferenceElement.ToString();

        var updatedFileText = originalFileText.Replace(originalPackageReferenceText, restoredPackageReferenceText);
        updatedFileText = updatedFileText.Replace(originalProjectReferenceText, "");

        textFile.Write(updatedFileText);

        AnsiConsole.MarkupLine($"  Restored [blue]{Name}[/] package [purple]{packageName}[/] reference [grey](removed: => {targetProject.AbsolutePath}[/])");
    }


    static List<Package> GetPackageReferences(string projectFilePath)
    {
        var xml = XDocument.Load(projectFilePath);
        var packageReferenceElements = xml.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference");

        var packageReferences = new List<Package>();
        foreach (var element in packageReferenceElements)
        {
            var name = element.Attribute("Include")?.Value;
            var version = element.Attribute("Version")?.Value ?? "";
            var condition = element.Attribute("Condition")?.Value ?? "";
            var (isSwitched, reference) = ParseCondition(condition);

            if (string.IsNullOrEmpty(name)) continue;  // Skip "Update" package references for now!!
            packageReferences.Add(new Package(name, version, isSwitched, reference));
        }

        return packageReferences;
    }


    static List<Project> GetProjectReferences(string projectFilePath)
    {
        var projectFolder = Path.GetDirectoryName(projectFilePath) ?? "";

        var xml = XDocument.Load(projectFilePath);
        var projectReferenceElements = xml.Descendants().Where(e => e.Name.LocalName == "ProjectReference");

        var projectReferences = new List<Project>();
        foreach (var element in projectReferenceElements)
        {
            var specifiedPath = element.Attribute("Include")?.Value;
            var condition = element.Attribute("Condition")?.Value ?? "";
            var (isSwitched, switchReference) = ParseCondition(condition);

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

            projectReferences.Add(Parse(absolutePath, specifiedPath, isSwitched, switchReference));
        }

        return projectReferences;
    }


    static (bool isSwitched, string switchReference) ParseCondition(string condition)
    {
        var pattern = @"'\$\(PSWITCH\)'\s*(?<operator>==|!=)\s*'(?<rightSide>.+)'";
        var match = Regex.Match(condition, pattern);

        if (!match.Success) return (false, "");

        var rightSide = match.Groups["rightSide"].Value;

        return (true, rightSide);
    }

}
