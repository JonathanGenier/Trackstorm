namespace Trackstorm.Core.Items;

/// <summary>Host native sweep result. A contact names the vehicle touched, never a client-claimed target.</summary>
public sealed record ProxyMineMotion(ProxyMineState State, ulong ContactVehicle = 0);
