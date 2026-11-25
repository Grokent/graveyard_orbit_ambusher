using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Owns the prototype ambusher population for one mining sortie.
    /// Lives under the MiningSortie object, so it dies with the scene.
    /// </summary>
    internal sealed class AmbusherDirector : MonoBehaviour
    {
        internal static AmbusherDirector Instance { get; private set; }

        internal ShipController Ship { get; private set; }
        internal EnemyManager Enemies { get; private set; }
        internal float HexSize { get; private set; } = 1f;
        internal float Px => HexSize / 32f;

        private List<SwarmUnit> _swarm;
        private readonly List<AmbusherDrone> _drones = new List<AmbusherDrone>();
        private Material _droneMaterial;
        private MiningSortie _pendingWin;
        private float _nextRipple;
        private Camera _camera;
        private bool _cameraWarned;
        private ProjectileSystem _projectiles;
        private System.Collections.IList _projectilePool;

        internal int LiveDrones
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _drones.Count; i++)
                {
                    if (_drones[i] != null) count++;
                }
                return count;
            }
        }

        internal static AmbusherDirector Ensure(MiningSortie sortie)
        {
            if (Instance != null)
            {
                return Instance;
            }

            if (sortie == null)
            {
                return null;
            }

            GameObject go = new GameObject("AmbusherDirector");
            go.transform.SetParent(sortie.transform, false);
            AmbusherDirector director = go.AddComponent<AmbusherDirector>();
            director.HexSize = sortie.hexSize > 0f ? sortie.hexSize : 1f;
            director.ResolveRefs();
            Instance = director;
            Plugin.Log.LogInfo($"[ambusher] director created (hexSize={director.HexSize:0.###}).");
            return director;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            ResolveRefs();
            _drones.RemoveAll(d => d == null);
            TickPhysicalHits();
            TickPendingWin();
        }

        internal void ResolveRefs()
        {
            if (Ship == null)
            {
                Ship = Object.FindAnyObjectByType<ShipController>();
            }

            if (Enemies == null)
            {
                Enemies = Object.FindAnyObjectByType<EnemyManager>();
            }

            if (_swarm == null && Enemies != null)
            {
                _swarm = AccessTools.Field(typeof(EnemyManager), "_swarm")?.GetValue(Enemies) as List<SwarmUnit>;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null)
                {
                    _camera = Object.FindAnyObjectByType<Camera>();
                }
            }

            if (_projectiles == null)
            {
                _projectiles = Object.FindAnyObjectByType<ProjectileSystem>();
                if (_projectiles != null)
                {
                    _projectilePool = AccessTools.Field(typeof(ProjectileSystem), "_pool")?.GetValue(_projectiles) as System.Collections.IList;
                }
            }
        }

        internal void BlockWin(MiningSortie sortie)
        {
            _pendingWin = sortie;
        }

        private void TickPendingWin()
        {
            if (_pendingWin == null || LiveDrones > 0)
            {
                return;
            }

            MiningSortie sortie = _pendingWin;
            _pendingWin = null;
            Plugin.Log?.LogInfo("[ambusher] last drone down; releasing the win.");
            Reflect.Call(sortie, "StructureCleared");
        }

        /// <summary>Lets stray player projectiles hit drones while they are cloaked.</summary>
        private void TickPhysicalHits()
        {
            if (_projectilePool == null || _drones.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _projectilePool.Count; i++)
            {
                object projectile = _projectilePool[i];
                if (projectile == null || !Reflect.Get<bool>(projectile, "live"))
                {
                    continue;
                }

                Vector3 pos = Reflect.Get<Vector3>(projectile, "pos");
                int damage = Reflect.Get<int>(projectile, "damage");
                float radius = Reflect.Get<float>(projectile, "hitRadius");

                for (int d = 0; d < _drones.Count; d++)
                {
                    AmbusherDrone drone = _drones[d];
                    if (drone != null)
                    {
                        drone.TryPhysicalHit(pos, Mathf.Max(radius, HexSize * 0.25f), damage);
                    }
                }
            }
        }

        internal AmbusherDrone SpawnDrone(Vector3 position)
        {
            try
            {
                return SpawnDroneInternal(position);
            }
            catch (System.Exception e)
            {
                Plugin.Log?.LogError($"[ambusher] drone spawn failed: {e}");
                return null;
            }
        }

        private AmbusherDrone SpawnDroneInternal(Vector3 position)
        {
            ResolveRefs();

            GameObject go = new GameObject("AmbusherDrone");
            go.transform.SetParent(transform, false);
            go.transform.position = position;

            SwarmUnit swarm = go.AddComponent<SwarmUnit>();
            swarm.Init(
                position,
                () => Ship,
                0f,
                1,
                2,
                HexSize,
                DroneMaterial(),
                Vector3.forward,
                0f,
                0f,
                null);
            swarm.enabled = false;

            AmbusherDrone drone = go.AddComponent<AmbusherDrone>();
            drone.Bind(this, swarm);
            _drones.Add(drone);

            if (Plugin.LogVerbose.Value)
            {
                Plugin.Log.LogInfo($"[ambusher] drone spawned at {position}.");
            }

            return drone;
        }

        /// <summary>Spawn a real EnemyManager carrier at the ship, for the debug hotkey.</summary>
        internal void SpawnRealCarrierNearShip()
        {
            ResolveRefs();
            if (Enemies == null || Ship == null)
            {
                return;
            }

            Vector3 forward = Ship.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 position = Ship.transform.position
                + forward * (HexSize * Mathf.Max(2f, Plugin.DebugCarrierDistanceHex.Value));
            Vector2Int cell = HexLayout.WorldToAxial(position, HexSize);

            List<(Vector2Int cell, string kind)> placements = new List<(Vector2Int, string)>
            {
                (cell, CarrierIntegration.TileId)
            };

            Enemies.SeedAtCellsExplicit(placements);
            if (!Plugin.CarriersVisibleAtStart.Value)
            {
                CarrierPresentation.RevealCarrier(Enemies, cell);
            }

            Plugin.Log?.LogInfo($"[ambusher] debug carrier seeded at cell {cell} ({Plugin.DebugCarrierDistanceHex.Value:0.#} hex ahead).");
        }

        internal void EmitRipple(Vector3 world, Vector3 direction, float speedPx)
        {
            if (speedPx < Mathf.Max(0f, Plugin.RippleMinSpeedPx.Value))
            {
                return;
            }

            float fast = Mathf.Clamp01(speedPx / 240f);
            float interval = Mathf.Max(0.05f, Plugin.RippleInterval.Value / Mathf.Lerp(0.7f, 1.6f, fast));
            if (Time.unscaledTime < _nextRipple)
            {
                return;
            }

            _nextRipple = Time.unscaledTime + interval;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return;
            }

            direction.Normalize();
            Vector3 center = world - direction * (Plugin.RippleWakeOffset.Value * HexSize);
            float kick = Plugin.RippleKick.Value * Mathf.Lerp(0.5f, 1.4f, fast);

            if (_camera == null)
            {
                _camera = Camera.main ?? Object.FindAnyObjectByType<Camera>();
            }

            if (_camera == null)
            {
                if (!_cameraWarned)
                {
                    _cameraWarned = true;
                    Plugin.Log?.LogWarning("[ambusher] no camera found for cloak ripple.");
                }
                return;
            }

            SpaceTimeRipple.Play(center, _camera, kick);
            if (Plugin.LogVerbose.Value && !_cameraWarned)
            {
                _cameraWarned = true;
                Plugin.Log?.LogInfo($"[ambusher] ripple camera = {_camera.name}.");
            }
        }

        internal void SpawnDroneNear(Vector3 center)
        {
            Vector3 offset = UnityEngine.Random.insideUnitSphere * (HexSize * 1.5f);
            offset.y = 0f;
            Vector3 position = center + offset;
            position.y = center.y;
            SpawnDrone(position);
        }

        internal void SpawnDebugDrone(int index)
        {
            ResolveRefs();
            if (Ship == null)
            {
                return;
            }

            float angle = 150f + index * 12f;
            Vector3 forward = Ship.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
            Vector3 position = Ship.transform.position + dir * (HexSize * 10f);
            SpawnDrone(position);
        }

        /// <summary>
        /// Adds/removes the drone from EnemyManager's swarm list so auto-aim and
        /// DamageTarget see it only while uncloaked.
        /// </summary>
        internal void SetTargetable(SwarmUnit unit, bool targetable)
        {
            if (_swarm == null || unit == null)
            {
                return;
            }

            bool present = _swarm.Contains(unit);
            if (targetable && !present)
            {
                _swarm.Add(unit);
            }
            else if (!targetable && present)
            {
                _swarm.Remove(unit);
            }
        }

        private Material DroneMaterial()
        {
            if (_droneMaterial != null)
            {
                return _droneMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                throw new System.Exception("no usable shader for the ambusher placeholder");
            }

            _droneMaterial = new Material(shader) { color = new Color(0.1f, 0.1f, 0.13f) };
            return _droneMaterial;
        }
    }
}
