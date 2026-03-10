using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Drop this component onto any GameObject in an empty scene and press Play.
    /// It will programmatically build a visual representation of the Akai MIDI Mix:
    ///
    ///   • 8 channel strips (left → right), each with:
    ///       – 3 knob spheres  (brighten as value increases)
    ///       – Mute button cube (dim green → bright green; yellow in Solo mode)
    ///       – Rec Arm button cube (dim red → bright red)
    ///       – Fader (blue fill rises from the bottom as value increases)
    ///   • Master fader (wider, on the far right)
    ///   • Bank Left / Bank Right button cubes (flash gold on press)
    ///   • Right-side raw MIDI log panel for debugging
    ///
    /// All controls respond live to MIDI input from a connected Akai MIDI Mix.
    ///
    /// Required components (created automatically if absent):
    ///   MidiEventManager, MidiMixRouter, UnityMainThreadDispatcher
    /// </summary>
    public class MidiMixTestScene : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Layout constants
        // ------------------------------------------------------------------ //

        const int   CHANNELS      = MidiMixInputMap.CHANNEL_COUNT; // 8
        const int   KNOB_ROWS     = MidiMixInputMap.KNOB_ROWS;      // 3
        const float CH_SPACING    = 1.4f;
        const float ROW_SPACING   = 1.0f;
        const float FADER_HEIGHT  = 2.0f;
        const float KNOB_SIZE     = 0.44f;
        const float BTN_SIZE      = 0.40f;
        const float FADER_WIDTH   = 0.22f;

        const float MUTE_Y        = -(KNOB_ROWS + 0) * ROW_SPACING;
        const float RECARM_Y      = -(KNOB_ROWS + 1) * ROW_SPACING;
        const float FADER_TOP_Y   = -(KNOB_ROWS + 2) * ROW_SPACING;
        const float FADER_BOT_Y   = FADER_TOP_Y - FADER_HEIGHT;
        const float FADER_CEN_Y   = (FADER_TOP_Y + FADER_BOT_Y) * 0.5f;
        const float BANK_Y        = FADER_BOT_Y - 0.8f;

        const float MASTER_X      = (CHANNELS + 0.7f) * CH_SPACING;
        const float MASTER_TRACK  = 0.40f;
        const float MASTER_FILL   = 0.36f;

        // ------------------------------------------------------------------ //
        // Colors
        // ------------------------------------------------------------------ //

        static readonly Color BG_COLOR            = new Color(0.05f, 0.05f, 0.06f);
        static readonly Color KNOB_MIN_COLOR      = new Color(0.10f, 0.12f, 0.14f);
        static readonly Color KNOB_MAX_COLOR      = new Color(0.25f, 0.70f, 1.00f);
        static readonly Color FADER_TRACK_COLOR   = new Color(0.10f, 0.10f, 0.12f);
        static readonly Color FADER_FILL_COLOR    = new Color(0.25f, 0.70f, 1.00f);
        static readonly Color MUTE_IDLE_COLOR     = new Color(0.08f, 0.18f, 0.08f);
        static readonly Color MUTE_ACTIVE_COLOR   = new Color(0.15f, 0.90f, 0.25f);
        static readonly Color SOLO_ACTIVE_COLOR   = new Color(1.00f, 0.80f, 0.10f);
        static readonly Color RECARM_IDLE_COLOR   = new Color(0.20f, 0.06f, 0.06f);
        static readonly Color RECARM_ACTIVE_COLOR = new Color(0.95f, 0.15f, 0.10f);
        static readonly Color BANK_IDLE_COLOR     = new Color(0.15f, 0.15f, 0.20f);
        static readonly Color BANK_ACTIVE_COLOR   = new Color(1.00f, 0.88f, 0.20f);

        const float FLASH_DURATION = 0.15f;

        // ------------------------------------------------------------------ //
        // Visual element references
        // ------------------------------------------------------------------ //

        readonly Material[] _knobMats    = new Material[KNOB_ROWS * CHANNELS];
        readonly Material[] _muteMats    = new Material[CHANNELS];
        readonly Material[] _recArmMats  = new Material[CHANNELS];
        readonly Transform[] _faderFills = new Transform[CHANNELS];

        Transform _masterFill;
        Material  _bankLeftMat;
        Material  _bankRightMat;

        readonly Light[]          _recArmLights     = new Light[CHANNELS];
        readonly ParticleSystem[] _faderParticles   = new ParticleSystem[CHANNELS];

        MidiMixParticleRefs _assetRefs;

        // ------------------------------------------------------------------ //
        // Debug UI
        // ------------------------------------------------------------------ //

        Text _statusLabel;
        Text _midiLogText;

        readonly Queue<string> _midiLog = new Queue<string>();
        const int              LOG_LINES = 24;
        bool                   _logDirty;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Awake()
        {
            _assetRefs = Resources.Load<MidiMixParticleRefs>(MidiMixParticleRefs.ResourceName);
            if (_assetRefs == null)
                Debug.LogWarning("[MidiMixTestScene] MidiMixParticleRefs not found in Resources — particles will be missing.");

            EnsureCoreComponents();
            BuildScene();
        }

        void OnEnable()
        {
            MidiMixRouter.OnKnob         += HandleKnob;
            MidiMixRouter.OnChannelFader += HandleChannelFader;
            MidiMixRouter.OnMasterFader  += HandleMasterFader;
            MidiMixRouter.OnMute         += HandleMute;
            MidiMixRouter.OnSolo         += HandleSolo;
            MidiMixRouter.OnRecArm       += HandleRecArm;
            MidiMixRouter.OnBankLeft     += HandleBankLeft;
            MidiMixRouter.OnBankRight    += HandleBankRight;

            MidiEventManager.OnNoteOn        += HandleRawNoteOn;
            MidiEventManager.OnNoteOff       += HandleRawNoteOff;
            MidiEventManager.OnControlChange += HandleRawCC;
        }

        void OnDisable()
        {
            MidiMixRouter.OnKnob         -= HandleKnob;
            MidiMixRouter.OnChannelFader -= HandleChannelFader;
            MidiMixRouter.OnMasterFader  -= HandleMasterFader;
            MidiMixRouter.OnMute         -= HandleMute;
            MidiMixRouter.OnSolo         -= HandleSolo;
            MidiMixRouter.OnRecArm       -= HandleRecArm;
            MidiMixRouter.OnBankLeft     -= HandleBankLeft;
            MidiMixRouter.OnBankRight    -= HandleBankRight;

            MidiEventManager.OnNoteOn        -= HandleRawNoteOn;
            MidiEventManager.OnNoteOff       -= HandleRawNoteOff;
            MidiEventManager.OnControlChange -= HandleRawCC;
        }

        void Update()
        {
            if (_statusLabel != null)
            {
                string dev = MidiEventManager.Instance != null
                    ? MidiEventManager.Instance.DeviceName
                    : "MidiEventManager missing";

                _statusLabel.text = dev == "No MIDI Device"
                    ? "No MIDI device detected — connect MIDI Mix and touch any control"
                    : $"Device: {dev}";
            }

            if (_logDirty && _midiLogText != null)
            {
                _midiLogText.text = _midiLog.Count > 0
                    ? string.Join("\n", _midiLog)
                    : "(no events yet)";
                _logDirty = false;
            }

        }

        // ------------------------------------------------------------------ //
        // Scene construction
        // ------------------------------------------------------------------ //

        void EnsureCoreComponents()
        {
            if (Object.FindFirstObjectByType<MidiEventManager>() == null)
                new GameObject("MidiEventManager").AddComponent<MidiEventManager>();

            if (Object.FindFirstObjectByType<UnityMainThreadDispatcher>() == null)
                new GameObject("UnityMainThreadDispatcher").AddComponent<UnityMainThreadDispatcher>();

            if (Object.FindFirstObjectByType<MidiMixRouter>() == null)
                new GameObject("MidiMixRouter").AddComponent<MidiMixRouter>();

            if (Object.FindFirstObjectByType<MidiGridRouter>() == null)
                new GameObject("MidiGridRouter").AddComponent<MidiGridRouter>();

            if (Object.FindFirstObjectByType<MidiMixDataVisualizer>() == null)
                new GameObject("MidiMixDataVisualizer").AddComponent<MidiMixDataVisualizer>();

            if (Object.FindFirstObjectByType<MidiMixCameraRig>() == null)
                new GameObject("MidiMixCameraRig").AddComponent<MidiMixCameraRig>();

            if (Object.FindFirstObjectByType<MidiFighterInteriorSpawner>() == null)
                new GameObject("MidiFighterInteriorSpawner").AddComponent<MidiFighterInteriorSpawner>();
        }

        void BuildScene()
        {
            BuildFaderParticles();
            BuildChannelStrips();
            BuildMasterFader();
            BuildBankButtons();
            BuildRecArmLights();
            BuildUI();
        }

        void BuildChannelStrips()
        {
            var root = new GameObject("MIDI Mix Channels");

            for (int ch = 0; ch < CHANNELS; ch++)
            {
                float cx = ch * CH_SPACING;

                for (int row = 0; row < KNOB_ROWS; row++)
                {
                    int   idx = row * CHANNELS + ch;
                    float y   = -row * ROW_SPACING;
                    var   go  = CreateSphere($"Knob_R{row+1}_Ch{ch+1}", root.transform,
                                             new Vector3(cx, y, 0f), KNOB_SIZE);
                    _knobMats[idx] = SetMaterial(go, KNOB_MIN_COLOR);
                }

                {
                    var go = CreateCube($"Mute_Ch{ch+1}", root.transform,
                                        new Vector3(cx, MUTE_Y, 0f), BTN_SIZE);
                    _muteMats[ch] = SetMaterial(go, MUTE_IDLE_COLOR);
                }

                {
                    var go = CreateCube($"RecArm_Ch{ch+1}", root.transform,
                                        new Vector3(cx, RECARM_Y, 0f), BTN_SIZE);
                    _recArmMats[ch] = SetMaterial(go, RECARM_IDLE_COLOR);
                }

                _faderFills[ch] = BuildFader($"Fader_Ch{ch+1}", root.transform, cx,
                                              FADER_WIDTH, FADER_WIDTH - 0.03f);
            }
        }

        void BuildMasterFader()
        {
            var root = new GameObject("Master Fader");
            _masterFill = BuildFader("Master", root.transform, MASTER_X,
                                     MASTER_TRACK, MASTER_FILL);
        }


        void BuildFaderParticles()
        {
            if (_assetRefs == null) return;

            var root = new GameObject("Fader Particles");

            for (int ch = 0; ch < CHANNELS; ch++)
            {
                var prefab = _assetRefs.faderParticles[ch];
                if (prefab == null) continue;

                var go = Instantiate(prefab, Random.insideUnitSphere * 10f, Quaternion.identity, root.transform);
                go.name = $"FaderParticle_Ch{ch + 1}";

                var ps = go.GetComponent<ParticleSystem>();
                if (ps == null) ps = go.GetComponentInChildren<ParticleSystem>();
                if (ps == null) continue;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                ps.Play();

                _faderParticles[ch] = ps;
            }
        }

        void BuildRecArmLights()
        {
            var root = new GameObject("RecArm Lights");

            for (int ch = 0; ch < CHANNELS; ch++)
            {
                var go = new GameObject($"RecArmLight_Ch{ch + 1}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = Random.insideUnitSphere * 10f;

                var light       = go.AddComponent<Light>();
                light.type      = LightType.Point;
                light.range     = 20f;
                light.intensity = 30f;
                light.color     = Random.ColorHSV(0f, 1f, 1f, 1f, 1f, 1f);
                light.shadows   = LightShadows.Hard;
                light.enabled   = false;

                _recArmLights[ch] = light;
            }
        }

        void BuildBankButtons()
        {
            var root = new GameObject("Bank Buttons");

            var left  = CreateCube("BankLeft",  root.transform,
                                   new Vector3(0f,         BANK_Y, 0f), BTN_SIZE);
            var right = CreateCube("BankRight", root.transform,
                                   new Vector3(CH_SPACING, BANK_Y, 0f), BTN_SIZE);

            _bankLeftMat  = SetMaterial(left,  BANK_IDLE_COLOR);
            _bankRightMat = SetMaterial(right, BANK_IDLE_COLOR);
        }

        Transform BuildFader(string name, Transform parent, float x,
                             float trackWidth, float fillWidth)
        {
            var track = CreateCube($"{name}_Track", parent,
                                   new Vector3(x, FADER_CEN_Y, 0f),
                                   new Vector3(trackWidth, FADER_HEIGHT, trackWidth * 0.8f));
            SetMaterial(track, FADER_TRACK_COLOR);

            var pivot = new GameObject($"{name}_FillPivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = new Vector3(x, FADER_BOT_Y, 0f);

            var fill = CreateCube($"{name}_Fill", pivot.transform,
                                  Vector3.zero,
                                  new Vector3(fillWidth, 0.001f, fillWidth * 0.8f));
            SetMaterial(fill, FADER_FILL_COLOR);
            return fill.transform;
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("Canvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ---- Bottom status bar ----
            var statusPanel = new GameObject("StatusPanel", typeof(RectTransform));
            statusPanel.transform.SetParent(canvasGo.transform, false);
            var statusRect  = statusPanel.GetComponent<RectTransform>();
            statusRect.anchorMin        = new Vector2(0f, 0f);
            statusRect.anchorMax        = new Vector2(1f, 0f);
            statusRect.pivot            = new Vector2(0.5f, 0f);
            statusRect.sizeDelta        = new Vector2(0f, 50f);
            statusRect.anchoredPosition = Vector2.zero;

            var labelGo  = new GameObject("StatusLabel");
            labelGo.transform.SetParent(statusPanel.transform, false);
            _statusLabel             = labelGo.AddComponent<Text>();
            _statusLabel.text        = "No MIDI device detected — connect MIDI Mix and touch any control";
            _statusLabel.alignment   = TextAnchor.MiddleCenter;
            _statusLabel.color       = Color.white;
            _statusLabel.fontSize    = 16;
            _statusLabel.font        = font;
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;

            // ---- Right-side raw MIDI log ----
            var logPanel = new GameObject("MidiLogPanel", typeof(RectTransform));
            logPanel.transform.SetParent(canvasGo.transform, false);
            var logPanelRect  = logPanel.GetComponent<RectTransform>();
            logPanelRect.anchorMin        = new Vector2(1f, 0f);
            logPanelRect.anchorMax        = new Vector2(1f, 1f);
            logPanelRect.pivot            = new Vector2(1f, 0.5f);
            logPanelRect.sizeDelta        = new Vector2(300f, -60f);
            logPanelRect.anchoredPosition = new Vector2(0f, 30f);

            var titleGo   = new GameObject("LogTitle");
            titleGo.transform.SetParent(logPanel.transform, false);
            var titleText = titleGo.AddComponent<Text>();
            titleText.text      = "— Raw MIDI Log —";
            titleText.alignment = TextAnchor.UpperCenter;
            titleText.color     = Color.white;
            titleText.fontSize  = 13;
            titleText.font      = font;
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin        = new Vector2(0f, 1f);
            titleRect.anchorMax        = new Vector2(1f, 1f);
            titleRect.pivot            = new Vector2(0.5f, 1f);
            titleRect.sizeDelta        = new Vector2(0f, 22f);
            titleRect.anchoredPosition = Vector2.zero;

            var logGo    = new GameObject("LogText");
            logGo.transform.SetParent(logPanel.transform, false);
            _midiLogText           = logGo.AddComponent<Text>();
            _midiLogText.text      = "(no events yet)";
            _midiLogText.alignment = TextAnchor.LowerLeft;
            _midiLogText.color     = Color.white;
            _midiLogText.fontSize  = 11;
            _midiLogText.font      = font;
            var logTextRect = logGo.GetComponent<RectTransform>();
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(6f,   6f);
            logTextRect.offsetMax = new Vector2(-6f, -24f);
        }

        // ------------------------------------------------------------------ //
        // MIDI Mix named event handlers
        // ------------------------------------------------------------------ //

        void HandleKnob(int channel, int row, float value)
        {
            int idx = (row - 1) * CHANNELS + (channel - 1);
            if (_knobMats[idx] != null)
                _knobMats[idx].color = Color.Lerp(KNOB_MIN_COLOR, KNOB_MAX_COLOR, value);

        }

        void HandleChannelFader(int channel, float value)
        {
            SetFaderValue(_faderFills[channel - 1], value, FADER_WIDTH - 0.03f);

            var ps = _faderParticles[channel - 1];
            if (ps != null)
            {
                var emission = ps.emission;
                emission.rateOverTime = Mathf.Lerp(0f, 300f, value);
            }
        }

        void HandleMasterFader(float value)
            => SetFaderValue(_masterFill, value, MASTER_FILL);

        void HandleMute(int channel, bool isOn)
        {
            if (_muteMats[channel - 1] != null)
                _muteMats[channel - 1].color = isOn ? MUTE_ACTIVE_COLOR : MUTE_IDLE_COLOR;
        }

        void HandleSolo(int channel, bool isOn)
        {
            if (_muteMats[channel - 1] != null)
                _muteMats[channel - 1].color = isOn ? SOLO_ACTIVE_COLOR : MUTE_IDLE_COLOR;
        }

        void HandleRecArm(int channel, bool isOn)
        {
            if (_recArmMats[channel - 1] != null)
                _recArmMats[channel - 1].color = isOn ? RECARM_ACTIVE_COLOR : RECARM_IDLE_COLOR;

            if (_recArmLights[channel - 1] != null)
                _recArmLights[channel - 1].enabled = isOn;

            if (isOn && _assetRefs != null)
            {
                var prefab = _assetRefs.explosionPrefabs[channel - 1];
                if (prefab != null)
                {
                    var light = _recArmLights[channel - 1];
                    var pos   = light != null ? light.transform.position : Random.insideUnitSphere * 10f;
                    var go    = Instantiate(prefab, pos, Quaternion.identity);
                    Destroy(go, 6f);
                }
            }
        }

        void HandleBankLeft()  => StartCoroutine(Flash(_bankLeftMat,  BANK_ACTIVE_COLOR, BANK_IDLE_COLOR));
        void HandleBankRight() => StartCoroutine(Flash(_bankRightMat, BANK_ACTIVE_COLOR, BANK_IDLE_COLOR));

        // ------------------------------------------------------------------ //
        // Raw MIDI handlers — feed the debug log panel
        // ------------------------------------------------------------------ //

        void HandleRawNoteOn(int note, float vel)  => AppendLog($"Note ON  #{note:D3}  vel={vel:F2}");
        void HandleRawNoteOff(int note)             => AppendLog($"Note OFF #{note:D3}");
        void HandleRawCC(int cc, float val)         => AppendLog($"CC  #{cc:D3}        = {val:F2}");

        void AppendLog(string msg)
        {
            _midiLog.Enqueue(msg);
            while (_midiLog.Count > LOG_LINES) _midiLog.Dequeue();
            _logDirty = true;
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        static void SetFaderValue(Transform fill, float value, float fillWidth)
        {
            if (fill == null) return;
            float height = Mathf.Max(0.001f, value * FADER_HEIGHT);
            float depth  = fillWidth * 0.8f;
            fill.localScale    = new Vector3(fillWidth, height, depth);
            fill.localPosition = new Vector3(0f, height * 0.5f, 0f);
        }

        IEnumerator Flash(Material mat, Color on, Color off)
        {
            if (mat == null) yield break;
            mat.color = on;
            yield return new WaitForSeconds(FLASH_DURATION);
            mat.color = off;
        }

        static GameObject CreateSphere(string name, Transform parent, Vector3 pos, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = Vector3.one * size;
            return go;
        }

        static GameObject CreateCube(string name, Transform parent, Vector3 pos, float size)
            => CreateCube(name, parent, pos, Vector3.one * size);

        static GameObject CreateCube(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale    = scale;
            return go;
        }

        /// <summary>Creates a URP/Lit material with the given base color.</summary>
        static Material SetMaterial(GameObject go, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            mat.color = color;
            mat.SetFloat("_Metallic",   0.05f);
            mat.SetFloat("_Smoothness", 0.60f);
            go.GetComponent<Renderer>().material = mat;
            return mat;
        }
    }
}
