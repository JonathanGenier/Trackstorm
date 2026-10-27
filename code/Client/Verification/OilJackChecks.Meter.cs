using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class OilJackChecks
{
    private bool _meterFull, _meterHalf, _meterEmpty, _meterRefilled;
    private float _meterPeerDifference;
    private int _meterPeerSamples;
    private ulong _meterPublicationLag;
    private ulong _meterPeerTick;
    private float _meterPeerAge;
    private int _meterCameraHold;
    private readonly HashSet<string> _completedCaptures = new();

    private void ObserveMeter(OilJack jack, OilPatch? current, ulong tick)
    {
        if (current is null)
        {
            Check(jack.MeterLevel > .999f, "new Oil pickup meter is full before accepted firing");
            _meterFull = true;
            _meterRefilled |= _meterEmpty;
            return;
        }
        if (current.Pour is not { } pour) { return; }
        long phase = (long)tick - (long)pour.StartTick;
        if (phase is >= 25 and <= 35)
        {
            Check(jack.MeterLevel is > .25f and < .65f, "sight glass visibly drains during accepted one-second burst");
            _meterHalf = true;
        }
        if (phase >= pour.DurationTicks + 6)
        {
            Check(jack.MeterLevel < .001f, "spent meter remains empty through mouth closure and retraction");
            _meterEmpty = true;
        }
        var observer = _arenas[1];
        var peerJack = observer.Bodies.GetValueOrDefault(1ul)?.FindChildren("*", "", true, false).OfType<OilJack>().FirstOrDefault();
        if (peerJack is null || observer.Driver.ItemState?.Patches.Any(p => p.Id == current.Id) != true) { return; }
        float difference = Math.Abs(jack.MeterLevel - peerJack.MeterLevel);
        // Compare the rendered observer to its own accepted publication clock.
        // Transport delay means the two peers need not have the same snapshot;
        // presentation may advance by this render frame's elapsed time.
        ulong hostTick = _arenas[0].Driver.ItemState!.World.Tick, peerTick = observer.Driver.ItemState!.World.Tick;
        _meterPeerAge = (_meterPeerTick == peerTick ? _meterPeerAge : 0) + (float)(_renderMs / 1000);
        _meterPeerTick = peerTick;
        float confirmedLevel = 1 - Mathf.Clamp(((long)peerTick - (long)pour.StartTick) / (float)pour.DurationTicks, 0, 1);
        float frameAdvance = _meterPeerAge * 60 / pour.DurationTicks;
        Check(peerJack.MeterLevel <= confirmedLevel + .035f && peerJack.MeterLevel >= confirmedLevel - frameAdvance - .035f,
            $"meter follows accepted tick {peerTick}: displayed={peerJack.MeterLevel}, confirmed={confirmedLevel}, frame advance={frameAdvance}");
        _meterPublicationLag = Math.Max(_meterPublicationLag, hostTick > peerTick ? hostTick - peerTick : 0);
        if (difference > _meterPeerDifference && difference > .25f)
        { GD.Print($"Meter delayed clocks: host publication={hostTick}, peer publication={peerTick}, host level={jack.MeterLevel:F3}, peer level={peerJack.MeterLevel:F3}."); }
        _meterPeerDifference = Math.Max(_meterPeerDifference, difference); _meterPeerSamples++;
    }

    private void RecordMeter(OilJack jack)
    {
        string? name = jack.Extension < .999f ? null : jack.MeterLevel switch
        {
            > .999f => "meter-full.png", > .35f and < .65f => "meter-half.png", < .001f => "meter-empty.png", _ => null
        };
        if (name is null || _completedCaptures.Contains(name)) { return; }
        Capture(name); _meterCameraHold = 4;
    }

    private static void AimMeter(Camera3D camera, Networking.NetworkVehicleBody car)
    {
        var meter = car.FindChild("GaugeLiquidLevel", true, false) as Node3D;
        if (meter is null) { return; }
        // Follow the rigid glass frame, not the shrinking liquid's scaled axis.
        Vector3 target = meter.GetParent<Node3D>().ToGlobal(new Vector3(.43f, -.004f, .309f));
        camera.GlobalPosition = target + car.VisualTransform.Basis * new Vector3(.10f, .05f, 1.05f);
        camera.LookAt(target, car.VisualTransform.Basis.Y);
    }
}
