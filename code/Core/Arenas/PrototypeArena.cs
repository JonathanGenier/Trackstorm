using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Single production layout contract; session identities are assigned to these stable slots.</summary>
public static class PrototypeArena
{
    /// <summary>120 by 100 metre combat yard with broad perimeter and central driving lanes.</summary>
    public static ArenaConfiguration Configuration { get; } = new(
        new Vector3(-60, 0, -50),
        new Vector3(60, 30, 50),
        new[]
        {
            new ArenaSpawn("player-01", new Vector3(-42, 0.6f, 34)),
            new ArenaSpawn("player-02", new Vector3(-14, 0.6f, 34)),
            new ArenaSpawn("player-03", new Vector3(14, 0.6f, 34)),
            new ArenaSpawn("player-04", new Vector3(42, 0.6f, 34)),
            new ArenaSpawn("player-05", new Vector3(-42, 0.6f, -34), MathF.PI),
            new ArenaSpawn("player-06", new Vector3(-14, 0.6f, -34), MathF.PI),
            new ArenaSpawn("player-07", new Vector3(14, 0.6f, -34), MathF.PI),
            new ArenaSpawn("player-08", new Vector3(42, 0.6f, -34), MathF.PI),
        },
        new[]
        {
            new ArenaSpawn("item-01", new Vector3(-46, 0, 0)),
            new ArenaSpawn("item-02", new Vector3(46, 0, 0)),
            new ArenaSpawn("item-03", new Vector3(0, 0, -36)),
            new ArenaSpawn("item-04", new Vector3(0, 0, 36)),
            new ArenaSpawn("item-05", new Vector3(-22, 0, -20)),
            new ArenaSpawn("item-06", new Vector3(22, 0, -20)),
            new ArenaSpawn("item-07", new Vector3(-22, 0, 20)),
            new ArenaSpawn("item-08", new Vector3(22, 0, 20)),
        },
        new[] { SurfaceType.Concrete, SurfaceType.Mud });
}
