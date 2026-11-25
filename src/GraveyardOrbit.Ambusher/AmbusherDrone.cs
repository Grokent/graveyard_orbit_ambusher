using UnityEngine;

namespace GraveyardOrbit.Ambusher
{
    /// <summary>
    /// Prototype ambusher drone. Wraps a disabled SwarmUnit for the placeholder
    /// visuals and for auto-aim/DamageTarget integration through EnemyManager._swarm.
    /// </summary>
    internal sealed class AmbusherDrone : MonoBehaviour
    {
        private enum State
        {
            Hunt,
            Fire,
            Windup,
            Lunge,
            Evade
        }

        // Tuning (px = hexSize / 32, the game's own unit for unit speeds).

        private const float OrbitRadiusPx = 320f;
        private const float LungeSpeedPx = 560f;
        private const float LungeSeconds = 0.35f;
        private const float EvadeSeconds = 5f;

        private const float RetargetSeconds = 1.5f;
        private const float CloakBreakSeconds = 0.75f;
        private const float LifetimeSeconds = 300f;
        private const float RamRadiusHex = 1.2f;

        private AmbusherDirector _director;
        private SwarmUnit _swarm;
        private GameObject _vis;
        private GameObject _shimmer;
        private Material _shimmerMat;
        private bool _glassVisual;
        private bool _mirageVisual;
        private bool _rippleVisual;
        private bool _trailVisual;
        private TrailRenderer _trail;
        private Material _trailMat;
        private Vector3 _lastPosition;
        private Vector2 _mirageOffset;
        private static Texture2D _mirageNormal;
        private Vector3 _baseScale;
        private float _nextPhysicalHit;
        private float _shimmerPhase;

        private State _state;
        private float _stateT;
        private float _age;
        private float _huntT;
        private float _visibleUntil;
        private float _bearingAngle;
        private float _bearingSign;
        private Vector3 _lungeDir;
        private bool _cloaked;

        internal void Bind(AmbusherDirector director, SwarmUnit swarm)
        {
            _director = director;
            _swarm = swarm;

            _vis = Reflect.Get<GameObject>(swarm, "_vis");
            _baseScale = Reflect.Get<Vector3>(swarm, "_baseScale");
            if (_baseScale == Vector3.zero)
            {
                _baseScale = Vector3.one * (director.HexSize * 0.6f);
            }

            _shimmerPhase = UnityEngine.Random.value * 10f;
            _bearingAngle = 180f;
            _bearingSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            _lastPosition = transform.position;
            CreateCloakVisual();
            Enter(State.Hunt);
            SetCloaked(true);

            if (Plugin.LogVerbose.Value)
            {
                Plugin.Log.LogInfo("[ambusher] drone bound (cloaked, hunting).");
            }
        }

        private void Update()
        {
            float dt = WorldTime.Dt;
            if (dt <= 0f)
            {
                return;
            }

            _age += dt;
            if (_age >= LifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }

            if (_director == null || _director.Ship == null)
            {
                return;
            }

            ShipController ship = _director.Ship;
            Vector3 shipPos = ship.transform.position;
            Vector3 toDrone = transform.position - shipPos;
            toDrone.y = 0f;

            // Ram: same role as a swarm drone touching the hull.
            if (toDrone.magnitude <= _director.HexSize * RamRadiusHex)
            {
                Log("rammed the ship");
                ship.OnEnemyHit(1, transform.position);
                Destroy(gameObject);
                return;
            }

            TickShimmer(dt);

            // A damaged drone stays visible for a short window, then recloaks.
            if (!_cloaked && Time.time >= _visibleUntil && _state != State.Windup && _state != State.Fire && _state != State.Lunge)
            {
                SetCloaked(true);
            }

            switch (_state)
            {
                case State.Hunt:
                    TickHunt(dt, ship, shipPos);
                    break;
                case State.Fire:
                    Fire(ship);
                    break;
                case State.Windup:
                    TickWindup(dt, shipPos);
                    break;
                case State.Lunge:
                    TickLunge(dt);
                    break;
                case State.Evade:
                    TickEvade(dt);
                    break;
            }

            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;
            float speedPx = delta.magnitude / Mathf.Max(dt, 0.0001f) / _director.Px;

            if (_rippleVisual && _cloaked && _director != null && delta.sqrMagnitude > 0.0000001f)
            {
                _director.EmitRipple(transform.position, delta, speedPx);
            }

            if (_trailVisual && _trail != null)
            {
                _trail.emitting = _cloaked && speedPx >= Mathf.Max(0f, Plugin.RippleMinSpeedPx.Value)
                    && !_isVisible;
            }

            _lastPosition = transform.position;
        }

        private void TickHunt(float dt, ShipController ship, Vector3 shipPos)
        {
            _stateT += dt;
            _huntT += dt;

            Vector3 forward = ship.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            if (_huntT >= RetargetSeconds)
            {
                _huntT = 0f;
                _bearingAngle = UnityEngine.Random.Range(160f, 200f);
                _bearingSign = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            }

            // Stay behind the current nose: recompute every frame from the stored bearing.
            Vector3 behind = Quaternion.Euler(0f, _bearingAngle * _bearingSign, 0f) * forward;
            Vector3 target = shipPos + behind * (OrbitRadiusPx * _director.Px);
            target.y = shipPos.y;

            float step = Mathf.Max(1f, Plugin.HuntSpeedPx.Value) * _director.Px * dt;

            // Do not fly through the hull while repositioning; skirt it instead.
            Vector3 toShip = shipPos - transform.position;
            toShip.y = 0f;
            Vector3 next;
            if (toShip.magnitude < _director.HexSize * 3f)
            {
                Vector3 away = -toShip.normalized;
                next = transform.position + away * step;
            }
            else
            {
                next = Vector3.MoveTowards(transform.position, target, step);
            }
            next.y = shipPos.y;
            transform.position = next;

            bool atRing = Vector3.Distance(new Vector3(transform.position.x, 0f, transform.position.z),
                                           new Vector3(target.x, 0f, target.z)) <= _director.HexSize * 0.75f;

            if (atRing || _stateT >= Mathf.Max(1f, Plugin.HuntTimeoutSeconds.Value))
            {
                Log(atRing ? "reached ring; firing" : "hunt timeout; firing");
                Enter(State.Fire);
            }
        }

        private void Fire(ShipController ship)
        {
            SetCloaked(false);
            AmbusherLob.Spawn(_director, transform.position, ship, Plugin.LobDamage.Value);
            Enter(State.Windup);
        }

        private void TickWindup(float dt, Vector3 shipPos)
        {
            _stateT += dt;
            float t = Mathf.Clamp01(_stateT / Mathf.Max(0.1f, Plugin.WindupSeconds.Value));

            if (_vis != null)
            {
                _vis.transform.localScale = _baseScale * Mathf.Lerp(1f, 1.45f, t);
            }

            if (_stateT >= Mathf.Max(0.1f, Plugin.WindupSeconds.Value))
            {
                Vector3 away = transform.position - shipPos;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
                away.Normalize();
                _lungeDir = Quaternion.Euler(0f, UnityEngine.Random.Range(-50f, 50f), 0f) * away;

                Enter(State.Lunge);
            }
        }

        private void TickLunge(float dt)
        {
            _stateT += dt;
            transform.position += _lungeDir * (LungeSpeedPx * _director.Px) * dt;

            if (_vis != null)
            {
                float t = 1f - Mathf.Exp(-12f * dt);
                _vis.transform.localScale = Vector3.Lerp(_vis.transform.localScale, _baseScale, t);
            }

            if (_stateT >= LungeSeconds)
            {
                SetCloaked(true);
                Enter(State.Evade);
            }
        }

        private void TickEvade(float dt)
        {
            _stateT += dt;
            transform.position += _lungeDir * (Mathf.Max(1f, Plugin.HuntSpeedPx.Value) * 0.8f * _director.Px) * dt;

            if (_stateT >= EvadeSeconds)
            {
                Enter(State.Hunt);
            }
        }

        internal void TryPhysicalHit(Vector3 projectilePos, float radius, int damage)
        {
            if (!_cloaked || _swarm == null || _director == null || Time.time < _nextPhysicalHit)
            {
                return;
            }

            Vector3 delta = transform.position - projectilePos;
            delta.y = 0f;
            if (delta.magnitude > radius + _director.HexSize * 0.5f)
            {
                return;
            }

            _nextPhysicalHit = Time.time + 0.2f;
            _swarm.TakeDamage(Mathf.Max(1, damage));
        }

        internal void OnDamaged()
        {
            Log("took damage");
            if (_swarm == null)
            {
                return;
            }

            // If the placeholder swarm unit died, let it finish; the GO is being destroyed.
            int hp = Reflect.Get<int>(_swarm, "_hp");
            if (hp <= 0)
            {
                return;
            }

            _visibleUntil = Time.time + CloakBreakSeconds;
            if (_cloaked)
            {
                SetCloaked(false);
            }

            if (Plugin.LogVerbose.Value)
            {
                Plugin.Log.LogInfo("[ambusher] drone hit: cloak broken for 0.75s.");
            }
        }

        private bool _isVisible;

        private void SetCloaked(bool cloaked)
        {
            _cloaked = cloaked;
            _isVisible = !cloaked;

            if (_vis != null)
            {
                _vis.SetActive(!cloaked);
            }

            if (_director != null && _swarm != null)
            {
                _director.SetTargetable(_swarm, !cloaked);
            }
        }

        private void CreateCloakVisual()
        {
            string mode = (Plugin.CloakEffect.Value ?? "auto").Trim().ToLowerInvariant();
            if (mode == "off")
            {
                return;
            }

            if (mode == "trail")
            {
                CreateTrail();
                return;
            }

            if (mode == "ripple")
            {
                _rippleVisual = true;
                return;
            }

            if ((mode == "mirage" || mode == "auto") && Plugin.CloakGlassShader != null)
            {
                CreateMirage();
                return;
            }

            if (mode == "glass" && Plugin.CloakGlassShader != null)
            {
                CreateGlass();
                return;
            }

            if (mode == "glass" && Plugin.CloakGlassShader == null)
            {
                Plugin.Log?.LogWarning("[ambusher] PRISM glass shader not loaded; falling back to shimmer.");
            }

            if (mode != "shimmer" && !Plugin.CloakPlaceholder.Value)
            {
                return;
            }

            CreateShimmer();
        }

        private void CreateTrail()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return;

            _trailMat = new Material(shader);
            _trailMat.color = new Color(0.65f, 0.85f, 1f, Mathf.Clamp01(Plugin.TrailAlpha.Value));

            GameObject go = new GameObject("AmbusherCloakWake");
            go.transform.SetParent(transform, false);
            _trail = go.AddComponent<TrailRenderer>();
            _trail.material = _trailMat;
            _trail.time = Mathf.Max(0.05f, Plugin.TrailTime.Value);
            _trail.minVertexDistance = 0.05f;
            _trail.widthMultiplier = _director.HexSize * Mathf.Max(0.01f, Plugin.TrailWidthHex.Value);
            _trail.textureMode = LineTextureMode.Stretch;
            _trail.alignment = LineAlignment.View;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.65f, 0.85f, 1f), 0f),
                    new GradientColorKey(new Color(0.65f, 0.85f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(Mathf.Clamp01(Plugin.TrailAlpha.Value), 0f),
                    new GradientAlphaKey(Mathf.Clamp01(Plugin.TrailAlpha.Value) * 0.7f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });
            _trail.colorGradient = gradient;
            _trail.emitting = false;

            _trailVisual = true;
            Plugin.Log?.LogInfo("[ambusher] cloak wake trail created.");
        }

        private void CreateMirage()
        {
            Shader shader = Plugin.CloakGlassShader;
            if (shader == null)
            {
                CreateShimmer();
                return;
            }

            _shimmerMat = new Material(shader);
            _shimmerMat.SetFloat("_Refraction", 1.1f);
            _shimmerMat.SetFloat("_Refraction_Power", 1.4f);
            _shimmerMat.SetFloat("_Refraction_Blur_Radius", 0f);
            _shimmerMat.SetFloat("_ENABLE_BLUR", 0f);
            _shimmerMat.SetFloat("_ENABLE_DISPERSION", 0f);
            _shimmerMat.SetFloat("_ENABLE_RAYMARCHING", 0f);
            _shimmerMat.SetFloat("_Normal_Texture_Strength", 0.8f);
            _shimmerMat.SetFloat("_Normal_Texture_Distortion", 0.7f);
            _shimmerMat.SetFloat("_NORMAL_TEXTURE_UNPACK", 0f);
            _shimmerMat.SetFloat("_Normal_Texture_Distortion_Depth_Fade", 0f);
            _shimmerMat.SetFloat("_Smoothness", 0f);
            _shimmerMat.SetFloat("_Metallic", 0f);
            _shimmerMat.SetFloat("_Glossiness", 0f);
            _shimmerMat.SetFloat("_ZWrite", 0f);
            _shimmerMat.SetFloat("_Cull", 0f);
            _shimmerMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _shimmerMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            float opacity = Mathf.Clamp01(Plugin.CloakOpacity.Value);
            _shimmerMat.SetColor("_Base_Colour", new Color(0f, 0f, 0f, 0f));
            _shimmerMat.SetColor("_Tint_Colour", new Color(0.75f, 0.86f, 1f, opacity));
            _shimmerMat.SetColor("_Fresnel_Colour", new Color(0f, 0f, 0f, 0f));
            _shimmerMat.renderQueue = 3000;

            if (_mirageNormal == null)
            {
                _mirageNormal = MakeMirageNormalMap();
            }

            if (_mirageNormal != null)
            {
                _shimmerMat.SetTexture("_Normal_Texture", _mirageNormal);
            }

            _shimmer = new GameObject("AmbusherCloakMirage");
            _shimmer.transform.SetParent(transform, false);
            _shimmer.transform.localScale = Vector3.one * (_director.HexSize * 2.2f);
            MeshFilter filter = _shimmer.AddComponent<MeshFilter>();
            filter.mesh = MakeMirageDisc();
            MeshRenderer renderer = _shimmer.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _shimmerMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _mirageVisual = true;

            Plugin.Log?.LogInfo("[ambusher] cloak mirage visual created.");
        }

        private static Mesh MakeMirageDisc()
        {
            const int segments = 48;
            Vector3[] vertices = new Vector3[segments + 1];
            Vector2[] uvs = new Vector2[segments + 1];
            Vector3[] normals = new Vector3[segments + 1];
            int[] triangles = new int[segments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.forward;

            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                float x = Mathf.Cos(a);
                float y = Mathf.Sin(a);
                vertices[i + 1] = new Vector3(x, y, 0f);
                uvs[i + 1] = new Vector2(x * 0.5f + 0.5f, y * 0.5f + 0.5f);
                normals[i + 1] = Vector3.forward;

                int next = (i + 1) % segments;
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = next + 1;
            }

            Mesh mesh = new Mesh { name = "AmbusherMirageDisc" };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateTangents();
            return mesh;
        }

        private static Texture2D MakeMirageNormalMap()
        {
            const int size = 128;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AmbusherMirageNormals",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1) * Mathf.PI * 2f;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1) * Mathf.PI * 2f;
                    float du = 3f * Mathf.Cos(3f * u + 2f * v) * 0.5f
                               + 5f * Mathf.Cos(5f * u - 3f * v) * 0.3f
                               + 2f * Mathf.Cos(2f * u + 7f * v) * 0.2f;
                    float dv = 2f * Mathf.Cos(3f * u + 2f * v) * 0.5f
                               - 3f * Mathf.Cos(5f * u - 3f * v) * 0.3f
                               + 7f * Mathf.Cos(2f * u + 7f * v) * 0.2f;

                    float nx = x / (float)(size - 1) * 2f - 1f;
                    float ny = y / (float)(size - 1) * 2f - 1f;
                    float radius = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
                    float edge = Mathf.SmoothStep(1f, 0.15f, radius);

                    Vector3 n = new Vector3(-du * 0.09f * edge, -dv * 0.09f * edge, 1f).normalized;
                    pixels[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void CreateGlass()
        {
            Shader shader = Plugin.CloakGlassShader;
            if (shader == null)
            {
                return;
            }

            _shimmerMat = new Material(shader);
            _shimmerMat.SetFloat("_Refraction", 1f);
            _shimmerMat.SetFloat("_Refraction_Power", 1f);
            _shimmerMat.SetFloat("_Refraction_Blur_Radius", 0f);
            _shimmerMat.SetFloat("_ENABLE_BLUR", 0f);
            _shimmerMat.SetFloat("_ENABLE_DISPERSION", 0f);
            _shimmerMat.SetFloat("_ENABLE_RAYMARCHING", 0f);
            _shimmerMat.SetFloat("_Normal_Texture_Distortion", 0.55f);
            _shimmerMat.SetFloat("_Normal_Texture_Strength", 0.2f);
            _shimmerMat.SetFloat("_Normal_Texture_Distortion_Depth_Fade", 0f);
            _shimmerMat.SetFloat("_ZWrite", 0f);
            _shimmerMat.SetFloat("_Cull", 0f);
            _shimmerMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _shimmerMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _shimmerMat.SetColor("_Base_Colour", new Color(0f, 0f, 0f, 0.04f));
            _shimmerMat.SetColor("_Tint_Colour", new Color(0.1f, 0.25f, 0.4f, 0.06f));
            _shimmerMat.SetColor("_Fresnel_Colour", new Color(0f, 0f, 0f, 0f));
            _shimmerMat.renderQueue = 3000;

            _shimmer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = _shimmer.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _shimmer.name = "AmbusherCloakDistortion";
            _shimmer.transform.SetParent(transform, false);
            _shimmer.transform.localScale = Vector3.one * (_director.HexSize * 1.05f);
            MeshRenderer renderer = _shimmer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _shimmerMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _glassVisual = true;

            Plugin.Log?.LogInfo("[ambusher] cloak distortion visual created (PRISM glass).");
        }

        private void CreateShimmer()
        {

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                return;
            }

            _shimmerMat = new Material(shader)
            {
                color = new Color(0.45f, 0.8f, 1f, 0.07f)
            };

            _shimmer = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = _shimmer.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            _shimmer.name = "AmbusherCloakShimmer";
            _shimmer.transform.SetParent(transform, false);
            _shimmer.transform.localScale = Vector3.one * (_director.HexSize * 0.9f);
            MeshRenderer renderer = _shimmer.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _shimmerMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void TickShimmer(float dt)
        {
            if (_shimmer == null) return;

            bool show = _cloaked && (Plugin.CloakPlaceholder.Value || _glassVisual || _mirageVisual);
            if (_shimmer.activeSelf != show)
            {
                _shimmer.SetActive(show);
            }

            if (!show) return;

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f + _shimmerPhase);

            if (_mirageVisual)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    _shimmer.transform.rotation = cam.transform.rotation;
                }

                _shimmer.transform.localScale = Vector3.one * (_director.HexSize * (2.05f + 0.18f * pulse));
                if (_shimmerMat != null)
                {
                    _mirageOffset.x += 0.015f * Time.deltaTime;
                    _mirageOffset.y += 0.022f * Time.deltaTime;
                    _shimmerMat.SetTextureOffset("_Normal_Texture", _mirageOffset);
                    _shimmerMat.SetFloat("_Refraction_Power", 1.1f + 0.5f * pulse);
                    _shimmerMat.SetFloat("_Normal_Texture_Distortion", 0.55f + 0.25f * pulse);
                }
                return;
            }

            if (_glassVisual)
            {
                _shimmer.transform.localScale = Vector3.one * (_director.HexSize * (1.0f + 0.08f * pulse));
                if (_shimmerMat != null)
                {
                    _shimmerMat.SetFloat("_Refraction_Power", 0.65f + 0.7f * pulse);
                    _shimmerMat.SetFloat("_Noise_Radius", 0.002f + 0.004f * pulse);
                }
                return;
            }

            _shimmer.transform.localScale = Vector3.one * (_director.HexSize * (0.82f + 0.12f * pulse));
            if (_shimmerMat != null)
            {
                Color c = _shimmerMat.color;
                c.a = 0.04f + 0.06f * pulse;
                _shimmerMat.color = c;
            }
        }

        private void OnDestroy()
        {
            if (_shimmerMat != null)
            {
                Destroy(_shimmerMat);
            }

            if (_trailMat != null)
            {
                Destroy(_trailMat);
            }
        }

        private void Enter(State state)
        {
            _state = state;
            _stateT = 0f;
            if (state == State.Hunt)
            {
                _huntT = RetargetSeconds;
            }
            Log("state -> " + state);
        }

        private void Log(string message)
        {
            if (Plugin.LogVerbose.Value)
            {
                Plugin.Log?.LogInfo($"[ambusher] drone {message}.");
            }
        }
    }
}
