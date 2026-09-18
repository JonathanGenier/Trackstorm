namespace Trackstorm.Client.Online;

/// <summary>Creates Trackstorm's distributable, untrusted EOS game-client configuration.</summary>
internal static class EosClientConfiguration
{
    private const string OverrideVariable = "TRACKSTORM_EOS_CONFIG";
    private const string Environment = "development";
    private const string DeploymentName = "live-deployment";
    private const string ProductId = "47d0843c277f4b11bce0e633b7ced6e9";
    private const string SandboxId = "6c0a33a5748841e09204957d28e6bb84";
    private const string DeploymentId = "dea3c60b4f30486688fbf674c9e57c2b";
    private const string ClientId = "xyza7891VzsMsnNdYcYraBkYrS4ZheUn";
    private const string ClientSecret = "RKhyGdQ1JBnNuDUlTXNp9OV5daAao522OZKgVj5JZkU";

    /// <summary>Creates and validates the configuration embedded in the distributed client.</summary>
    /// <returns>The default EOS client configuration.</returns>
    internal static EosConfiguration Create()
    {
        var configuration = new EosConfiguration
        {
            Environment = Environment,
            DeploymentName = DeploymentName,
            ProductId = ProductId,
            SandboxId = SandboxId,
            DeploymentId = DeploymentId,
            ClientId = ClientId,
            ClientSecret = ClientSecret,
        };

        try
        {
            configuration.Validate();
            return configuration;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Embedded EOS client configuration is invalid: {exception.Message}", exception);
        }
    }

    /// <summary>Uses an explicitly configured file override, or the embedded client configuration.</summary>
    /// <returns>The validated effective EOS configuration.</returns>
    internal static EosConfiguration Resolve() => Resolve(System.Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>Resolves a supplied override path for deterministic verification.</summary>
    /// <param name="overridePath">Explicit override path, or null to use the embedded configuration.</param>
    /// <returns>The validated effective EOS configuration.</returns>
    internal static EosConfiguration Resolve(string? overridePath)
    {
        if (overridePath is null)
        {
            return Create();
        }

        if (string.IsNullOrWhiteSpace(overridePath))
        {
            throw new InvalidOperationException($"{OverrideVariable} is set but does not contain a configuration file path.");
        }

        try
        {
            return EosConfiguration.Load(overridePath);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"{OverrideVariable} override '{overridePath}' could not be loaded: {exception.Message}", exception);
        }
    }
}
