using System;
using System.Collections.Generic;
using System.Reflection;
using BoneLib;
using BoneLib.BoneMenu;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(DarkMatterMenu.DarkMatterMod), "Dark Matter Menu", "2.0.0", "crez")]
[assembly: MelonGame("StressLevelZero", "BONELAB")]

namespace DarkMatterMenu
{
    public class DarkMatterMod : MelonMod
    {
        private static readonly Color Void = new Color(0.55f, 0.1f, 0.9f);
        private static readonly Color Nebula = new Color(0.8f, 0.4f, 1f);
        private static readonly Color Ember = new Color(1f, 0.2f, 0.3f);

        private const float MuzzleOffset = 0.3f;
        private const float AimRange = 500f;
        private static readonly System.Random Rng = new System.Random();

        private readonly List<GameObject> _live = new List<GameObject>();
        private readonly List<KeyValuePair<string, string>> _catalog = new List<KeyValuePair<string, string>>();
        private readonly List<string> _npcBarcodes = new List<string>();
        private readonly List<string> _gunBarcodes = new List<string>();
        private bool _menuBuilt;

        // Spawner
        private int _maxLive = 500;
        private int _spawnCount = 1;
        private float _spawnScale = 1f;
        private bool _spawnAtAim;

        // Gun
        private bool _fireRateOn;
        private float _fireRpm = 3000f;
        private bool _gunSpawnNpcs, _gunSpawnItems;
        private int _shotSpawnCount = 1;
        private bool _gunDeleteOnHit;
        private Il2CppSLZ.Marrow.Gun _lastGun;

        // Player
        private bool _god;
        private bool _invincible;
        private bool _fly;
        private float _flySpeed = 8f;
        private Rigidbody[] _rigBodies;
        private bool _warnedNoStick;
        private float _maxHealthMult = 1f;

        // World
        private float _timeScale = 1f;
        private float _gravityY = -9.81f;
        private bool _autoNuke;
        private float _nukeTimer;

        public override void OnInitializeMelon()
        {
            var guns = ReadBarcodes(typeof(CommonBarcodes.Guns), "Gun: ");
            var npcs = ReadBarcodes(typeof(CommonBarcodes.NPCs), "NPC: ");
            _catalog.AddRange(guns);
            _catalog.AddRange(ReadBarcodes(typeof(CommonBarcodes.Melee), "Melee: "));
            _catalog.AddRange(ReadBarcodes(typeof(CommonBarcodes.Misc), "Misc: "));
            _catalog.AddRange(npcs);
            foreach (var g in guns) _gunBarcodes.Add(g.Value);
            foreach (var n in npcs) _npcBarcodes.Add(n.Value);

            Hooking.OnLevelLoaded += _ => { BuildMenu(); _rigBodies = null; if (_invincible) ApplyHealthMode(); };
            Hooking.OnPreFireGun += OnGunFired;
            // Hook signatures from BoneLib.dll: DamageReceived takes (health, float), DeathImminent takes (health).
            Hooking.OnPlayerDamageReceived += (_, __) => { if (_god) FullHeal(); };
            Hooking.OnPlayerDeathImminent += _ => { if (_god) FullHeal(); };
            Hooking.OnPlayerResurrected += _ => { if (_invincible) ApplyHealthMode(); };
        }

        public override void OnFixedUpdate()
        {
            if (_fly) FlyStep();
        }

        public override void OnUpdate()
        {
            if (_god) FullHeal();

            if (_autoNuke)
            {
                _nukeTimer += Time.unscaledDeltaTime;
                if (_nukeTimer >= 0.5f) { _nukeTimer = 0f; NukeNpcs(); }
            }
        }

        private List<KeyValuePair<string, string>> ReadBarcodes(Type holder, string prefix)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (FieldInfo f in holder.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType != typeof(string)) continue;
                string barcode = f.GetValue(null) as string;
                if (!string.IsNullOrEmpty(barcode))
                    list.Add(new KeyValuePair<string, string>(prefix + f.Name, barcode));
            }
            return list;
        }

        // ---------------------------------------------------------------- MENU
        private void BuildMenu()
        {
            if (_menuBuilt) return;
            _menuBuilt = true;

            Page root = Page.Root.CreatePage("Dark Matter", Void, 128, true);

            // ---- PLAYER ----
            Page player = root.CreatePage("Player", Nebula, 128, true);
            player.CreateBool("Invincible (no damage)", Color.cyan, false, SetInvincible);
            player.CreateBool("God mode (unkillable)", Color.white, false, v => _god = v);
            player.CreateFloat("Max health x", Color.white, 1f, 1f, 1f, 1000f, v => { _maxHealthMult = v; ApplyMaxHealth(); });
            player.CreateBool("Fly", Color.cyan, false, SetFly);
            player.CreateFloat("Fly speed", Color.white, _flySpeed, 2f, 1f, 80f, v => _flySpeed = v);
            player.CreateFunction("Full heal", Color.green, FullHeal);

            // ---- WEAPONS ----
            Page weapons = root.CreatePage("Weapons", Nebula, 128, true);
            weapons.CreateBool("Apply fire rate", Color.white, false, v => _fireRateOn = v);
            weapons.CreateFloat("Fire rate (RPM)", Color.white, _fireRpm, 250f, 50f, 30000f, v => _fireRpm = v);
            weapons.CreateBool("Shots spawn NPCs", Color.white, false, v => _gunSpawnNpcs = v);
            weapons.CreateBool("Shots spawn guns", Color.white, false, v => _gunSpawnItems = v);
            weapons.CreateInt("Spawns per shot", Color.white, 1, 1, 1, 25, v => _shotSpawnCount = v);
            weapons.CreateBool("Shots delete what they hit", Ember, false, v => _gunDeleteOnHit = v);

            // ---- DESTRUCTION ----
            Page chaos = root.CreatePage("Destruction", Ember, 128, true);
            chaos.CreateFunction("NUKE all NPCs", Ember, NukeNpcs);
            chaos.CreateBool("Auto-nuke NPCs (every 0.5s)", Ember, false, v => _autoNuke = v);
            chaos.CreateFunction("Clear all spawns", Color.red, ClearAll);

            // ---- WORLD ----
            Page world = root.CreatePage("World", Nebula, 128, true);
            world.CreateFloat("Time scale", Color.white, 1f, 0.1f, 0.05f, 5f, v => { _timeScale = v; Time.timeScale = v; });
            world.CreateFloat("Gravity Y", Color.white, _gravityY, 1f, -60f, 30f, v => { _gravityY = v; Physics.gravity = new Vector3(0f, v, 0f); });
            world.CreateFunction("Reset time + gravity", Color.white, () =>
            {
                _timeScale = 1f; _gravityY = -9.81f;
                Time.timeScale = 1f; Physics.gravity = new Vector3(0f, -9.81f, 0f);
            });

            // ---- SPAWNER ----
            Page spawner = root.CreatePage("Spawner", Void, 128, true);
            spawner.CreateInt("Amount per click", Color.white, 1, 1, 1, 50, v => _spawnCount = v);
            spawner.CreateFloat("Scale", Color.white, 1f, 0.25f, 0.1f, 20f, v => _spawnScale = v);
            spawner.CreateInt("Max live spawns", Color.white, _maxLive, 50, 20, 2000, v => _maxLive = v);
            spawner.CreateBool("Spawn at aim point", Color.white, false, v => _spawnAtAim = v);
            spawner.CreateFunction("SPAWN EVERY NPC", Ember, () => { foreach (var b in _npcBarcodes) SpawnInFront(b, 1); });
            spawner.CreateFunction("SPAWN EVERY GUN", Ember, () => { foreach (var b in _gunBarcodes) SpawnInFront(b, 1); });

            Page catalog = spawner.CreatePage("Catalog", Color.white, 128, true);
            foreach (var entry in _catalog)
            {
                string barcode = entry.Value;
                catalog.CreateFunction(entry.Key, Color.white, () => SpawnInFront(barcode, _spawnCount));
            }

            MelonLogger.Msg($"Dark Matter Menu: {_catalog.Count} items loaded.");
        }

        // ---------------------------------------------------------------- PLAYER
        // Player_Health field names come from the game's IL2CPP assembly; reflection keeps this compiling
        // even if one is named differently. Check the log for "DarkMatter:" lines if god mode misbehaves.
        private object GetHealth()
        {
            var rm = Player.RigManager;
            if (rm == null) return null;
            return GetMember(rm, "health");
        }

        // Sets the game's own health mode (Mortal / Invincible) so damage is ignored at the source.
        // Enum is parsed by name through reflection since the exact type lives in the game's IL2CPP assembly.
        private void SetInvincible(bool on)
        {
            _invincible = on;
            ApplyHealthMode();
        }

        private void ApplyHealthMode()
        {
            object h = GetHealth();
            if (h == null) return;
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = h.GetType();
            FieldInfo fi = t.GetField("healthMode", f);
            PropertyInfo pi = fi == null ? t.GetProperty("healthMode", f) : null;
            Type enumType = fi != null ? fi.FieldType : pi?.PropertyType;
            if (enumType == null || !enumType.IsEnum)
            {
                MelonLogger.Warning("DarkMatter: healthMode not found on player health; using god mode instead.");
                _god = _invincible;
                return;
            }
            object value = Enum.Parse(enumType, _invincible ? "Invincible" : "Mortal");
            if (fi != null) fi.SetValue(h, value); else pi.SetValue(h, value);
        }

        private void FullHeal()
        {
            object h = GetHealth();
            if (h == null) return;
            object max = GetMember(h, "max_Health");
            if (max is float m) SetMember(h, "curr_Health", m);
        }

        private void ApplyMaxHealth()
        {
            object h = GetHealth();
            if (h == null) return;
            SetMember(h, "max_Health", 100f * _maxHealthMult);
            FullHeal();
        }

        private static object GetMember(object o, string name)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = o.GetType();
            FieldInfo fi = t.GetField(name, f);
            if (fi != null) return fi.GetValue(o);
            PropertyInfo pi = t.GetProperty(name, f);
            return pi?.GetValue(o);
        }

        private static void SetMember(object o, string name, object value)
        {
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            Type t = o.GetType();
            FieldInfo fi = t.GetField(name, f);
            if (fi != null) { fi.SetValue(o, value); return; }
            PropertyInfo pi = t.GetProperty(name, f);
            if (pi != null && pi.CanWrite) pi.SetValue(o, value);
        }

        // ---------------------------------------------------------------- FLY
        // Turns off gravity on every rigidbody in the player's physics rig and drives them with a velocity.
        // Left stick moves along where you look, right stick up/down moves vertically, no input = hover.
        private void SetFly(bool on)
        {
            _fly = on;
            _rigBodies = null;
            if (!on) SetRigGravity(true);
        }

        private bool EnsureRigBodies()
        {
            if (_rigBodies != null && _rigBodies.Length > 0 && _rigBodies[0] != null) return true;
            var rig = Player.GetPhysicsRig();
            if (rig == null) return false;
            var found = rig.GetComponentsInChildren<Rigidbody>();
            _rigBodies = new Rigidbody[found.Length];
            for (int i = 0; i < found.Length; i++) _rigBodies[i] = found[i];
            return _rigBodies.Length > 0;
        }

        private void SetRigGravity(bool on)
        {
            if (!EnsureRigBodies()) return;
            foreach (var rb in _rigBodies)
                if (rb != null) rb.useGravity = on;
        }

        private void FlyStep()
        {
            if (!EnsureRigBodies()) return;
            Transform head = Player.Head;
            if (head == null) return;

            Vector2 move = ReadStick(Player.LeftController);
            Vector2 lift = ReadStick(Player.RightController);

            Vector3 dir = head.forward * move.y + head.right * move.x + Vector3.up * lift.y;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            Vector3 vel = dir * _flySpeed;

            foreach (var rb in _rigBodies)
            {
                if (rb == null) continue;
                rb.useGravity = false;
                rb.velocity = vel;
            }
        }

        // BaseController's stick getter is in the game assembly, which BoneLib doesn't expose, so it's read by name.
        private Vector2 ReadStick(object controller)
        {
            if (controller == null) return Vector2.zero;
            const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (string name in new[] { "GetThumbStickAxis", "GetThumbstickAxis" })
            {
                MethodInfo m = controller.GetType().GetMethod(name, f, null, Type.EmptyTypes, null);
                if (m != null && m.Invoke(controller, null) is Vector2 v) return v;
            }
            FieldInfo fi = controller.GetType().GetField("_thumbstickAxis", f);
            if (fi != null && fi.GetValue(controller) is Vector2 fv) return fv;

            if (!_warnedNoStick)
            {
                _warnedNoStick = true;
                MelonLogger.Warning("DarkMatter: couldn't read thumbstick input; fly will hover only. Controller type: " + controller.GetType().FullName);
            }
            return Vector2.zero;
        }

        // ---------------------------------------------------------------- WEAPONS
        private void OnGunFired(Il2CppSLZ.Marrow.Gun gun)
        {
            if (gun == null) return;
            _lastGun = gun;

            if (_fireRateOn) gun.SetRpm(_fireRpm);

            if (_gunSpawnNpcs || _gunSpawnItems || _gunDeleteOnHit)
                HandleAim(gun.transform);
        }

        private void HandleAim(Transform muzzle)
        {
            Vector3 origin = muzzle.position + muzzle.forward * MuzzleOffset;
            if (!Physics.Raycast(origin, muzzle.forward, out RaycastHit hit, AimRange, Physics.AllLayers)) return;
            if (hit.distance < 0.5f) return;

            if (_gunDeleteOnHit)
            {
                Rigidbody rb = hit.collider.attachedRigidbody;
                if (rb != null && !IsPlayerPart(rb.transform))
                    UnityEngine.Object.Destroy(rb.gameObject);
            }

            if (!(_gunSpawnNpcs || _gunSpawnItems)) return;

            for (int i = 0; i < _shotSpawnCount; i++)
            {
                bool wantNpc = _gunSpawnNpcs && (!_gunSpawnItems || Rng.Next(2) == 0);
                List<string> pool = wantNpc ? _npcBarcodes : _gunBarcodes;
                if (pool.Count == 0) return;
                string barcode = pool[Rng.Next(pool.Count)];

                Vector3 jitter = i == 0 ? Vector3.zero : new Vector3((float)Rng.NextDouble() - 0.5f, 0f, (float)Rng.NextDouble() - 0.5f);
                Vector3 pos = (hit.normal.y > 0.5f ? hit.point + Vector3.up * 0.05f : hit.point + hit.normal * 0.5f) + jitter;
                Vector3 facing = -muzzle.forward; facing.y = 0f;
                Quaternion rot = wantNpc && facing.sqrMagnitude > 0.001f ? Quaternion.LookRotation(facing) : Quaternion.identity;
                HelperMethods.SpawnCrate(barcode, pos, rot, Vector3.one * _spawnScale, true, Track, null);
            }
        }

        private static bool IsPlayerPart(Transform t)
        {
            Transform rig = Player.GetPhysicsRig()?.transform;
            return rig != null && t.IsChildOf(rig.root);
        }

        // ---------------------------------------------------------------- DESTRUCTION
        // Extensions.DealDamage(AIBrain, float) verified from BoneLib.dll.
        private void NukeNpcs()
        {
            var brains = UnityEngine.Object.FindObjectsOfType<Il2CppSLZ.Marrow.AI.AIBrain>();
            foreach (var brain in brains)
            {
                if (brain == null) continue;
                brain.DealDamage(1000000f);
            }
        }

        // ---------------------------------------------------------------- SPAWNING
        private void SpawnInFront(string barcode, int count)
        {
            Transform rig = Player.GetPhysicsRig()?.transform;
            if (rig == null) return;

            Vector3 basePos = rig.position + rig.forward * 1.5f + Vector3.up * 1.0f;
            if (_spawnAtAim && Physics.Raycast(rig.position + Vector3.up * 1.5f, rig.forward, out RaycastHit h, AimRange))
                basePos = h.point + h.normal * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3(((float)Rng.NextDouble() - 0.5f) * 0.6f * count, i * 0.15f, ((float)Rng.NextDouble() - 0.5f) * 0.6f * count);
                HelperMethods.SpawnCrate(barcode, basePos + offset, Quaternion.identity, Vector3.one * _spawnScale, true, Track, null);
            }
        }

        private void Track(GameObject go)
        {
            if (go == null) return;
            _live.Add(go);
            while (_live.Count > _maxLive)
            {
                GameObject oldest = _live[0];
                _live.RemoveAt(0);
                if (oldest != null) UnityEngine.Object.Destroy(oldest);
            }
        }

        private void ClearAll()
        {
            foreach (GameObject go in _live)
                if (go != null) UnityEngine.Object.Destroy(go);
            _live.Clear();
        }
    }
}
