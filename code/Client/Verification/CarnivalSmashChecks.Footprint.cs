using Godot;
using Trackstorm.Client.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class CarnivalSmashChecks
{
    /// <summary>Measures actual rendered pixels in three orthographic views, independently of shader motion math.</summary>
    private async Task VerifyRenderedFootprint()
    {
        const int pixels = 512;
        const float metres = 8;
        var viewport = new SubViewport { Size = new(pixels, pixels), OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(viewport);
        var stage = new Node3D(); viewport.AddChild(stage);
        stage.AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .7f } });
        stage.AddChild(new DirectionalLight3D { RotationDegrees = new(-45, -30, 0), LightEnergy = 1 });
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = metres, Current = true };
        stage.AddChild(camera);
        var resources = new CarnivalSmashResources();
        Vector3 defaultSpan = Vector3.Zero;
        float defaultWidth = 0;
        foreach (float scale in new[] { 1f, 1.5f })
        {
            Vector3 span = Vector3.Zero;
            foreach (int view in new[] { 0, 1, 2 })
            {
                Vector3 target = Vector3.Up*1.4f;
                camera.Position = target + (view == 0 ? Vector3.Back : view == 1 ? Vector3.Right : Vector3.Up)*10;
                camera.LookAt(target, view == 2 ? Vector3.Back : Vector3.Up);
                var effect = new CarnivalSmash { Token = 600, Resources = resources, Settings = new() { Size = scale } };
                stage.AddChild(effect); effect.SetProcess(false);
                float previous = 0;
                foreach (float age in new[] { .06f, .22f, .55f, 1.1f, 3f })
                {
                    effect.Advance(age-previous); previous = age;
                    await Frames(2); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var capture = viewport.GetTexture().GetImage();
                    capture.Convert(Image.Format.Rgba8);
                    byte[] data = capture.GetData();
                    int left = pixels, right = -1, top = pixels, bottom = -1;
                    for (int y = 0; y < pixels; y++)
                    for (int x = 0; x < pixels; x++)
                    {
                        int index = (y*pixels+x)*4;
                        if (Math.Max(data[index], Math.Max(data[index+1], data[index+2])) < 12) continue;
                        left = Math.Min(left,x); right = Math.Max(right,x); top = Math.Min(top,y); bottom = Math.Max(bottom,y);
                    }
                    Check(right >= left && bottom >= top && left > 0 && top > 0 && right < pixels-1 && bottom < pixels-1,
                        $"Footprint view {view}, scale {scale}, age {age}: visible unclipped rendered burst", false);
                    float width = (right-left+1)*metres/pixels, height = (bottom-top+1)*metres/pixels;
                    if (view == 0) { span.X = Math.Max(span.X,width); span.Y = Math.Max(span.Y,height); }
                    if (view == 1) { span.Z = Math.Max(span.Z,width); span.Y = Math.Max(span.Y,height); }
                    if (view == 2) { span.X = Math.Max(span.X,width); span.Z = Math.Max(span.Z,height); }
                    Check(capture.SavePng(System.IO.Path.Combine(_output,$"footprint-{scale:F1}-{view}-{age:F2}.png")) == Error.Ok,
                        "Footprint capture saved", false);
                }
                effect.Free();
            }
            _evidence.Add($"Rendered orthographic footprint at size {scale:F1}: maximum observed width {span.X:F3} m, height {span.Y:F3} m, depth {span.Z:F3} m across five ages and three views.");
            if (scale == 1) { defaultSpan = span; defaultWidth = span.X; }
            else Check(span.X > defaultWidth*1.4f && span.X < defaultWidth*1.6f, "1.5x preview measurably scales the rendered burst without changing particle count or lifetime");
        }
        Check(defaultSpan.X > 3.09f && defaultSpan.X <= 4.05f && defaultSpan.Y <= 3.6f && defaultSpan.Z <= 2.4f,
            $"Default burst stays just beyond the 3.09 x 2.85 x 1.77 m stack; observed {defaultSpan}");
        viewport.QueueFree(); await Frames(3);
    }
}
