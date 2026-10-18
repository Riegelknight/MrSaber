using System;
using System.Collections.Generic;
using UnityEngine;
// `System` wird nur für Func<> gebraucht; `Object` muss explizit UnityEngine.Object
// bleiben (Unity-API-Aufrufe wie Object.Instantiate/Object.DontDestroyOnLoad).
using Object = UnityEngine.Object;

namespace BeatSaber
{
    /// <summary>
    /// Gecachte, gepoolte Partikel-Effekte: Schnitt-Funken (richtungstreu), Minen-
    /// Explosionen, Noten-Spawn-Partikel (Prefabs) und Wand-Kontakt-Funken.
    /// </summary>
    public static class Fx
    {
        private sealed class Pool
        {
            /// <summary>Erzeugt die Vorlage für code-gebaute Systeme (null = Prefab laden).</summary>
            public Func<GameObject> Factory;
            /// <summary>Vorlage (geladenes Prefab ODER von der Factory gebaut).</summary>
            public GameObject Prefab;
            public readonly Stack<GameObject> Free = new();
        }

        private sealed class Active
        {
            public GameObject Go;
            public Pool Pool;
            public float ReleaseAt;
        }

        private static readonly Dictionary<string, Pool> _pools = new();
        private static readonly List<Active> _active = new();
        private static readonly Stack<Active> _activeSpare = new();
        private static FxRunner _runner;
        private static MaterialPropertyBlock _colorBlock;

        // Poolschlüssel der code-gebauten Systeme.
        private const string SparkRedKey = "BuiltSparksRed";
        private const string SparkBlueKey = "BuiltSparksBlue";
        private const float MineLifetimeMin = 0.22f, MineLifetimeMax = 0.32f; // s (kurz & scharf)
        private const float MineSizeMin = 0.55f, MineSizeMax = 0.68f;         // m Startdurchmesser
        private const float MineGrow = 1.4f;                                 // Endfaktor (nur größer)
        private const float MineSpinSpeed = 1.4f;                            // rad/s Eigendrehung
        private const int MineSeedRange = 60;                                // Zufalls-Seed je Explosion
        private const int MinePoolMax = 12;                                  // Pool-Obergrenze der 3D-Bälle

        /// <summary>Funken am Schnittpunkt — RICHTUNG = Schwunggeschwindigkeit der
        /// Klingenspitze PLUS ein Vorwärts-Anteil (`roomForward` — der Stoß fliegt immer
        /// ein Stück in den Raum hinein, nicht rein quer). Gestreckte, DÜNNE Streaks,
        /// kaum Gravitation. Kein Schwung → Richtung zum Spieler als Fallback.</summary>
        public static void PlayHit(Vector3 worldPosition, SaberSide side,
            Vector3 swingVelocity, Vector3 roomForward, float scale = 1f)
        {
            bool red = side == SaberSide.Left;
            Vector3 dir = swingVelocity.sqrMagnitude > 1e-6f ? swingVelocity.normalized : Vector3.back;
            // Vorwärts-Anteil (Raum-vorwärts) — „immer ein bisschen mehr nach vorne als gerade".
            dir += roomForward.normalized * GameConfig.SparkForwardBias;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.back;
            Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
            PlayBuilt(
                red ? SparkRedKey : SparkBlueKey,
                worldPosition, rot, Mathf.Max(0.01f, scale),
                red ? GameConfig.SparkColorRed : GameConfig.SparkColorBlue,
                CreateSparkTemplate,
                GameConfig.SparkLifetimeMax + 0.5f);
        }

        private sealed class ContactEmitter
        {
            public GameObject Go;
            public ParticleSystem Ps;
            public bool Active;
        }

        private static readonly Dictionary<int, ContactEmitter> _contactSparks = new();

        private const int ContactChannelWallLeft = 0;
        private const int ContactChannelWallRight = 1;
        private const int ContactChannelClash = 2;

        /// <summary>Wand-Funken (anhaltend): EIN System pro Hand, folgt der Eintrittsstelle
        /// der Klinge in die Wand, solange der Kontakt besteht. `active=false` stoppt nur
        /// die Emission — die bereits ausgestoßenen Funken fallen weiter aus.</summary>
        public static void SetWallContact(SaberSide side, bool active, Vector3 worldPosition, float scale)
        {
            int channel = side == SaberSide.Left ? ContactChannelWallLeft : ContactChannelWallRight;
            var color = side == SaberSide.Left ? GameConfig.SparkColorRed : GameConfig.SparkColorBlue;
            var em = EnsureContact(channel, color);
            UpdateContact(em, active, worldPosition, scale);
        }

        /// <summary>Schwert-Clash (anhaltend): EINE weiße Fontäne an der Berührungsstelle
        /// beider Klingen, solange die Schwerter aufeinanderliegen.</summary>
        public static void SetClashContact(bool active, Vector3 worldPosition, float scale)
        {
            var em = EnsureContact(ContactChannelClash, GameConfig.ClashSparkColor);
            UpdateContact(em, active, worldPosition, scale);
        }

        /// <summary>Stoppt alle Kontakt-Funken sofort (Song-/Menü-Wechsel): Reste fallen aus.</summary>
        public static void StopAllContacts()
        {
            foreach (var kv in _contactSparks)
            {
                if (kv.Value == null || kv.Value.Ps == null) continue;
                kv.Value.Active = false;
                kv.Value.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        private static ContactEmitter EnsureContact(int channel, Color color)
        {
            if (_contactSparks.TryGetValue(channel, out var existing)) return existing;

            var em = new ContactEmitter();
            em.Go = CreateContactFountain(channel, color);
            em.Ps = em.Go.GetComponent<ParticleSystem>();
            Object.DontDestroyOnLoad(em.Go);
            em.Go.SetActive(false);
            em.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _contactSparks[channel] = em;
            return em;
        }

        private static GameObject CreateContactFountain(int channel, Color color)
        {
            var go = new GameObject("ContactSparks[" + channel + "]");
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                GameConfig.ContactSparkLifetimeMin, GameConfig.ContactSparkLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                GameConfig.ContactSparkSpeedMin, GameConfig.ContactSparkSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(
                GameConfig.ContactSparkSizeMin, GameConfig.ContactSparkSizeMax);
            main.startColor = Color.white; // Ton kommt über _Color (MPB)
            main.gravityModifier = GameConfig.ContactSparkGravity; // erst radial sprühen, dann runterziehen
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 400;

            var emission = ps.emission;
            emission.rateOverTime = GameConfig.WallSparkRate; // Daueremission statt Burst
            emission.SetBursts(new ParticleSystem.Burst[0]);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere; // alle Richtungen, nicht nur hoch
            shape.radius = 0.03f;
            shape.radiusThickness = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient();

            var siz = ps.sizeOverLifetime;
            siz.enabled = true;
            siz.size = new ParticleSystem.MinMaxCurve(1f, ShrinkCurve(0.35f));

            ConfigureSparkRenderer(go, 1.0f, 0.05f);

            // HDR-Farbe je Kanal (additiver Spark-Shader, MPB) — dauerhaft für diesen Emitter.
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            if (pr != null) pr.SetPropertyBlock(block);
            return go;
        }

        private static void UpdateContact(ContactEmitter em, bool active,
            Vector3 worldPosition, float scale)
        {
            if (em == null || em.Ps == null) return;

            if (active)
            {
                if (!em.Active)
                {
                    em.Go.SetActive(true);
                    em.Ps.Play(true);
                }
                em.Go.transform.position = worldPosition;
                em.Go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                em.Active = true;
            }
            else if (em.Active)
            {
                em.Active = false;
                // Nur die Emission stoppen — bereits ausgestoßene Funken fallen aus.
                em.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public static void PlayMineExplosion(Vector3 worldPosition, float scale = 1f)
        {
            var mat = MineFireballMaterial();
            if (mat == null) return; // Shader fehlt: Effekt entfällt (kein Fehler)

            GameObject go = _mineFree.Count > 0 ? _mineFree.Pop() : CreateMineFireball();
            var t = go.transform;
            t.SetPositionAndRotation(worldPosition, Quaternion.identity);
            go.SetActive(true);

            float baseScale = Mathf.Max(0.01f, scale);
            go.GetComponent<MineFireballFx>().Init(
                UnityEngine.Random.Range(MineSizeMin, MineSizeMax) * baseScale,
                MineGrow,
                UnityEngine.Random.Range(MineLifetimeMin, MineLifetimeMax),
                UnityEngine.Random.Range(0, MineSeedRange),
                UnityEngine.Random.Range(-MineSpinSpeed, MineSpinSpeed),
                GameConfig.MineFlashColor);
        }

        /// <summary>Spawn-Partikel einer Note an ihrer Erscheinungsposition (Rot = links).
        /// Referenz-Prefabs (ParticleSpawnL/R), Rotation −90° X — bewusst UNVERÄNDERT.</summary>
        public static void PlayNoteSpawn(Vector3 worldPosition, bool red, float scale = 1f)
        {
            PlayPrefab(
                red ? GameConfig.NoteSpawnParticleRedResource
                    : GameConfig.NoteSpawnParticleBlueResource,
                worldPosition, scale);
        }

        // ==================================================================
        // Code-gebaute Einmal-Stöße (Funken / Minen-Explosion)
        // ==================================================================

        private static void PlayBuilt(string key, Vector3 position, Quaternion rotation,
            float scale, Color color, Func<GameObject> factory, float holdSeconds)
        {
            EnsureRunner();
            Pool pool = PoolFor(key, factory);
            GameObject go = pool.Free.Count > 0 ? pool.Free.Pop() : Object.Instantiate(pool.Prefab);
            var t = go.transform;
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            go.SetActive(true);

            // HDR-Farbe je Stoß (additiver Spark-Shader, MPB je Instanz) — die Vorlage
            // hat genau EINEN Renderer (ParticleSystemRenderer am Root).
            if (_colorBlock == null) _colorBlock = new MaterialPropertyBlock();
            _colorBlock.SetColor("_Color", color);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            if (pr != null) pr.SetPropertyBlock(_colorBlock);

            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);

            var entry = _activeSpare.Count > 0 ? _activeSpare.Pop() : new Active();
            entry.Go = go;
            entry.Pool = pool;
            entry.ReleaseAt = Time.time + holdSeconds + 0.1f;
            _active.Add(entry);
        }

        /// <summary>Funken-Vorlage: EIN Burst entlang +Z (Schwungrichtung), gestreckte
        /// Billboard-Streaks, fast schwerelos, HDR-Farbe über den Spark-Shader.</summary>
        private static GameObject CreateSparkTemplate()
        {
            var go = new GameObject("CutSparks");
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.duration = GameConfig.SparkLifetimeMax;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                GameConfig.SparkLifetimeMin, GameConfig.SparkLifetimeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(
                GameConfig.SparkSpeedMin, GameConfig.SparkSpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(
                GameConfig.SparkSizeMin, GameConfig.SparkSizeMax);
            main.startColor = Color.white; // Ton kommt je Stoß über _Color (MPB)
            main.gravityModifier = GameConfig.SparkGravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = GameConfig.SparkCount * 2;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)GameConfig.SparkCount) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = GameConfig.SparkSpreadAngle;
            shape.radius = 0.008f;
            shape.radiusThickness = 1f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = FadeGradient();

            var siz = ps.sizeOverLifetime;
            siz.enabled = true;
            siz.size = new ParticleSystem.MinMaxCurve(1f, ShrinkCurve(0.35f));

            ConfigureSparkRenderer(go, 2.5f, 0.12f);
            return go;
        }

        private static GameObject CreateMineFireball()
        {
            var go = new GameObject("MineFireball");
            Object.DontDestroyOnLoad(go);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = MineSphereMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MineFireballMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.allowOcclusionWhenDynamic = false;
            go.AddComponent<MineFireballFx>().Bind(mr);
            go.SetActive(false);
            return go;
        }

        /// <summary>Prozedurale UV-Kugel (Radius 0,5) — das Volumen der 3D-Explosion. Der Shader
        /// ist unlit, daher nur Vertices/Dreiecke nötig (keine Normalen/UVs). EIN Mesh, gecacht.</summary>
        private static Mesh _mineSphere;

        private static Mesh MineSphereMesh()
        {
            if (_mineSphere != null) return _mineSphere;
            const int slices = 24, stacks = 16;
            var verts = new Vector3[(stacks + 1) * (slices + 1)];
            var tris = new int[stacks * slices * 6];
            int vi = 0;
            for (int st = 0; st <= stacks; st++)
            {
                float phi = (float)st / stacks * Mathf.PI;   // 0 … π (Pol zu Pol)
                float y = Mathf.Cos(phi) * 0.5f;
                float r = Mathf.Sin(phi) * 0.5f;
                for (int sl = 0; sl <= slices; sl++)
                {
                    float th = (float)sl / slices * Mathf.PI * 2f;
                    verts[vi++] = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                }
            }
            int ti = 0;
            for (int st = 0; st < stacks; st++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    int a = st * (slices + 1) + sl;
                    int b = a + slices + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = a + 1; tris[ti++] = b; tris[ti++] = b + 1;
                }
            }
            var mesh = new Mesh { name = "FxMineSphere" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            _mineSphere = mesh;
            return mesh;
        }

        // Gepoolte 3D-Feuerbälle (KEIN ParticleSystem): die MonoBehaviour verwaltet Lebenszeit,
        // Wachstum und Ausfaden selbst und gibt das Objekt hier zurück.
        private static readonly Stack<GameObject> _mineFree = new();

        private static void ReleaseMine(GameObject go)
        {
            go.SetActive(false);
            if (_mineFree.Count < MinePoolMax) _mineFree.Push(go);
            else Object.Destroy(go);
        }

        /// <summary>Animiert EINE 3D-Gas-Explosion: wächst monoton (wird NIE kleiner), fadet am
        /// Ende weich aus und dreht langsam (gasig). Seed + Zufallsparameter machen jede Explosion
        /// etwas anders. HDR-Tint + _Fade/_Seed werden je Instanz über MPB gesetzt.</summary>
        public sealed class MineFireballFx : MonoBehaviour
        {
            private MeshRenderer _mr;
            private MaterialPropertyBlock _mpb;
            private float _start, _duration, _size0, _grow, _spin;

            public void Bind(MeshRenderer mr)
            {
                _mr = mr;
                _mpb = new MaterialPropertyBlock();
            }

            public void Init(float size, float grow, float duration, int seed, float spin, Color color)
            {
                _size0 = Mathf.Max(0.01f, size);
                _grow = Mathf.Max(1f, grow);
                _duration = Mathf.Max(0.05f, duration);
                _spin = spin;
                _start = Time.time;

                transform.localRotation = Quaternion.Euler(0f, 0f, seed * 37f + UnityEngine.Random.Range(0f, 360f));
                transform.localScale = Vector3.one * _size0;

                _mpb.SetColor("_Color", color);
                _mpb.SetFloat("_Seed", seed);
                _mpb.SetFloat("_Fade", 1f);
                if (_mr != null) _mr.SetPropertyBlock(_mpb);
            }

            private void Update()
            {
                if (_mr == null) return;
                float t = (Time.time - _start) / _duration;
                if (t >= 1f)
                {
                    ReleaseMine(gameObject);
                    return;
                }

                // Wachstum: sehr schnell am Anfang (scharfer Knall), monoton steigend
                // (0 → 1) → KEIN Schrumpfen.
                float u = 1f - t;
                float e = 1f - u * u * u;
                transform.localScale = Vector3.one * (_size0 * Mathf.Lerp(1f, _grow, e));
                transform.Rotate(0f, 0f, _spin * Mathf.Rad2Deg * Time.deltaTime, Space.Self);

                // Kurz voll, dann schnell ausfaden (kürzer/schärfer).
                float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.1f) / 0.8f));
                _mpb.SetFloat("_Fade", fade);
                _mr.SetPropertyBlock(_mpb);
            }
        }

        private static Material _mineFireballMat;

        /// <summary>Gemeinsames Material der 3D-Explosion (additiver Volumen-Shader
        /// `BeatSaber/MineFireball`; HDR-Tint + _Fade/_Seed je Instanz über MPB). Fehlt der
        /// Shader, entfällt der Effekt sauber (kein Fehler).</summary>
        private static Material MineFireballMaterial()
        {
            if (_mineFireballMat != null) return _mineFireballMat;
            var shader = Resources.Load<Shader>("BeatSaber/MineFireball");
            if (shader == null) shader = Shader.Find("BeatSaber/MineFireball");
            if (shader == null) return null;
            _mineFireballMat = new Material(shader);
            return _mineFireballMat;
        }

        /// <summary>Renderer der Einmal-Stöße: gestreckte Billboards entlang der
        /// Partikelgeschwindigkeit (= Bewegungs-Streaks, kein Fontänen-Wurf).</summary>
        private static void ConfigureSparkRenderer(GameObject go, float lengthScale, float velocityScale)
        {
            var render = go.GetComponent<ParticleSystemRenderer>();
            render.renderMode = ParticleSystemRenderMode.Stretch;
            render.lengthScale = lengthScale;
            render.velocityScale = velocityScale;
            render.maxParticleSize = 0.5f;
            render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            render.receiveShadows = false;
            var shader = Resources.Load<Shader>("BeatSaber/Spark");
            render.sharedMaterial = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
        }

        /// <summary>Alpha-Fade 1 → 0 über die Lebenszeit (Farbe bleibt weiß — der Ton
        /// kommt vom HDR-_Color des Shaders).</summary>
        private static Gradient FadeGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.5f),
                    new GradientAlphaKey(0f, 1f)
                });
            return g;
        }

        private static AnimationCurve ShrinkCurve(float endScale)
        {
            // 1 → endScale über die Lebenszeit (Streaks schrumpfen statt zu wachsen).
            return new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, endScale));
        }

        // ==================================================================
        // Referenz-Prefab-Pfad (Spawn-Partikel)
        // ==================================================================

        private static void PlayPrefab(string resourcePath, Vector3 worldPosition, float scale)
        {
            Pool pool = PoolFor(resourcePath, null);
            if (pool.Prefab == null) return; // Assets fehlen: Effekt entfällt (kein Fehler)

            EnsureRunner();

            GameObject go = pool.Free.Count > 0 ? pool.Free.Pop() : Object.Instantiate(pool.Prefab);
            var t = go.transform;
            t.SetPositionAndRotation(worldPosition, GameConfig.ParticleRotation);
            t.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            go.SetActive(true);

            // Prefabs sind looping-Systeme: erst stoppen+leeren (setzt die Simulationszeit
            // zurück, damit auch ein Burst bei t=0 erneut ausgelöst wird), dann abspielen.
            var ps = go.GetComponent<ParticleSystem>();
            float hold;
            if (ps != null)
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Play(true);
                hold = ps.main.duration + ps.main.startLifetime.constantMax;
            }
            else
            {
                hold = 1f; // Sicherheitsnetz, falls das Prefab kein ParticleSystem am Root hat
            }

            var entry = _activeSpare.Count > 0 ? _activeSpare.Pop() : new Active();
            entry.Go = go;
            entry.Pool = pool;
            entry.ReleaseAt = Time.time + hold + 0.1f;
            _active.Add(entry);
        }

        // ==================================================================
        // Pools/Rückgabe
        // ==================================================================

        private static Pool PoolFor(string key, Func<GameObject> factory)
        {
            if (_pools.TryGetValue(key, out var pool))
            {
                // Vorlage nachziehen (code-gebaut): beim ersten Play erzeugen.
                if (pool.Prefab == null && pool.Factory != null)
                {
                    pool.Prefab = pool.Factory();
                    Object.DontDestroyOnLoad(pool.Prefab);
                    pool.Prefab.SetActive(false);
                }
                return pool;
            }
            pool = new Pool { Factory = factory };
            _pools.Add(key, pool);
            if (pool.Factory != null)
            {
                pool.Prefab = pool.Factory();
                Object.DontDestroyOnLoad(pool.Prefab);
                pool.Prefab.SetActive(false);
            }
            else
            {
                pool.Prefab = Resources.Load<GameObject>(key);
            }
            return pool;
        }

        private static void EnsureRunner()
        {
            if (_runner != null) return;
            var go = new GameObject("[FxRunner]");
            Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<FxRunner>();
        }

        /// <summary>Gibt fällige Effekte zurück in ihren Pool (läuft im FxRunner-Update).</summary>
        private static void ReleaseDue()
        {
            float now = Time.time;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                if (entry.ReleaseAt > now) continue;

                entry.Go.SetActive(false);
                _active.RemoveAt(i);

                // Pool begrenzen (wie die Referenz: maximal 8 je Effekt)
                if (entry.Pool.Free.Count < GameConfig.ParticlePoolMaxSize)
                    entry.Pool.Free.Push(entry.Go);
                else
                    Object.Destroy(entry.Go);

                entry.Go = null;
                entry.Pool = null;
                _activeSpare.Push(entry);
            }
        }

        private sealed class FxRunner : MonoBehaviour
        {
            private void Update() => ReleaseDue();
        }
    }
}
