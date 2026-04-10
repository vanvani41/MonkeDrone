using BepInEx;
using UnityEngine;
using System;

// ═══════════════════════════════════════════════════════════════
//  FPV DRONE MOD  ·  Gorilla Tag  ·  BepInEx 5  ·  net472
//  Namespace: MonkeDrone  ·  Class: Mod
// ═══════════════════════════════════════════════════════════════

namespace MonkeDrone
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Mod : BaseUnityPlugin
    {
        public static Mod Instance { get; private set; }

        // ─── CALIBRATION ────────────────────────────────────────────
        public enum CalibPhase { Center, Edges, SelectType, Done }
        public CalibPhase calibPhase = CalibPhase.Center;
        public float calibTimer = 7f;
        public bool calibDone = false;

        readonly float[] axisMin = new float[10];
        readonly float[] axisMax = new float[10];
        readonly float[] axisCenter = new float[10];
        bool isGamepad = true;

        // ─── MENU ───────────────────────────────────────────────────
        bool menuOpen = false;
        int menuSel = 0;

        int throttleAxis = 1;
        int yawAxis = 0;
        int pitchAxis = 3;
        int rollAxis = 2;
        int armAxis = 5;

        public enum FlightMode { Acro, Angle, Horizon }
        public enum SpeedMode { Slow, Middle, Fast }
        FlightMode flightMode = FlightMode.Angle;
        SpeedMode speedMode = SpeedMode.Middle;

        readonly string[] menuLabels = { "Throttle Axis", "Yaw Axis", "Pitch Axis",
                                             "Roll Axis",    "Arm Axis", "Mode", "Speed" };
        readonly string[][] menuOptions =
        {
            new[]{"0","1","2","3","4","5","6","7","8","9"},
            new[]{"0","1","2","3","4","5","6","7","8","9"},
            new[]{"0","1","2","3","4","5","6","7","8","9"},
            new[]{"0","1","2","3","4","5","6","7","8","9"},
            new[]{"0","1","2","3","4","5","6","7","8","9"},
            new[]{"Acro","Angle","Horizon"},
            new[]{"Slow","Middle","Fast"}
        };

        // ─── ARMING ─────────────────────────────────────────────────
        bool armed = false;
        bool prevArmSwitch = false;
        bool showThrottleWarn = false;

        // ─── DRONE OBJECT ───────────────────────────────────────────
        GameObject droneObj;
        Camera droneCam;
        Rigidbody droneRb;
        bool droneReady = false;

        // ─── STATS ──────────────────────────────────────────────────
        float flightTimer = 0f;

        // ─── SPEED PRESETS ───────────────────────────────────────────
        readonly float[] thrustPreset = { 9f, 20f, 38f };
        readonly float[] ratePreset = { 90f, 240f, 480f };
        readonly float[] angleLimitPre = { 25f, 50f, 75f };
        readonly float[] kpPreset = { 8f, 10f, 14f };

        // ─── GUI STYLES ──────────────────────────────────────────────
        GUIStyle styleLabel;
        GUIStyle styleBig;
        GUIStyle styleHint;
        bool stylesInit = false;

        // ════════════════════════════════════════════════════════════
        //  AWAKE  —  викликається BepInEx при старті
        // ════════════════════════════════════════════════════════════

        void Awake()
        {
            Instance = this;
            Logger.LogInfo("[MonkeDrone] Mod loaded!");

            for (int i = 0; i < 10; i++)
            {
                axisMin[i] = -1f;
                axisMax[i] = 1f;
                axisCenter[i] = 0f;
            }
        }

        // ════════════════════════════════════════════════════════════
        //  INITIALIZE  —  викликається з Plugin.cs / OnPlayerSpawned
        // ════════════════════════════════════════════════════════════

        public static void Init()
        {
            if (Instance == null) return;

            Instance.calibDone = false;
            Instance.calibPhase = CalibPhase.Center;
            Instance.calibTimer = 7f;
            Instance.armed = false;
            Instance.droneReady = false;
            Instance.flightTimer = 0f;

            // Якщо дрон вже існував — знищити
            if (Instance.droneObj != null)
                Destroy(Instance.droneObj);

            Logger.LogInfo("[MonkeDrone] Initialized — calibration started");
        }

        // ════════════════════════════════════════════════════════════
        //  UPDATE
        // ════════════════════════════════════════════════════════════

        void Update()
        {
            if (!calibDone) { UpdateCalib(); return; }

            if (Input.GetKeyDown(KeyCode.F))
                menuOpen = !menuOpen;

            if (!menuOpen)
            {
                UpdateArm();
            }
            else
            {
                HandleMenuKeys();
            }
        }

        // ════════════════════════════════════════════════════════════
        //  CALIBRATION
        // ════════════════════════════════════════════════════════════

        void UpdateCalib()
        {
            calibTimer -= Time.deltaTime;

            if (calibPhase == CalibPhase.Center && calibTimer <= 0f)
            {
                for (int i = 0; i < 10; i++)
                {
                    axisCenter[i] = RawAxis(i);
                    axisMin[i] = axisCenter[i];
                    axisMax[i] = axisCenter[i];
                }
                calibPhase = CalibPhase.Edges;
                calibTimer = 7f;
            }
            else if (calibPhase == CalibPhase.Edges)
            {
                for (int i = 0; i < 10; i++)
                {
                    float v = RawAxis(i);
                    if (v < axisMin[i]) axisMin[i] = v;
                    if (v > axisMax[i]) axisMax[i] = v;
                }
                if (calibTimer <= 0f)
                {
                    calibPhase = CalibPhase.SelectType;
                    calibTimer = float.MaxValue;
                }
            }
            else if (calibPhase == CalibPhase.SelectType)
            {
                bool pickGamepad = Input.GetKeyDown(KeyCode.Alpha1)
                                || Input.GetKeyDown(KeyCode.Keypad1)
                                || Input.GetKeyDown(KeyCode.JoystickButton0);
                bool pickFPV = Input.GetKeyDown(KeyCode.Alpha2)
                                || Input.GetKeyDown(KeyCode.Keypad2)
                                || Input.GetKeyDown(KeyCode.JoystickButton1);

                if (pickGamepad || pickFPV)
                {
                    isGamepad = pickGamepad;
                    calibDone = true;
                    calibPhase = CalibPhase.Done;
                    SpawnDrone();
                    Logger.LogInfo("[MonkeDrone] Calibration done. Type: "
                                   + (isGamepad ? "Gamepad" : "FPV Radio"));
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  SPAWN DRONE
        // ════════════════════════════════════════════════════════════

        void SpawnDrone()
        {
            Vector3 spawnPos = Vector3.up * 2f;
            if (Camera.main != null)
                spawnPos = Camera.main.transform.position
                         + Camera.main.transform.forward * 0.5f;

            droneObj = new GameObject("FPV_Drone");
            droneObj.transform.position = spawnPos;

            // Rigidbody
            droneRb = droneObj.AddComponent<Rigidbody>();
            droneRb.mass = 0.25f;
            droneRb.drag = 1.8f;
            droneRb.angularDrag = 4f;
            droneRb.useGravity = true;
            droneRb.maxAngularVelocity = 50f;

            // Візуальний куб (червоний) щоб бачити дрон у світі
            GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vis.transform.SetParent(droneObj.transform, false);
            vis.transform.localScale = Vector3.one * 0.12f;
            Destroy(vis.GetComponent<Collider>());
            var rend = vis.GetComponent<Renderer>();
            if (rend) rend.material.color = Color.red;

            // FPV камера
            droneCam = droneObj.AddComponent<Camera>();
            droneCam.fieldOfView = 95f;
            droneCam.nearClipPlane = 0.05f;
            droneCam.depth = 2;
            droneCam.clearFlags = CameraClearFlags.Skybox;
            droneCam.enabled = false;

            // Нахил камери вперед як у реального дрона
            droneCam.transform.localEulerAngles = new Vector3(15f, 0f, 0f);

            droneReady = true;
            Logger.LogInfo("[MonkeDrone] Drone spawned at " + spawnPos);
        }

        // ════════════════════════════════════════════════════════════
        //  ARMING
        // ════════════════════════════════════════════════════════════

        void UpdateArm()
        {
            if (!droneReady) return;

            bool armSwitch = NormAxis(armAxis) > 0.5f;

            if (armSwitch != prevArmSwitch)
            {
                if (armSwitch)
                {
                    if (ThrottleIsLow())
                        Arm();
                    else
                        showThrottleWarn = true;
                }
                else
                {
                    Disarm();
                }
                prevArmSwitch = armSwitch;
            }

            if (armSwitch && !armed) showThrottleWarn = true;
            else if (!armSwitch) showThrottleWarn = false;
        }

        bool ThrottleIsLow()
        {
            float raw = RawAxis(throttleAxis);
            float range = axisMax[throttleAxis] - axisMin[throttleAxis];
            if (range < 0.001f) return true;
            float norm = (raw - axisMin[throttleAxis]) / range;
            return norm < 0.12f;
        }

        void Arm()
        {
            armed = true;
            showThrottleWarn = false;
            flightTimer = 0f;

            if (Camera.main != null) Camera.main.enabled = false;
            if (droneCam != null) droneCam.enabled = true;

            Logger.LogInfo("[MonkeDrone] ARMED");
        }

        void Disarm()
        {
            armed = false;
            showThrottleWarn = false;

            if (droneCam != null) droneCam.enabled = false;
            if (Camera.main != null) Camera.main.enabled = true;

            Logger.LogInfo("[MonkeDrone] DISARMED");
        }

        // ════════════════════════════════════════════════════════════
        //  FLIGHT PHYSICS (FixedUpdate)
        // ════════════════════════════════════════════════════════════

        void FixedUpdate()
        {
            if (!droneReady || !armed) return;

            int si = (int)speedMode;
            float maxThrust = thrustPreset[si];
            float maxRate = ratePreset[si];
            float maxAngle = angleLimitPre[si];
            float kp = kpPreset[si];

            float throttleNorm = ThrottleNorm();
            float yawIn = NormAxis(yawAxis);
            float pitchIn = NormAxis(pitchAxis);
            float rollIn = NormAxis(rollAxis);

            // Тяга
            float gravComp = droneRb.mass * Physics.gravity.magnitude;
            float thrust = throttleNorm * maxThrust + gravComp;
            droneRb.AddForce(droneObj.transform.up * thrust, ForceMode.Force);

            // Ротація
            switch (flightMode)
            {
                case FlightMode.Acro:
                    FlightAcro(pitchIn, rollIn, yawIn, maxRate);
                    break;

                case FlightMode.Angle:
                    FlightAngle(pitchIn, rollIn, yawIn, maxAngle, maxRate, kp);
                    break;

                case FlightMode.Horizon:
                    float blend = Mathf.Clamp01(
                        (Mathf.Abs(pitchIn) + Mathf.Abs(rollIn) - 0.6f) / 0.4f);
                    FlightAngle(pitchIn * (1f - blend), rollIn * (1f - blend),
                                yawIn, maxAngle, maxRate * (1f - blend), kp);
                    FlightAcro(pitchIn * blend, rollIn * blend, 0f, maxRate * blend);
                    break;
            }

            flightTimer += Time.fixedDeltaTime;
        }

        void FlightAcro(float pitch, float roll, float yaw, float maxRate)
        {
            float rad = maxRate * Mathf.Deg2Rad;
            Vector3 torque = new Vector3(
                 pitch * rad * 0.3f,
                 yaw * rad * 0.15f,
                -roll * rad * 0.3f
            );
            droneRb.AddRelativeTorque(torque, ForceMode.Force);
        }

        void FlightAngle(float pitch, float roll, float yaw,
                         float maxAngle, float maxRate, float kp)
        {
            float targetPitch = pitch * maxAngle;
            float targetRoll = roll * maxAngle;

            Vector3 euler = droneObj.transform.eulerAngles;
            float curPitch = euler.x > 180f ? euler.x - 360f : euler.x;
            float curRoll = euler.z > 180f ? euler.z - 360f : euler.z;

            float pitchErr = targetPitch - curPitch;
            float rollErr = targetRoll - curRoll;

            Vector3 torque = new Vector3(
                 pitchErr * kp * Time.fixedDeltaTime,
                 yaw * maxRate * Mathf.Deg2Rad * 0.1f,
                -rollErr * kp * Time.fixedDeltaTime
            );
            droneRb.AddRelativeTorque(torque, ForceMode.Force);
        }

        float ThrottleNorm()
        {
            float raw = RawAxis(throttleAxis);
            float range = axisMax[throttleAxis] - axisMin[throttleAxis];
            if (range < 0.001f) return 0f;
            return Mathf.Clamp01((raw - axisMin[throttleAxis]) / range);
        }

        // ════════════════════════════════════════════════════════════
        //  INPUT HELPERS
        // ════════════════════════════════════════════════════════════

        float RawAxis(int idx)
        {
            try { return Input.GetAxisRaw("Joystick1Axis" + idx); }
            catch { return 0f; }
        }

        float NormAxis(int idx)
        {
            float raw = RawAxis(idx);
            float center = axisCenter[idx];
            if (raw >= center)
            {
                float range = axisMax[idx] - center;
                return range < 0.001f ? 0f : Mathf.Clamp01((raw - center) / range);
            }
            else
            {
                float range = center - axisMin[idx];
                return range < 0.001f ? 0f : -Mathf.Clamp01((center - raw) / range);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  MENU INPUT
        // ════════════════════════════════════════════════════════════

        void HandleMenuKeys()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
                menuSel = Mathf.Max(0, menuSel - 1);
            if (Input.GetKeyDown(KeyCode.DownArrow))
                menuSel = Mathf.Min(menuLabels.Length - 1, menuSel + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                ChangeMenuValue(menuSel, -1);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                ChangeMenuValue(menuSel, +1);
        }

        void ChangeMenuValue(int item, int delta)
        {
            int maxOpt = menuOptions[item].Length;
            switch (item)
            {
                case 0: throttleAxis = Wrap(throttleAxis + delta, maxOpt); break;
                case 1: yawAxis = Wrap(yawAxis + delta, maxOpt); break;
                case 2: pitchAxis = Wrap(pitchAxis + delta, maxOpt); break;
                case 3: rollAxis = Wrap(rollAxis + delta, maxOpt); break;
                case 4: armAxis = Wrap(armAxis + delta, maxOpt); break;
                case 5: flightMode = (FlightMode)Wrap((int)flightMode + delta, maxOpt); break;
                case 6: speedMode = (SpeedMode)Wrap((int)speedMode + delta, maxOpt); break;
            }
        }

        static int Wrap(int x, int m) => ((x % m) + m) % m;

        // ════════════════════════════════════════════════════════════
        //  GUI
        // ════════════════════════════════════════════════════════════

        void OnGUI()
        {
            EnsureStyles();

            if (!calibDone) { DrawCalib(); return; }
            if (menuOpen) { DrawMenu(); return; }
            if (droneReady) DrawOSD();
        }

        // ── Калібровка ───────────────────────────────────────────────
        void DrawCalib()
        {
            int W = Screen.width, H = Screen.height;

            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string title = "", body = "";
            switch (calibPhase)
            {
                case CalibPhase.Center:
                    title = "CALIBRATION  —  STEP 1 / 3";
                    body = "Постав обидва стіки по ЦЕНТРУ\nНічого не чіпай\n\n"
                          + $"Залишилось:  {Mathf.CeilToInt(calibTimer)} сек";
                    break;
                case CalibPhase.Edges:
                    title = "CALIBRATION  —  STEP 2 / 3";
                    body = "Повільно крути обидва стіки до ВСІХ КРАЇВ\n(повне коло в кожну сторону)\n\n"
                          + $"Залишилось:  {Mathf.CeilToInt(calibTimer)} сек";
                    break;
                case CalibPhase.SelectType:
                    title = "CALIBRATION  —  STEP 3 / 3";
                    body = "Вибери тип контролера:\n\n"
                          + "[1]  або  [Button A]  →  Gamepad\n"
                          + "[2]  або  [Button B]  →  FPV Radio (Taranis / EdgeTX / ін.)";
                    break;
            }

            GUI.Label(new Rect(W * 0.1f, H * 0.28f, W * 0.8f, 50f), title, styleBig);
            GUI.Label(new Rect(W * 0.1f, H * 0.42f, W * 0.8f, 180f), body, styleLabel);
        }

        // ── OSD ──────────────────────────────────────────────────────
        void DrawOSD()
        {
            int W = Screen.width, H = Screen.height;

            // Arm індикатор (лівий верх)
            GUIStyle armSt = new GUIStyle(styleLabel);
            armSt.alignment = TextAnchor.UpperLeft;
            armSt.normal.textColor = armed ? Color.green : Color.red;
            GUI.Label(new Rect(14, 10, 150, 32), armed ? "● ARMED" : "○ DISARMED", armSt);

            // Таймер (лівий низ)
            TimeSpan ts = TimeSpan.FromSeconds(flightTimer);
            string timerTxt = $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds / 100}";
            GUIStyle timerSt = new GUIStyle(styleLabel);
            timerSt.alignment = TextAnchor.LowerLeft;
            timerSt.normal.textColor = Color.white;
            GUI.Label(new Rect(14, H - 90, 160, 32), timerTxt, timerSt);

            // Mode + Speed (правий низ)
            GUIStyle rightSt = new GUIStyle(styleLabel);
            rightSt.alignment = TextAnchor.LowerRight;
            rightSt.normal.textColor = ModeColor();
            GUI.Label(new Rect(W - 174, H - 90, 160, 32),
                      flightMode.ToString().ToUpper(), rightSt);
            rightSt.normal.textColor = Color.white;
            GUI.Label(new Rect(W - 174, H - 56, 160, 32),
                      speedMode.ToString().ToUpper(), rightSt);

            // Два стіки (центр знизу)
            float stickSz = 72f;
            float gap = 18f;
            float stickX = (W - stickSz * 2 - gap) * 0.5f;
            float stickY = H - stickSz - 14f;

            DrawStick(new Rect(stickX, stickY, stickSz, stickSz),
                      NormAxis(yawAxis), ThrottleNorm() * 2f - 1f, "L");
            DrawStick(new Rect(stickX + stickSz + gap, stickY, stickSz, stickSz),
                      NormAxis(rollAxis), NormAxis(pitchAxis), "R");

            // Центральний текст
            if (showThrottleWarn)
            {
                GUIStyle warnSt = new GUIStyle(styleBig);
                warnSt.normal.textColor = Color.red;
                GUI.Label(new Rect(W * 0.15f, H * 0.52f, W * 0.7f, 48f),
                          "LOWER THROTTLE!!", warnSt);
            }
            else if (!armed)
            {
                GUIStyle dsSt = new GUIStyle(styleBig);
                dsSt.normal.textColor = new Color(1f, 0.3f, 0.3f);
                GUI.Label(new Rect(W * 0.15f, H * 0.52f, W * 0.7f, 48f),
                          "DISARMED", dsSt);
            }
        }

        Color ModeColor()
        {
            switch (flightMode)
            {
                case FlightMode.Acro: return new Color(1f, 0.4f, 0.4f);
                case FlightMode.Angle: return new Color(0.4f, 1f, 0.4f);
                case FlightMode.Horizon: return new Color(1f, 0.85f, 0.2f);
                default: return Color.white;
            }
        }

        void DrawStick(Rect box, float x, float y, string label)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.18f);
            GUI.DrawTexture(new Rect(box.x + box.width * 0.5f - 1f, box.y, 2f, box.height),
                            Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y + box.height * 0.5f - 1f, box.width, 2f),
                            Texture2D.whiteTexture);

            float dotSz = 11f;
            float dotX = box.x + (x * 0.5f + 0.5f) * box.width - dotSz * 0.5f;
            float dotY = box.y + (-y * 0.5f + 0.5f) * box.height - dotSz * 0.5f;
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(dotX, dotY, dotSz, dotSz), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle lbSt = new GUIStyle(styleHint) { alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(box.x, box.yMax + 2f, box.width, 18f), label, lbSt);
        }

        // ── Меню ─────────────────────────────────────────────────────
        void DrawMenu()
        {
            int W = Screen.width, H = Screen.height;
            float mw = 430f, mh = 420f;
            float mx = (W - mw) * 0.5f, my = (H - mh) * 0.5f;

            GUI.color = new Color(0.04f, 0.06f, 0.12f, 0.93f);
            GUI.DrawTexture(new Rect(mx, my, mw, mh), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUIStyle titleSt = new GUIStyle(styleBig);
            titleSt.normal.textColor = new Color(0.4f, 0.85f, 1f);
            GUI.Label(new Rect(mx, my + 10f, mw, 38f), "FPV DRONE  —  SETTINGS", titleSt);

            int[] curVals = { throttleAxis, yawAxis, pitchAxis, rollAxis,
                              armAxis, (int)flightMode, (int)speedMode };

            for (int i = 0; i < menuLabels.Length; i++)
            {
                bool sel = (i == menuSel);
                float rowY = my + 58f + i * 46f;
                string optTxt = menuOptions[i][curVals[i]];

                if (sel)
                {
                    GUI.color = new Color(0f, 0.45f, 0.9f, 0.28f);
                    GUI.DrawTexture(new Rect(mx + 6f, rowY - 3f, mw - 12f, 40f),
                                    Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                GUIStyle lSt = new GUIStyle(styleLabel);
                lSt.alignment = TextAnchor.MiddleLeft;
                lSt.normal.textColor = sel ? Color.white : new Color(0.75f, 0.75f, 0.75f);
                GUI.Label(new Rect(mx + 18f, rowY, mw * 0.55f, 34f), menuLabels[i], lSt);

                GUIStyle vSt = new GUIStyle(styleLabel);
                vSt.alignment = TextAnchor.MiddleRight;
                vSt.normal.textColor = sel ? Color.yellow : new Color(0.85f, 0.85f, 0.5f);
                GUI.Label(new Rect(mx + 18f, rowY, mw - 36f, 34f),
                          sel ? $"◀  {optTxt}  ▶" : optTxt, vSt);

                // Live axis bar (тільки для перших 5 пунктів — осі)
                if (i < 5)
                {
                    float liveVal = (NormAxis(curVals[i]) + 1f) * 0.5f;
                    GUI.color = new Color(0f, 0.6f, 0.3f, 0.5f);
                    GUI.DrawTexture(new Rect(mx + 6f, rowY + 33f, (mw - 12f) * liveVal, 4f),
                                    Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }

            GUI.Label(new Rect(mx, my + mh - 32f, mw, 26f),
                      "[↑↓] Navigate    [←→] Change    [F] Close", styleHint);
        }

        // ── Стилі ────────────────────────────────────────────────────
        void EnsureStyles()
        {
            if (stylesInit) return;
            stylesInit = true;

            styleLabel = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
            styleLabel.normal.textColor = Color.white;

            styleBig = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            styleBig.normal.textColor = Color.white;

            styleHint = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            styleHint.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        }
    }
}
