using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Fires the same single flaming projectile the vanilla turrets use:
    /// EnemyManager.SpawnShot + the _fxLava prefab, registered in
    /// EnemyManager._shots so it is targetable and shootable.
    /// </summary>
    internal static class AmbusherLob
    {
        private const float MaxDistancePx = 1600f;
        private const int ProjectileHp = 3;

        internal static void Spawn(AmbusherDirector director, Vector3 from, ShipController ship, int damage)
        {
            if (director == null || ship == null)
            {
                return;
            }

            director.ResolveRefs();
            EnemyManager enemies = director.Enemies;
            if (enemies == null)
            {
                return;
            }

            float px = director.Px;
            float speed = Mathf.Max(1f, Plugin.LobSpeedPx.Value) * px;

            Vector3 aim = ship.transform.position;
            Vector3 velocity = Reflect.Get<Vector3>(ship, "_smoothVel");
            for (int i = 0; i < 2; i++)
            {
                float time = Vector3.Distance(from, aim) / Mathf.Max(0.0001f, speed);
                aim = ship.transform.position + velocity * time;
            }

            Vector3 dir = aim - from;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            Material shotMat = Reflect.Get<Material>(enemies, "_shotMat");
            MethodInfo spawn = AccessTools.Method(typeof(EnemyManager), "SpawnShot");
            if (spawn == null)
            {
                Plugin.Log?.LogWarning("[ambusher] EnemyManager.SpawnShot not found.");
                return;
            }

            spawn.Invoke(enemies, new object[]
            {
                from,
                dir,
                speed,
                damage,
                ProjectileHp,
                MaxDistancePx * px,
                director.HexSize * 0.5f,
                shotMat
            });
        }
    }
}
