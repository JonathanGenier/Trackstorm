using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;

namespace Trackstorm.Transport.Tests;

internal sealed partial class VehicleNetworkDriverTests
{
    [Test]
    public void WrenchEarlyTapIsNotQueuedAndPresentationMustBeReadyBeforeSending()
    {
        using var wire = ConnectedGateway();
        using var driver = new VehicleNetworkDriver(wire, Session,
            configuration: new() { Match = new() { MinimumPlayers = 1, CountdownTicks = 1 } });
        bool ready = false;
        driver.WrenchReady = _ => ready;
        driver.Advance(default, Observe); driver.Advance(default, Observe);
        var authority = driver.Host!;
        authority.Items.Grant(authority.World, 1, HeldItem.Wrench);
        Assert.That(driver.RequestItemUse(), Is.False);
        for (int i = 0; i < 180; i++) { driver.Advance(default, Observe); }
        Assert.That(authority.Items.Slots.Single().Item, Is.EqualTo(HeldItem.Wrench));
        Assert.That(driver.RequestItemUse(), Is.False, "Authority readiness cannot bypass a still-returning local payload.");
        ready = true;
        driver.Advance(default, Observe);
        Assert.That(authority.Items.Events, Is.Empty, "Early tap does not auto-fire later.");
        Assert.That(driver.RequestItemUse(), Is.True);
        Assert.That(driver.RequestItemUse(), Is.False);
        driver.Advance(default, Observe);
        Assert.That(authority.Items.Events.Single().Item, Is.EqualTo(HeldItem.Wrench));
        authority.Items.Grant(authority.World, 1, HeldItem.Salvo);
        driver.Advance(default, Observe);
        driver.WrenchReturning = () => true;
        for (int i = 0; i < 240; i++) { driver.Advance(default, Observe); }
        Assert.That(authority.Items.SalvoReady(authority.Items.Slots.Single()), Is.True);
        Assert.That(driver.RequestItemUse(), Is.False, "Incoming capability waits for the spent Wrench mechanism.");
        Assert.That(driver.RequestItemDiscard(), Is.False, "Discard cannot delete the incoming capability behind the displayed Wrench.");
        driver.Advance(default, Observe);
        Assert.That(authority.Items.Events, Is.Empty);
        driver.WrenchReturning = () => false;
        Assert.That(driver.RequestItemUse(), Is.True, "Handoff releases the preserved incoming capability.");
    }
}
