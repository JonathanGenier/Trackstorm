using Trackstorm.Core;

namespace Trackstorm.Core.Tests.Architecture;

/// <summary>
/// Verifies the compile-time boundary around the authoritative Core assembly.
/// </summary>
[TestFixture]
internal sealed class CoreDependencyTests
{
    /// <summary>
    /// Verifies that Core remains independent from Client and Godot assemblies.
    /// </summary>
    [Test]
    public void CoreAssembly_DoesNotReferenceClientOrGodot()
    {
        string[] referenceNames = typeof(CoreAssemblyMarker).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(referenceNames, Does.Not.Contain("Trackstorm.Client"));
            Assert.That(referenceNames, Has.None.StartsWith("GodotSharp"));
        });
    }
}
