namespace Trackstorm.Client.Online;

/// <summary>One EOS notification's narrow semantic authority and affected member, when applicable.</summary>
/// <param name="Kind">Operation authorized by the native callback.</param>
/// <param name="Target">Exact member affected by a join or departure.</param>
internal readonly record struct OnlineLobbyUpdate(OnlineLobbyUpdateKind Kind, OnlineProductUserId? Target = null);
