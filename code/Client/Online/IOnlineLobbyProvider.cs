namespace Trackstorm.Client.Online;

/// <summary>Owner-thread asynchronous coordination boundary, independent of Godot and native SDK types.</summary>
internal interface IOnlineLobbyProvider : IDisposable
{
    /// <summary>Publishes admission availability derived from the existing host authority.</summary>
    /// <param name="id">Current logical lobby identity.</param>
    /// <param name="open">Whether new members may join.</param>
    /// <param name="completed">Safe owner-thread completion.</param>
    void SetJoinable(string id, bool open, Action<OnlineLobby?, string?> completed);

    /// <summary>Retrieves a fresh batch of compatible discovery metadata.</summary>
    /// <param name="completed">Owner-thread completion with safe error information.</param>
    void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed);
    /// <summary>Creates and advertises one lobby using already validated metadata.</summary>
    /// <param name="lobby">Current client-only lobby metadata.</param>
    /// <param name="completed">Owner-thread completion with safe error information.</param>
    void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed);
    /// <summary>Joins fresh compatible membership after the coordinator validates access.</summary>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <param name="completed">Owner-thread completion with safe error information.</param>
    void Join(string id, Action<OnlineLobby?, string?> completed);
    /// <summary>Re-establishes membership by a known locator; gameplay admission remains independently authenticated.</summary>
    /// <param name="id">Previously joined lobby.</param>
    /// <param name="completed">Owner-thread completion.</param>
    void Resume(string id, Action<OnlineLobby?, string?> completed) => Join(id, completed);
    /// <summary>Updates the existing logical lobby without replacing its identity.</summary>
    /// <param name="lobby">Current client-only lobby metadata.</param>
    /// <param name="completed">Owner-thread completion with safe error information.</param>
    void Update(OnlineLobby lobby, Action<OnlineLobby?, string?> completed);
    /// <summary>Invalidates local membership and releases or destroys the associated online lobby.</summary>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <param name="destroy">Whether the host closes the lobby for everyone.</param>
    /// <param name="completed">Owner-thread completion with safe error information.</param>
    void Leave(string id, bool destroy, Action<string?> completed);
    /// <summary>Registers one disposable membership notification subscription.</summary>
    /// <param name="id">Logical EOS lobby identity.</param>
    /// <param name="changed">Consumer of updated membership or closure.</param>
    /// <returns>A subscription whose disposal makes queued notifications inert.</returns>
    IDisposable Watch(string id, Action<OnlineLobby?> changed);
}
