using Godot;
using Trackstorm.Client.Networking;

namespace Trackstorm.Client.Items;

/// <summary>Articulated load path from production rack sockets to the shield's four-point cradle.</summary>
internal sealed partial class TombstoneRack : Node3D
{
    private Node3D _model = null!;
    private readonly Dictionary<string, Node3D> _parts = new();
    private readonly Dictionary<Node3D, Transform3D> _fittings = new();
    internal Transform3D? Shield { get; set; }

    public override void _Ready()
    {
        _model = MatchResourceLoader.LoadResource<PackedScene>("res://assets/items/tombstone/TombstoneRack.glb").Instantiate<Node3D>();
        AddChild(_model);
        foreach (string name in new[] { "Cradle", "Upper_L", "Lower_L", "Ram_L", "Rod_L", "Upper_R", "Lower_R", "Ram_R", "Rod_R" })
        { _parts.Add(name, (Node3D)_model.FindChild(name, true, false)); }
        foreach (var part in _parts.Values)
        {
            foreach (var fitting in part.GetChildren().OfType<Node3D>().Where(n =>
                n.Name.ToString().Contains("Clevis", StringComparison.Ordinal) ||
                n.Name.ToString().Contains("Retaining", StringComparison.Ordinal) ||
                n.Name.ToString().Contains("collar", StringComparison.Ordinal)))
            { _fittings.Add(fitting, fitting.Transform); }
        }
    }

    public override void _Process(double delta)
    {
        var cradle = _parts["Cradle"];
        var target = Shield ?? new Transform3D(new Basis(Vector3.Right, MathF.PI / 2).Scaled(Vector3.One * .28f), new(0, .35f, .10f));
        cradle.Transform = target * new Transform3D(Basis.Identity, new(0, 0, -.185f));
        foreach (var (side, x) in new[] { ("L", -.45f), ("R", .45f) })
        {
            Vector3 a = new(x, .22f, .30f);
            Vector3 b = cradle.Transform * new Vector3(x, 0, 0);
            Vector3 elbow = new(x, Math.Max(.32f, b.Y + .25f), Math.Max(.50f, b.Z + .08f));
            Align(_parts["Upper_" + side], a, elbow);
            Align(_parts["Lower_" + side], elbow, b);
            Vector3 start = a + new Vector3(.16f, -.02f, .13f);
            Vector3 end = b.Lerp(elbow, .23f) + Vector3.Right * .16f;
            Vector3 middle = start.Lerp(end, .58f);
            Align(_parts["Ram_" + side], start, middle);
            Align(_parts["Rod_" + side], middle, end);
        }
    }

    private void Align(Node3D part, Vector3 a, Vector3 b)
    {
        Vector3 direction = b - a;
        float length = Math.Max(.001f, direction.Length());
        Vector3 up = Math.Abs(direction.Normalized().Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up;
        part.Transform = new(Basis.LookingAt(-direction, up).Scaled(new Vector3(1, 1, length)), a);
        // Only the telescopic beams change length; axles and collars retain
        // circular, rigid dimensions as their attachment positions travel.
        foreach (var fitting in part.GetChildren().OfType<Node3D>())
        {
            if (_fittings.TryGetValue(fitting, out var authored))
            { fitting.Transform = new(Basis.FromScale(new(1, 1, 1 / length)) * authored.Basis, authored.Origin); }
        }
    }
}
