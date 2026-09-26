using Godot;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Client.Hud;

/// <summary>Read-only race-start presentation. Local time animates artwork, never values or release.</summary>
internal sealed partial class RaceCountdown : Control
{
    private readonly Label _number = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Label _caption = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Control _card = new() { MouseFilter = MouseFilterEnum.Ignore };
    private double _age;
    private string _value = string.Empty;
    private bool _sawCountdown;
    private bool _goShown;

    internal string Displayed => Visible ? _value : string.Empty;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_card);
        _card.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _card.OffsetLeft = -160;
        _card.OffsetRight = 160;
        _card.OffsetTop = -130;
        _card.OffsetBottom = 130;
        _card.PivotOffset = _card.Size / 2;
        _card.AddChild(_number);
        _number.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _number.OffsetBottom = -35;
        _number.MouseFilter = MouseFilterEnum.Ignore;
        _number.AddThemeFontSizeOverride("font_size", 144);
        _number.AddThemeColorOverride("font_color", new Color("fff4dc"));
        _number.AddThemeColorOverride("font_outline_color", new Color("101923"));
        _number.AddThemeConstantOverride("outline_size", 16);
        _number.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        _number.AddThemeConstantOverride("shadow_offset_y", 7);
        _card.AddChild(_caption);
        _caption.Position = new Vector2(0, 212);
        _caption.Size = new Vector2(320, 36);
        _caption.MouseFilter = MouseFilterEnum.Ignore;
        _caption.AddThemeFontSizeOverride("font_size", 20);
        _caption.AddThemeConstantOverride("outline_size", 8);
        _caption.AddThemeColorOverride("font_outline_color", new Color("101923"));
        Visible = false;
    }

    internal void Refresh(MatchState? match, ulong authoritativeTick, bool synchronized, double delta)
    {
        string next = string.Empty;
        if (synchronized && match?.Phase == MatchPhase.Countdown)
        {
            _sawCountdown = true;
            // If Active is delayed, hold 1: a world snapshot alone never means GO.
            ulong remaining = match.Lifecycle.RemainingCountdownTicks(authoritativeTick);
            next = Math.Max(1ul, (remaining + HostVehicleSession.TickRate - 1) / HostVehicleSession.TickRate).ToString();
        }
        else if (synchronized && match?.Phase == MatchPhase.Active && _sawCountdown &&
            match.ActiveStartedAtTick is ulong start && authoritativeTick < start + 45 && (!_goShown || _value == "GO"))
        {
            next = "GO";
            _goShown = true;
        }

        if (next != _value)
        {
            _value = next;
            _age = 0;
            _number.Text = next;
            _caption.Text = next == "GO" ? "RACE IS LIVE" : "GET READY";
            _number.AddThemeColorOverride("font_color", new Color(next == "GO" ? "86ffd2" : "fff4dc"));
        }
        _age += delta;
        Visible = next.Length > 0 && (next != "GO" || _age < 0.75);
        float settle = (float)Math.Exp(-_age * 14);
        _card.Scale = Vector2.One * (1 + 0.18f * settle);
        _card.Modulate = new Color(1, 1, 1, next == "GO" ? (float)Math.Clamp((0.75 - _age) / 0.25, 0, 1) : 1);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!Visible) return;
        Vector2 center = Size / 2;
        Color accent = new(_value == "GO" ? "86ffd2" : "efb967");
        DrawCircle(center - new Vector2(0, 10), 110, new Color(0.025f, 0.045f, 0.07f, 0.72f));
        DrawArc(center - new Vector2(0, 10), 114, -Mathf.Pi * 0.82f, Mathf.Pi * 0.82f, 80, accent, 3, true);
        for (int index = 0; index < 5; index++)
        {
            bool lit = _value == "GO" || (int.TryParse(_value, out int value) && index >= 5 - value);
            DrawRect(new Rect2(center.X - 64 + index * 27, center.Y + 125, 20, 4), lit ? accent : new Color(0.3f, 0.35f, 0.4f, 0.5f));
        }
    }
}
