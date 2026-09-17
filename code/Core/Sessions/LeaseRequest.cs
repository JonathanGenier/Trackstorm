namespace Trackstorm.Core.Sessions;

/// <summary>Conditional lease operation. The trusted endpoint supplies the caller identity.</summary>
/// <param name="Session">Unpredictable session routing capability, never public lobby metadata.</param>
/// <param name="Epoch">Expected current fencing epoch, zero only for creation.</param>
/// <param name="Token">Expected current token, empty only for creation.</param>
public sealed record LeaseRequest(string Session, ulong Epoch, string Token);
