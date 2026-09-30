using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Rack-owned Blender arm driven by confirmed placement state; no gameplay authority.</summary>
internal sealed partial class ProxyMineRack : Node3D
{
    internal const string AssetPath = "res://assets/items/proxy-mine/ProxyMinePlacementArm.glb";
    private const float ReturnSeconds = .23f;
    private static readonly Vector3 Shoulder = new(0, -.24f, -.25f);
    private static readonly Vector3 Stored = new(0, -.43f, -.27f);
    private static readonly Vector3 RackMine = new(0, .42f, 0);
    private readonly ProxyMineVisual _mine = new();
    private Node3D _upper = null!;
    private Node3D _forearm = null!;
    private Node3D _wrist = null!;
    private Node3D _left = null!;
    private Node3D _right = null!;
    private ProxyMineState? _placement;
    private Vector3 _returnFrom;
    private Vector3 _preparationFrom = Stored;
    private float _return;
    private float _remaining;

    internal bool Returning => _return > 0;
    internal bool ShowStored { get; set; } = true;

    public override void _Ready()
    {
        var arm = MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(arm);
        _upper = (Node3D)arm.FindChild("Upper", true, false);
        _forearm = (Node3D)arm.FindChild("Forearm", true, false);
        _wrist = (Node3D)arm.FindChild("Wrist", true, false);
        _left = (Node3D)arm.FindChild("JawLeft", true, false);
        _right = (Node3D)arm.FindChild("JawRight", true, false);
        AddChild(_mine);
        _mine.Position = RackMine;
        Pose(Stored, 0, 0);
    }

    internal void Observe(ProxyMineState? placement)
    {
        if (_placement is not null && placement is null)
        {
            _returnFrom = _wrist.Position;
            _return = ReturnSeconds;
        }
        if (placement is not null)
        {
            if (_placement?.Id != placement.Id)
            {
                _preparationFrom = _wrist.Position;
                _remaining = placement.PlacementTicks;
            }
            else { _remaining = Math.Min(_remaining, placement.PlacementTicks); }
            _return = 0;
        }
        _placement = placement;
    }

    public override void _Process(double delta)
    {
        if (_placement is not null)
        {
            // Interpolate between accepted 60 Hz boundaries, but never predict release.
            _remaining = _remaining < _placement.PlacementTicks - 3
                ? Mathf.MoveToward(_remaining, _placement.PlacementTicks, (float)delta * 120)
                : Math.Max(_placement.PlacementTicks - 3, _remaining - (float)delta * 60);
            float t = 1 - _remaining / ProxyMineState.PlacementDurationTicks;
            Quaternion level = GlobalBasis.GetRotationQuaternion().Inverse() * new Quaternion(Vector3.Up, VehicleBody.ToGodot(_placement.Normal));
            Quaternion carriedRotation = Quaternion.Identity.Slerp(level, Ease((t - .72f) / .28f));
            Vector3 pickup = RackMine + Vector3.Up * ProxyMineState.WristHeight;
            Vector3 wrist;
            float unfold = Ease(t / .48f);
            if (t < .15f) { wrist = _preparationFrom.Lerp(Stored, Ease(t / .15f)); }
            else if (t < .30f) { wrist = Stored.Lerp(new Vector3(0, .15f, 1.25f), Ease((t - .15f) / .15f)); }
            else if (t < .42f) { wrist = new Vector3(0, .15f, 1.25f).Lerp(new Vector3(0, 1.25f, 1.25f), Ease((t - .30f) / .12f)); }
            else if (t < .50f) { wrist = new Vector3(0, 1.25f, 1.25f).Lerp(pickup, Ease((t - .42f) / .08f)); }
            else if (t < .58f) { wrist = pickup; }
            else if (t < .72f) { wrist = pickup.Lerp(new Vector3(0, 1.35f, 1.15f), Ease((t - .58f) / .14f)); }
            else
            {
                Vector3 ground = ToLocal(VehicleBody.ToGodot(_placement.Position));
                Vector3 end = ground + level * (Vector3.Up * ProxyMineState.WristHeight);
                // A missing support can leave the last valid point behind a moving car.
                // Keep the carried tool within its reach until authority finds ground again.
                end = Shoulder + (end - Shoulder).LimitLength(ProxyMineState.PlacementReach);
                wrist = new Vector3(0, 1.35f, 1.15f).Lerp(end, Ease((t - .72f) / .28f));
            }
            float closed = Ease((t - .50f) / .07f);
            Pose(wrist, unfold, 1 - closed);
            if (t >= .72f) { _wrist.Quaternion = carriedRotation; }
            _mine.Visible = true;
            _mine.Position = t < .58f ? RackMine : wrist - carriedRotation * (Vector3.Up * ProxyMineState.WristHeight);
            _mine.Quaternion = carriedRotation;
        }
        else if (_return > 0)
        {
            _return = Math.Max(0, _return - (float)delta);
            float t = 1 - _return / ReturnSeconds;
            Vector3 clear = new(0, .20f, 1.35f);
            Vector3 wrist = t < .15f ? _returnFrom : t < .60f ? _returnFrom.Lerp(clear, Ease((t - .15f) / .45f)) : clear.Lerp(Stored, Ease((t - .60f) / .40f));
            Pose(wrist, 1 - Ease((t - .60f) / .40f), Ease(t / .15f));
            _mine.Visible = false;
        }
        else
        {
            Pose(Stored, 0, 0);
            _mine.Visible = ShowStored;
            _mine.Position = RackMine;
            _mine.Quaternion = Quaternion.Identity;
        }
    }

    private void Pose(Vector3 wrist, float unfold, float open)
    {
        Vector3 offset = wrist - Shoulder;
        float distance = offset.Length();
        float length = Math.Max(.96f, distance * .52f + .18f);
        Vector3 bend = new Vector3(0, -offset.Z, offset.Y).Normalized();
        if (bend.Z < 0) { bend = -bend; }
        Vector3 elbow = (Shoulder + wrist) * .5f + bend * MathF.Sqrt(Math.Max(0, length * length - distance * distance * .25f));
        elbow = new Vector3(0, -.30f, .64f).Lerp(elbow, unfold);
        Link(_upper, Shoulder, elbow);
        Link(_forearm, elbow, wrist);
        _wrist.Position = wrist;
        _wrist.Rotation = new Vector3(Mathf.Pi * .5f * (1 - unfold), 0, 0);
        _left.Rotation = new Vector3(0, 0, -.45f * open);
        _right.Rotation = new Vector3(0, 0, .45f * open);
    }

    private static void Link(Node3D node, Vector3 start, Vector3 end)
    {
        Vector3 offset = end - start;
        node.Position = start;
        node.Quaternion = new Quaternion(Vector3.Up, offset.Normalized());
        var slide = node.GetChildren().OfType<Node3D>().Single(child => child.Name.ToString().StartsWith("Slide", StringComparison.Ordinal));
        slide.Position = new Vector3(0, offset.Length() - .86f, 0);
    }

    private static float Ease(float value) => Mathf.SmoothStep(0, 1, Mathf.Clamp(value, 0, 1));
}
