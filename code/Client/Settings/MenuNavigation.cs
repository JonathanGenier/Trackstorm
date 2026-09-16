namespace Trackstorm.Client.Settings;

/// <summary>Contextual back stack independent of match phase, player count, artwork and persistence.</summary>
internal sealed class MenuNavigation
{
    private bool _fromArena;

    /// <summary>Current local page; initially closed.</summary>
    internal MenuPage Page { get; private set; }

    /// <summary>Records the caller so Back returns to the correct context.</summary>
    /// <param name="arena">Whether the caller is the arena.</param>
    internal void Open(bool arena)
    {
        _fromArena = arena;
        Page = arena ? MenuPage.Game : MenuPage.Settings;
    }

    /// <summary>Accepts only a direct child of the current page.</summary>
    /// <param name="page">Requested child page.</param>
    internal void Select(MenuPage page)
    {
        if ((Page == MenuPage.Game && page == MenuPage.Settings) ||
            (Page == MenuPage.Settings && page >= MenuPage.Audio && page <= MenuPage.DeveloperOptions))
        {
            Page = page;
        }
    }

    /// <summary>Returns exactly one level toward the caller.</summary>
    internal void Back() => Page = Page switch
    {
        MenuPage.Closed => MenuPage.Closed,
        MenuPage.Game => MenuPage.Closed,
        MenuPage.Settings => _fromArena ? MenuPage.Game : MenuPage.Closed,
        _ => MenuPage.Settings,
    };

    /// <summary>Closes all pages when gameplay resumes or the session ends.</summary>
    internal void Close() => Page = MenuPage.Closed;
}
