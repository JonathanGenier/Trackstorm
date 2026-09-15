using Trackstorm.Core;

namespace Trackstorm.Core.Tests.Architecture;

/// <summary>
/// Verifies the compile-time boundary around the authoritative Core assembly.
/// </summary>
[TestFixture]
internal sealed class CoreDependencyTests
{
    private static readonly string[] ProhibitedDependencies =
    [
        "Trackstorm.Client",
        "Godot",
        "GameNetworkingSockets",
        "GnsSharp",
        "Epic.OnlineServices",
    ];

    /// <summary>
    /// Verifies that Core remains independent from Client, Godot, and native transport assemblies.
    /// </summary>
    [Test]
    public void CoreAssembly_DoesNotReferenceProhibitedDependencies()
    {
        string[] referenceNames = typeof(CoreAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        foreach (string referenceName in referenceNames)
        {
            Assert.That(
                ProhibitedDependencies.Any(
                    prohibited => referenceName.Contains(prohibited, StringComparison.OrdinalIgnoreCase)),
                Is.False,
                $"Core references prohibited dependency '{referenceName}'.");
        }
    }

    /// <summary>
    /// Verifies Core source and its project cannot silently declare prohibited runtime integrations.
    /// </summary>
    [Test]
    public void CoreProject_DoesNotDeclareProhibitedDependencies()
    {
        string repositoryRoot = FindRepositoryRoot();
        string coreDirectory = Path.Combine(repositoryRoot, "code", "Core");
        IEnumerable<string> sourceFiles = Directory
            .EnumerateFiles(coreDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        string[] guardedFiles = sourceFiles
            .Append(Path.Combine(coreDirectory, "Trackstorm.Core.csproj"))
            .ToArray();

        foreach (string file in guardedFiles)
        {
            string contents = File.ReadAllText(file);
            foreach (string prohibited in ProhibitedDependencies)
            {
                Assert.That(
                    contents.Contains(prohibited, StringComparison.OrdinalIgnoreCase),
                    Is.False,
                    $"Core file '{file}' contains prohibited dependency '{prohibited}'.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Trackstorm.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Trackstorm repository root.");
    }
}
