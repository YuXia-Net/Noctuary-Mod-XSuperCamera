#pragma warning disable CS8604, CS0169
using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using HarmonyLib;

[assembly: MelonInfo(typeof(XSuperCamera.Plugin), "Noctuary-XSuperCamera", "1.0.0-beta", "洛灯夏夜(Xia)", "https://xiau.net")]
[assembly: MelonGame]

namespace XSuperCamera;

// ── Mod 入口 ───────────────────────────────────────────
public class Plugin : MelonMod
{
    public static MelonPreferences_Entry<float> MoveSpeed   = null!;
    public static MelonPreferences_Entry<float> RotateSpeed = null!;
    public static MelonPreferences_Entry<float> ZoomSpeed   = null!;
    public static MelonPreferences_Entry<float> SlowSpeed   = null!;

    private static bool _patched;

    public override void OnInitializeMelon()
    {
        var cfg = MelonPreferences.CreateCategory("XSuperCamera");
        MoveSpeed   = cfg.CreateEntry("MoveSpeed",   12f, "移动速度");
        RotateSpeed = cfg.CreateEntry("RotateSpeed",  3f, "旋转速度");
        ZoomSpeed   = cfg.CreateEntry("ZoomSpeed",    5f, "滚轮速度");
        SlowSpeed   = cfg.CreateEntry("SlowSpeed",    4f, "Ctrl减速倍率");

        ClassInjector.RegisterTypeInIl2Cpp<FreeCam>();
        SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<Scene, LoadSceneMode>)
            ((s, m) => { FreeCam.OnSceneChange(); Locker.OnSceneChange(); });

        MelonLoader.MelonCoroutines.Start(PatchRoutine());

        LoggerInstance.Msg("╔══════════════════════════════════════╗");
        LoggerInstance.Msg("║   Noctuary XSuperCamera v1.0.0-beta  ║");
        LoggerInstance.Msg("║   作者: 洛灯夏夜(Xia)                ║");
        LoggerInstance.Msg("║   网站: https://xiau.net             ║");
        LoggerInstance.Msg("╚══════════════════════════════════════╝");
        LoggerInstance.Msg("按 F1 显示/隐藏控制面板");
    }

    public override void OnGUI()    => HUD.Draw();
    public override void OnUpdate() => InputHandler.Tick();

    public static void ToggleTime() => Time.timeScale = Time.timeScale > 0f ? 0f : 1f;

    private static System.Collections.IEnumerator PatchRoutine()
    {
        while (!_patched)
        {
            yield return null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }

                foreach (var t in types)
                {
                    if (t == null || !t.Name.Contains("Character")) continue;
                    var m = t.GetMethod("MoveDirection",
                        BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (m == null) continue;

                    var h  = new HarmonyLib.Harmony("xsupercamera");
                    var px = new HarmonyMethod(typeof(MovePatch).GetMethod(nameof(MovePatch.Prefix),   BindingFlags.Static | BindingFlags.Public));
                    var pe = new HarmonyMethod(typeof(MovePatch).GetMethod(nameof(MovePatch.PrefixEnemy), BindingFlags.Static | BindingFlags.Public));

                    h.Patch(m, px);
                    var mb = t.GetMethod("MoveDirectionBackward",
                        BindingFlags.Public | BindingFlags.Instance |
                        BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (mb != null) h.Patch(mb, px);

                    foreach (var t2 in types)
                    {
                        if (t2 == null || !t2.Name.Contains("Enemy")) continue;
                        var m2 = t2.GetMethod("MoveDirection",
                            BindingFlags.Public | BindingFlags.Instance |
                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                        if (m2 != null) h.Patch(m2, pe);
                    }

                    _patched = true;
                    MelonLogger.Msg("Patch OK");
                    yield break;
                }
            }
        }
    }
}

// ── 输入处理 ──────────────────────────────────────────────
public static class InputHandler
{
    public static void Tick()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        if (Input.GetKeyDown(KeyCode.F1))              HUD.ToggleVisible();
        if (ctrl && Input.GetKeyDown(KeyCode.C))       FreeCam.Toggle();
        if (Input.GetKeyDown(KeyCode.F3))              Locker.ToggleChar();
        if (Input.GetKeyDown(KeyCode.F4))              Locker.ToggleEnemy();
        if (Input.GetKeyDown(KeyCode.F5))              Plugin.ToggleTime();
        if (Input.GetKeyDown(KeyCode.F6))              UIHider.Toggle();
        if (Input.GetKeyDown(KeyCode.F7))              FreeCam.ExitKeep();
        if (Input.GetKeyDown(KeyCode.F8))              FreeCam.ExitFollow();
        if (Input.GetKeyDown(KeyCode.F9))              FreeCam.RestoreCamera();

        Locker.Tick();
        FreeCam.FollowTick();
    }
}

// ── Harmony Prefix ────────────────────────────────────────
public static class MovePatch
{
    public static bool Prefix(Il2CppSystem.Object __instance) => !Locker.IsLocked(__instance);
    public static bool PrefixEnemy() => !Locker.EnemyLocked;
}

// ── HUD 面板 ──────────────────────────────────────────────
public static class HUD
{
    private static bool _visible = true;
    private static GUIStyle? _box, _btnOff, _btnOn, _title, _sub, _hint, _sep;
    private static Texture2D? _texBg, _texOff, _texOffHov, _texOn, _texOnHov;
    private static readonly Rect WinBase = new Rect(16, 16, 380, 0);

    private const float BH  = 32f;
    private const float LH  = 24f;
    private const float SH  = 18f;
    private const float PAD = 12f;
    private const float GAP = 6f;
    private const float RG  = 5f;

    public static void ToggleVisible() => _visible = !_visible;

    public static void Draw()
    {
        if (!_visible) return;
        EnsureStyles();

        float iw = WinBase.width - PAD * 2f;
        float bw = (iw - GAP) / 2f;
        float x  = WinBase.x + PAD;

        float h = PAD
            + (LH + 4) + 8 + LH + 10   // 标题区
            + SH + RG                    // 视角分隔
            + (BH + RG) * 2 + (BH + RG) * 2   // 视角按钮（含F8/F9整行）
            + SH + RG                    // 锁定分隔
            + BH + RG                    // 锁定按钮
            + SH + RG                    // 其他分隔
            + (BH + RG) * 2             // 其他按钮
            + SH + RG                    // 底部分隔
            + LH + PAD;                 // 提示行

        var win = WinBase; win.height = h;
        GUI.Box(win, "", _box!);
        float y = WinBase.y + PAD;

        // 标题
        GUI.Label(new Rect(x, y, iw, LH + 4), "✦  Noctuary XSuperCamera  v1.0.0-beta", _title!); y += LH + 8;
        GUI.Label(new Rect(x, y, iw, LH), "By: 洛灯夏夜(Xia)  |  https://xiau.net", _sub!); y += LH + 10;

        // 视角控制
        Sep(x, ref y, iw, "📷  视角控制");
        Row2(x, ref y, bw, "Ctrl+C  自由视角", FreeCam.IsActive, FreeCam.Toggle,
                           "F7  保持视角退出", false,            FreeCam.ExitKeep);
        if (GUI.Button(new Rect(x, y, iw, BH), "F8  保持视角退出(跟随角色移动)", _btnOff!)) FreeCam.ExitFollow();
        y += BH + RG;
        if (GUI.Button(new Rect(x, y, iw, BH), "F9  恢复默认视角", _btnOff!)) FreeCam.RestoreCamera();
        y += BH + RG;

        // 锁定控制
        Sep(x, ref y, iw, "🔒  锁定控制");
        Row2(x, ref y, bw, "F3  锁定角色移动",  Locker.CharLocked,  Locker.ToggleChar,
                           "F4  锁定所有敌人",  Locker.EnemyLocked, Locker.ToggleEnemy);

        // 其他
        Sep(x, ref y, iw, "⚙  其他");
        Row2(x, ref y, bw, "F5  冻结游戏时间",  Time.timeScale == 0f, Plugin.ToggleTime,
                           "F6  隐藏/显示UI",   UIHider.Hidden,       UIHider.Toggle);
        Row2(x, ref y, bw, "F1  隐藏此面板",    false,                HUD.ToggleVisible,
                           "",                  false,                null);

        // 提示
        Sep(x, ref y, iw, "");
        GUI.Label(new Rect(x, y, iw, LH), "移动: WASD/QE  |  Shift加速  |  Ctrl减速  |  滚轮推拉", _hint!);
    }

    private static void Sep(float x, ref float y, float w, string lbl)
    {
        GUI.Label(new Rect(x, y, w, SH),
            lbl.Length > 0 ? lbl : "──────────────────────────────────────",
            lbl.Length > 0 ? _sep! : _hint!);
        y += SH + RG;
    }

    private static void Row2(float x, ref float y, float bw,
        string l1, bool on1, Action? a1,
        string l2, bool on2, Action? a2)
    {
        if (a1 != null && GUI.Button(new Rect(x,          y, bw, BH), l1, on1 ? _btnOn! : _btnOff!)) a1();
        if (a2 != null && GUI.Button(new Rect(x + bw + GAP, y, bw, BH), l2, on2 ? _btnOn! : _btnOff!)) a2();
        y += BH + RG;
    }

    private static void EnsureStyles()
    {
        // 纹理被Unity销毁时重建
        if (_texBg == null || !_texBg)
        {
            _texBg     = Tex(1f,    1f,    1f,    0.97f);
            _texOff    = Tex(0.88f, 0.88f, 0.93f, 1f);
            _texOffHov = Tex(0.75f, 0.75f, 0.88f, 1f);
            _texOn     = Tex(0.18f, 0.45f, 0.85f, 0.97f);
            _texOnHov  = Tex(0.22f, 0.52f, 0.95f, 0.97f);
            _box = null;
        }

        if (_box != null)
        {
            _box.normal.background     = _texBg;
            _btnOff!.normal.background = _texOff;
            _btnOff.hover.background   = _texOffHov;
            _btnOn!.normal.background  = _texOn;
            _btnOn.hover.background    = _texOnHov;
            return;
        }

        _box = new GUIStyle(GUI.skin.box) { normal = { background = _texBg } };

        _title  = Label(17, FontStyle.Bold,   0.10f, 0.10f, 0.20f);
        _sub    = Label(13, FontStyle.Normal, 0.30f, 0.30f, 0.50f);
        _hint   = Label(16, FontStyle.Normal, 0.45f, 0.45f, 0.55f);
        _sep    = Label(13, FontStyle.Bold,   0.15f, 0.15f, 0.45f);
        _btnOff = Btn(_texOff, _texOffHov, new Color(0.1f, 0.1f, 0.2f));
        _btnOn  = Btn(_texOn,  _texOnHov,  Color.white, FontStyle.Bold);
    }

    private static GUIStyle Label(int size, FontStyle fs, float r, float g, float b)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs };
        s.normal.textColor = new Color(r, g, b); return s;
    }

    private static GUIStyle Btn(Texture2D norm, Texture2D hov, Color col, FontStyle fs = FontStyle.Normal)
    {
        var s = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = fs, alignment = TextAnchor.MiddleCenter };
        s.normal.background = norm; s.hover.background = hov;
        s.normal.textColor  = col;  s.hover.textColor  = col;
        s.padding = new RectOffset(6, 6, 3, 3);
        return s;
    }

    private static Texture2D Tex(float r, float g, float b, float a)
    {
        var t = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        t.SetPixel(0, 0, new Color(r, g, b, a)); t.Apply();
        UnityEngine.Object.DontDestroyOnLoad(t);
        return t;
    }
}

// ── 自由视角 ──────────────────────────────────────────────
public class FreeCam : MonoBehaviour
{
    public FreeCam(IntPtr p) : base(p) { }

    public  static bool IsActive => _active;
    private static FreeCam?   _inst;
    private static bool       _active;
    private static Transform? _cam, _camParent;
    private static Vector3    _camPos;
    private static Quaternion _camRot;
    private static readonly List<Behaviour> _disabled = new(8);
    private static bool       _following;
    private static Transform? _followTarget;
    private static Vector3    _followOffset;
    private float _yaw, _pitch;

    // 第一次进入自由视角时保存快照
    private static bool       _hasSnapshot;
    private static Transform? _snapCam, _snapParent;
    private static Vector3    _snapPos;
    private static Quaternion _snapRot;
    private static readonly List<Behaviour> _snapDisabled = new(8);

    public static void OnSceneChange()
    {
        _active = _following = _hasSnapshot = false;
        _cam = _camParent = _followTarget = null;
        _snapCam = _snapParent = null;
        _disabled.Clear(); _snapDisabled.Clear();
        if (_inst != null) { try { Destroy(_inst.gameObject); } catch { } _inst = null; }
        Locker.FreeCamLock = false;
    }

    private static FreeCam EnsureInst()
    {
        if (_inst) return _inst!;
        var go = new GameObject("XSuperCam");
        DontDestroyOnLoad(go);
        return _inst = go.AddComponent<FreeCam>();
    }

    public static void Toggle()
    {
        _following = false;
        _active = !_active;
        if (_active) EnsureInst().Activate(); else _inst?.Deactivate();
    }

    public static void ExitKeep()
    {
        if (!_active) return;
        _active = false;
        Locker.FreeCamLock = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    public static void RestoreCamera()
    {
        if (!_hasSnapshot || _snapCam == null) return;
        try
        {
            // 快照恢复
            _snapCam.SetParent(_snapParent);
            _snapCam.position = _snapPos;
            _snapCam.rotation = _snapRot;

            // 恢复所有控制脚本
            foreach (var b in _disabled)      try { if (b) b.enabled = true; } catch { }
            foreach (var b in _snapDisabled) try { if (b) b.enabled = true; } catch { }
            _snapDisabled.Clear();
            _disabled.Clear();

            // 重置所有状态
            _active = _following = false;
            _cam = _snapCam;
            _camParent = _snapParent;
            _camPos = _snapPos;
            _camRot = _snapRot;
            Locker.FreeCamLock = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
            MelonLogger.Msg("视角已恢复默认");
        }
        catch { }
    }

    public static void ExitFollow()
    {
        if (!_active) return;
        _active = false;
        Locker.FreeCamLock = false;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        _followTarget = FindPlayer();
        if (_followTarget && _cam)
        {
            _followOffset = _cam!.position - _followTarget!.position;
            _following = true;
        }
    }

    public static void FollowTick()
    {
        if (!_following || !_followTarget || !_cam) return;
        try { _cam!.position = _followTarget!.position + _followOffset; }
        catch { _following = false; }
    }

    private static Transform? FindPlayer()
    {
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            try
            {
                if (mb.GetIl2CppType().FullName != "MyCharacterController") continue;
                if (!mb.gameObject.activeInHierarchy) continue;
                if (!Locker.GetInputEnabled(mb, out bool isPlayer) || !isPlayer) continue;
                return mb.transform;
            }
            catch { }
        }
        return null;
    }

    private void Activate()
    {
        var cam = PickCamera();
        if (!cam) { _active = false; return; }
        _cam = cam!.transform; _camParent = _cam.parent;
        _camPos = _cam.position; _camRot = _cam.rotation;
        _cam.SetParent(null);
        _disabled.Clear();
        DisableScripts(_cam.gameObject);
        for (var p = _camParent; p != null; p = p.parent) DisableScripts(p.gameObject);
        var e = _cam.eulerAngles;
        _yaw = e.y; _pitch = e.x > 180f ? e.x - 360f : e.x;
        Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        Locker.FreeCamLock = true;

        // 保存快照
        if (!_hasSnapshot)
        {
            _snapCam    = _cam;
            _snapParent = _camParent;
            _snapPos    = _camPos;
            _snapRot    = _camRot;
            _snapDisabled.Clear();
            foreach (var b in _disabled) _snapDisabled.Add(b);
            _hasSnapshot = true;
        }
    }

    private void Deactivate()
    {
        try { if (_cam) { _cam!.SetParent(_camParent); _cam.position = _camPos; _cam.rotation = _camRot; } } catch { }
        foreach (var b in _disabled) try { if (b) b.enabled = true; } catch { }
        _disabled.Clear();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        Locker.FreeCamLock = false;
    }

    private static Camera? PickCamera()
    {
        foreach (var c in Camera.allCameras)
            if (c && c.enabled && c.tag == "MainCamera") return c;
        Camera? best = null; float minD = float.MaxValue;
        string[] exc = { "UI", "Brightness", "Overlay", "Effect", "Post" };
        foreach (var c in Camera.allCameras)
        {
            if (!c || !c.enabled) continue;
            bool skip = false;
            foreach (var kw in exc) if (c.gameObject.name.Contains(kw)) { skip = true; break; }
            if (!skip && c.depth < minD) { minD = c.depth; best = c; }
        }
        return best;
    }

    private static void DisableScripts(GameObject go)
    {
        try
        {
            foreach (var b in go.GetComponents<Behaviour>())
            {
                if (!b || !b.enabled || b is Camera || b is FreeCam) continue;
                var n = b.GetIl2CppType().FullName ?? "";
                if (n.Contains("Camera") || n.Contains("Cinemachine") || n.Contains("Follow"))
                { b.enabled = false; _disabled.Add(b); }
            }
        }
        catch { }
    }

    void Update()
    {
        if (!_active || !_cam) return;
        float dt   = Time.unscaledDeltaTime;
        bool  ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool  fast = Input.GetKey(KeyCode.LeftShift);
        float spd  = Plugin.MoveSpeed.Value;
        if (fast)      spd *= 3f;
        else if (ctrl) spd /= Mathf.Max(Plugin.SlowSpeed.Value, 0.1f);

        _yaw   += Input.GetAxis("Mouse X") * Plugin.RotateSpeed.Value;
        _pitch  = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * Plugin.RotateSpeed.Value, -89f, 89f);
        _cam!.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

        float s = spd * dt;
        var   t = _cam!;
        if (Input.GetKey(KeyCode.W)) t.position += t.forward * s;
        if (Input.GetKey(KeyCode.S)) t.position -= t.forward * s;
        if (Input.GetKey(KeyCode.A)) t.position -= t.right   * s;
        if (Input.GetKey(KeyCode.D)) t.position += t.right   * s;
        if (Input.GetKey(KeyCode.E)) t.position += t.up      * s;
        if (Input.GetKey(KeyCode.Q)) t.position -= t.up      * s;
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (scroll != 0f) t.position += t.forward * (scroll * Plugin.ZoomSpeed.Value * 20f);
    }
}

//角色/敌人锁定
public static class Locker
{
    private static MonoBehaviour?[] _chars = new MonoBehaviour?[2];
    private static bool _c0, _c1;
    public  static bool CharLocked  => _c0 || _c1;
    public  static bool EnemyLocked { get; private set; }
    public  static bool FreeCamLock;

    private static readonly List<MonoBehaviour> _enemies = new(8);
    private static readonly List<(Transform t, Vector3 p)> _frozen = new(16);

    private static readonly Dictionary<IntPtr, Il2CppSystem.Reflection.MethodInfo> _inputEnabledCache = new();
    private static readonly Dictionary<IntPtr, Il2CppSystem.Reflection.MethodInfo> _aiTreeCache       = new();
    private static readonly Dictionary<IntPtr, Il2CppSystem.Reflection.MethodInfo> _aiStateCache      = new();
    private static readonly Dictionary<IntPtr, Il2CppSystem.Reflection.MethodInfo> _moveCtrlCache     = new();
    private static readonly Dictionary<IntPtr, Il2CppSystem.Reflection.MethodInfo> _stopMoveCache     = new();

    private static readonly Il2CppSystem.Reflection.BindingFlags All =
        Il2CppSystem.Reflection.BindingFlags.NonPublic | Il2CppSystem.Reflection.BindingFlags.Public |
        Il2CppSystem.Reflection.BindingFlags.Instance  | Il2CppSystem.Reflection.BindingFlags.FlattenHierarchy;

    private static readonly Il2CppSystem.Reflection.BindingFlags PubInst =
        Il2CppSystem.Reflection.BindingFlags.Public | Il2CppSystem.Reflection.BindingFlags.Instance;

    private static int _enemyCheckTick;

    public static void OnSceneChange()
    {
        _chars[0] = _chars[1] = null;
        _c0 = _c1 = EnemyLocked = FreeCamLock = false;
        _enemies.Clear(); _frozen.Clear(); _enemyCheckTick = 0;
        _inputEnabledCache.Clear(); _aiTreeCache.Clear();
        _aiStateCache.Clear(); _moveCtrlCache.Clear(); _stopMoveCache.Clear();
    }

    public static bool IsLocked(Il2CppSystem.Object inst)
    {
        if (inst == null) return false;
        if (FreeCamLock)  return true;
        try
        {
            var ptr = inst.Pointer;
            if (_c0 && _chars[0] != null && _chars[0]!.Pointer == ptr) return true;
            if (_c1 && _chars[1] != null && _chars[1]!.Pointer == ptr) return true;
        }
        catch { }
        return false;
    }

    public static void Tick()
    {
        // 每120帧重新扫描新刷新的敌人
        if (EnemyLocked && ++_enemyCheckTick >= 120)
        {
            _enemyCheckTick = 0;
            RefreshEnemies();
        }

        // 冻结敌人位置
        for (int i = 0; i < _frozen.Count; i++)
        {
            var (t, p) = _frozen[i];
            try { if (t) t.position = p; } catch { }
        }

        // 锁定的角色若变成AI则持续禁AI
        for (int i = 0; i < 2; i++)
        {
            if (!(i == 0 ? _c0 : _c1) || _chars[i] == null) continue;
            try
            {
                if (!GetInputEnabled(_chars[i]!, out bool isPlayer) || !isPlayer)
                    SetAI(_chars[i]!, true);
            }
            catch { }
        }
    }

    public static void ToggleChar()
    {
        EnsureChars();
        for (int i = 0; i < 2; i++)
        {
            if (_chars[i] == null) continue;
            try
            {
                if (!GetInputEnabled(_chars[i]!, out bool isPlayer) || !isPlayer) continue;
                ref bool locked = ref (i == 0 ? ref _c0 : ref _c1);
                locked = !locked;
                if (!locked) SetAI(_chars[i]!, false);
                MelonLogger.Msg($"角色{i + 1}移动{(locked ? "锁定" : "解锁")}");
                return;
            }
            catch { }
        }
        MelonLogger.Warning("未找到当前操控角色");
    }

    public static void ToggleEnemy()
    {
        EnsureEnemies();
        EnemyLocked = !EnemyLocked;
        foreach (var mb in _enemies) try { SetAIEnemy(mb, EnemyLocked); } catch { }
        _frozen.Clear();
        if (EnemyLocked)
            foreach (var mb in _enemies)
            {
                try { _frozen.Add((mb.transform, mb.transform.position)); } catch { }
                try { if (mb.transform.parent) _frozen.Add((mb.transform.parent, mb.transform.parent.position)); } catch { }
            }
        MelonLogger.Msg($"敌人{(EnemyLocked ? "锁定" : "解锁")}");
    }

    private static void RefreshEnemies()
    {
        var seen = new HashSet<IntPtr>(_enemies.Count);
        foreach (var e in _enemies) if (e) seen.Add(e.Pointer);

        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            try
            {
                if (mb.GetIl2CppType().FullName != "MyEnemyController") continue;
                if (!mb.gameObject.activeInHierarchy || seen.Contains(mb.Pointer)) continue;
                _enemies.Add(mb);
                SetAIEnemy(mb, true);
                try { _frozen.Add((mb.transform, mb.transform.position)); } catch { }
                try { if (mb.transform.parent) _frozen.Add((mb.transform.parent, mb.transform.parent.position)); } catch { }
            }
            catch { }
        }
    }

    //反射工具
    public static bool GetInputEnabled(MonoBehaviour mb, out bool result)
    {
        result = false;
        var key = mb.Pointer;
        if (!_inputEnabledCache.TryGetValue(key, out var m))
        {
            m = mb.GetIl2CppType().GetMethod("get_InputEnabled", PubInst);
            if (m == null) return false;
            _inputEnabledCache[key] = m;
        }
        result = Il2CppSystem.Convert.ToBoolean(m.Invoke(mb.Cast<Il2CppSystem.Object>(), null));
        return true;
    }

    public static void SetAI(MonoBehaviour mb, bool disable)
    {
        if (!mb) return;
        try
        {
            var key = mb.Pointer;
            if (!_aiTreeCache.TryGetValue(key, out var getTree))
            {
                var et = mb.GetIl2CppType().BaseType;
                getTree = et?.GetMethod("get_aiTree", All) ?? mb.GetIl2CppType().GetMethod("get_aiTree", All);
                if (getTree == null) return;
                _aiTreeCache[key] = getTree;
            }
            var tree = getTree.Invoke(mb.Cast<Il2CppSystem.Object>(), null);
            if (tree == null) return;

            var treePtr = tree.Pointer;
            if (!_aiStateCache.TryGetValue(treePtr, out var setState))
            {
                setState = tree.GetIl2CppType().GetMethod("set_State", PubInst);
                if (setState == null) return;
                _aiStateCache[treePtr] = setState;
            }
            Il2CppSystem.Int32 v = new(); v.m_value = disable ? 0 : 1;
            var a = new Il2CppReferenceArray<Il2CppSystem.Object>(1); a[0] = v.BoxIl2CppObject();
            setState.Invoke(tree, a);
        }
        catch { }
    }

    private static void SetAIEnemy(MonoBehaviour mb, bool disable)
    {
        SetAI(mb, disable);
        if (!disable) return;
        try
        {
            var key = mb.Pointer;
            if (!_moveCtrlCache.TryGetValue(key, out var getMC))
            {
                var at = mb.GetIl2CppType().BaseType?.BaseType;
                getMC = at?.GetMethod("get_moveController", All);
                if (getMC == null) return;
                _moveCtrlCache[key] = getMC;
            }
            var mc = getMC.Invoke(mb.Cast<Il2CppSystem.Object>(), null);
            if (mc == null) return;

            var mcPtr = mc.Pointer;
            if (!_stopMoveCache.TryGetValue(mcPtr, out var stopMove))
            {
                stopMove = mc.GetIl2CppType().GetMethod("StopMove", PubInst);
                if (stopMove == null) return;
                _stopMoveCache[mcPtr] = stopMove;
            }
            stopMove.Invoke(mc, null);
        }
        catch { }
    }

    private static void EnsureChars()
    {
        if (_chars[0] != null && _chars[1] != null) return;
        int idx = 0;
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (idx >= 2) break;
            try
            {
                if (mb.GetIl2CppType().FullName != "MyCharacterController") continue;
                if (!mb.gameObject.activeInHierarchy) continue;
                if (idx == 1 && _chars[0] != null && _chars[0]!.Pointer == mb.Pointer) continue;
                _chars[idx++] = mb;
            }
            catch { }
        }
    }

    private static void EnsureEnemies()
    {
        _enemies.Clear();
        foreach (var mb in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            try { if (mb.GetIl2CppType().FullName == "MyEnemyController" && mb.gameObject.activeInHierarchy) _enemies.Add(mb); }
            catch { }
    }
}

//UI隐藏
public static class UIHider
{
    public  static bool Hidden { get; private set; }
    private static readonly List<Canvas> _cvs = new(16);

    public static void Toggle()
    {
        Hidden = !Hidden;
        if (Hidden)
        {
            _cvs.Clear();
            foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
                try { if (c && c.enabled && c.gameObject.activeInHierarchy) { c.enabled = false; _cvs.Add(c); } } catch { }
            foreach (var cam in Camera.allCameras)
                try { if (cam) cam.cullingMask &= ~(1 << 5); } catch { }
        }
        else
        {
            foreach (var c in _cvs) try { if (c) c.enabled = true; } catch { }
            _cvs.Clear();
            foreach (var cam in Camera.allCameras)
                try { if (cam) cam.cullingMask |= (1 << 5); } catch { }
        }
    }
}