using Trackstorm.Client.Settings;

namespace Trackstorm.Transport.Tests;

/// <summary>Back-stack and caller-context regression coverage without native Godot.</summary>
internal sealed class MenuNavigationTests
{
    /// <summary>Every approved page preserves the full arena hierarchy.</summary>
    [Test]
    public void EveryCategoryReturnsThroughSettingsAndGame()
    {
        foreach (MenuPage category in Enum.GetValues<MenuPage>().Where(page => page >= MenuPage.Audio))
        {
            var menu = new MenuNavigation();
            menu.Open(true);
            Assert.That(menu.Page, Is.EqualTo(MenuPage.Game));
            menu.Select(MenuPage.Settings);
            menu.Select(category);
            Assert.That(menu.Page, Is.EqualTo(category));
            menu.Back();
            Assert.That(menu.Page, Is.EqualTo(MenuPage.Settings));
            menu.Back();
            Assert.That(menu.Page, Is.EqualTo(MenuPage.Game));
            menu.Back();
            Assert.That(menu.Page, Is.EqualTo(MenuPage.Closed));
        }
    }

    /// <summary>Non-arena entry and invalid transitions cannot invent a gameplay context.</summary>
    [Test]
    public void MainMenuSettingsReturnToTheirCallerAndInvalidTransitionsDoNothing()
    {
        var menu = new MenuNavigation();
        menu.Select(MenuPage.Audio);
        Assert.That(menu.Page, Is.EqualTo(MenuPage.Closed));
        menu.Open(false);
        menu.Select(MenuPage.Game);
        Assert.That(menu.Page, Is.EqualTo(MenuPage.Settings));
        menu.Back();
        Assert.That(menu.Page, Is.EqualTo(MenuPage.Closed));
        menu.Open(true);
        menu.Close();
        menu.Back();
        Assert.That(menu.Page, Is.EqualTo(MenuPage.Closed));
    }
}
