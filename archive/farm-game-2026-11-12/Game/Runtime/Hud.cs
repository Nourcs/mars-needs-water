using System.Collections.Generic;
using MarsNeedsWater.Simulation;
using Pose = MarsNeedsWater.Simulation.Pose;
using UnityEngine;
using Sim = MarsNeedsWater.Simulation.Simulation;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Placeholder HUD (IMGUI; UX spec and art bible deferred): credits and job title, held water, pad tank,
    /// next item and shortfall, rocket state, crosshair prompt, hold ring, rejection text, the spoonful counter,
    /// and corporate-radio placeholder lines. Presentation only.
    /// </summary>
    public sealed class Hud : MonoBehaviour, ISimEventListener
    {
        Sim _sim;
        PlayerController _player;
        GameRoot _root;

        // Spoonful counter (Hand Tools Rule 12): presentation only, restarts on load.
        int _spoonfuls;
        double _lastSpoon;
        string _quip = "";
        float _quipUntil;

        // Radio placeholder (Corporate Radio GDD is MVP; this is a stand-in until that system is built).
        readonly Queue<string> _radio = new Queue<string>();
        string _radioLine = "";
        float _radioUntil;

        GUIStyle _label, _big, _center, _small, _box, _panel, _dim;
        Texture2D _panelTex;

        public void Init(Sim sim, PlayerController player, GameRoot root)
        {
            _sim = sim;
            _player = player;
            _root = root;
            _sim.Bus.Subscribe(this);
            Say("RADIO: Good morning, contractor! Crystal Lake Holdings has issued you one (1) spoon. Make it count.");
        }

        void Say(string line)
        {
            if (_radio.Count < 3) _radio.Enqueue(line);
        }

        public void OnSimEvent(in SimEvent e, SimState state)
        {
            switch (e.Type)
            {
                case SimEventType.LaunchCompleted:
                    if (state.Shipping.Launches == 1) Say($"RADIO: First shipment! {e.Value:0.###} L are on their way to Mars. Shareholders are weeping with joy.");
                    else if (state.Shipping.Launches % 10 == 0) Say($"RADIO: Shipment number {state.Shipping.Launches}. Earth barely noticed.");
                    break;
                case SimEventType.Purchased:
                {
                    string name = _sim.Tuning.TryItem(e.Text, out var row) ? row.displayName : e.Text;
                    Say($"RADIO: Congratulations on your {name}. Your new title: {_sim.Economy.JobTitle()}.");
                    break;
                }
                case SimEventType.SourceDrained:
                    if (e.Id == GreyboxLevel.PuddleId) Say("PAYSLIP: Puddle liquidated. Outstanding!");
                    else Say("RADIO: The pond is... done? Wonderful. Please stand by for relocation.");
                    break;
                case SimEventType.DrawdownThreshold:
                    if (e.Id == GreyboxLevel.PondId) Say($"COMMENDATION: Farm pond {e.Value:0.#}% extracted. The ducks have been notified.");
                    break;
                case SimEventType.Reveal:
                    if (e.Id == GreyboxLevel.DockRevealId) Say("RADIO: Something is surfacing. A sign? Disregard the sign.");
                    if (e.Id == GreyboxLevel.CartRevealId) Say("RADIO: A shopping cart. Please do not return it.");
                    break;
                case SimEventType.PumpStateChanged:
                    if (e.Text2 == "Running" && e.Text == "Ready" && !_firstPumpSaid) { _firstPumpSaid = true; Say("RADIO: Mechanised extraction detected. Productivity is up 10,000%."); }
                    break;
            }
        }

        bool _firstPumpSaid;

        void Update()
        {
            if (_sim == null) return;
            // Count accepted spoon scoops (held amount rose).
            double spoon = _sim.HandTools.Held(0, "spoon");
            if (spoon > _lastSpoon + 1e-9)
            {
                _spoonfuls++;
                if (_spoonfuls == 10) Quip("Spoonful #10. The shareholders are moved.");
                if (_spoonfuls == 25) Quip("Spoonful #25. Hydration targets: in sight.");
                if (_spoonfuls == 40) Quip("Spoonful #40. That is a bucket. Go launch it.");
            }
            _lastSpoon = spoon;
            if (Time.unscaledTime > _radioUntil && _radio.Count > 0)
            {
                _radioLine = _radio.Dequeue();
                _radioUntil = Time.unscaledTime + 6f;
            }
            else if (Time.unscaledTime > _radioUntil) _radioLine = "";
        }

        void Quip(string q)
        {
            _quip = q;
            _quipUntil = Time.unscaledTime + 3f;
        }

        void Styles()
        {
            if (_label != null) return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            _label.normal.textColor = Color.white;
            _big = new GUIStyle(_label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _big.normal.textColor = new Color(1f, 0.86f, 0.4f);
            _center = new GUIStyle(_label) { alignment = TextAnchor.MiddleCenter, fontSize = 16 };
            _small = new GUIStyle(_label) { fontSize = 12 };
            _dim = new GUIStyle(_small);
            _dim.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
            _box = new GUIStyle(GUI.skin.box);
            // A soft dark glass panel instead of the default grey box.
            _panelTex = new Texture2D(1, 1);
            _panelTex.SetPixel(0, 0, new Color(0.04f, 0.07f, 0.12f, 0.5f));
            _panelTex.Apply();
            _panel = new GUIStyle();
            _panel.normal.background = _panelTex;
        }

        void OnGUI()
        {
            if (_sim == null) return;
            Styles();
            if (_sim.Run == RunState.Paused) { PauseMenu(); return; }
            if (_sim.Run != RunState.Running) return;

            var eco = _sim.Economy;
            var pad = _sim.Network.PadPool();
            var hands = _sim.Placement.Hands(0);
            GUI.Box(new Rect(14, 14, 330, 126), GUIContent.none, _panel);
            Text(new Rect(24, 18, 310, 28), $"{eco.Credits:N0} cr", _big);
            Text(new Rect(24, 42, 310, 20), eco.JobTitle(), _dim);
            if (pad != null) Text(new Rect(24, 60, 310, 20), $"Pad tank: {pad.Contents:0.###} / {pad.Capacity:0} L", _label);
            if (hands.Kind == HandsKind.Tool)
            {
                double held = _sim.HandTools.Held(0, hands.Tool);
                double cap = _sim.HandTools.Capacity(hands.Tool);
                string shown = held <= HandToolsSystem.EmptyEpsilon ? "0.0" : held < 1 ? $"{held * 1000:0} mL" : $"{held:0.0}";
                string tool = _sim.Tuning.TryItem(hands.Tool, out var row) ? row.displayName : hands.Tool;
                string extra = hands.Tool == "spoon" ? $"   Spoonful #{_spoonfuls}" : "";
                Text(new Rect(24, 78, 310, 20), $"{tool}: {shown} / {(cap < 1 ? $"{cap * 1000:0} mL" : $"{cap:0} L")}{extra}", _label);
            }
            else Text(new Rect(24, 78, 310, 20), $"Carrying: {Describe(hands)}   (G to drop)", _label);
            var next = eco.NextItem();
            if (next != null)
            {
                long shortCr = next.price_cr - eco.Credits;
                double padPays = pad == null ? 0 : eco.PayoutFor(System.Math.Min(pad.Contents, _sim.Tuning.Data.cargo_capacity_L)) / 1000.0;
                string line = shortCr <= 0 ? "Affordable now: pull ORDER" :
                    padPays >= shortCr ? "Enough aboard: LAUNCH to afford" :
                    $"Ship {System.Math.Ceiling((shortCr - padPays) / _sim.Tuning.Data.credits_per_litre * 1000) / 1000:0.###} L more";
                Text(new Rect(24, 98, 310, 18), $"Next: {next.displayName} ({next.price_cr:N0} cr) — {line}", _small);
            }
            var sh = _sim.State.Shipping;
            string rocket = sh.State == PadState.Ready ? "Rocket on pad" : sh.State == PadState.Countdown ? $"Countdown {sh.TimerTicks * _sim.Dt:0.0} s" : $"Next rocket in {_sim.Shipping.SecondsUntilReady():0} s";
            Text(new Rect(24, 116, 310, 18), $"{rocket}   ·   Pond {_sim.WaterSource.DrawdownPct(GreyboxLevel.PondId):0.00}% drained", _small);
            // The sea or lake you are looking at (only while the prompt is about it).
            int ww = _player.LastWorldWater;
            string wname = ww != 0 ? WorldWaters.Name(ww) : "";
            if (ww != 0 && _sim.WaterSource.Exists(ww) && wname.Length > 0 && _player.Prompt != null && _player.Prompt.Contains(wname))
            {
                double taken = _sim.WaterSource.Extracted(ww), start = _sim.WaterSource.Start(ww);
                float cx0 = Screen.width / 2f;
                Text(new Rect(cx0 - 300, Screen.height / 2f + 68, 600, 20), $"{wname}: {taken:N1} L taken of {WorldWaters.Litres(start)}", _centerSmall);
            }

            // Earth counter (derived, exact decimal), top right in a small panel.
            GUI.Box(new Rect(Screen.width - 384, 14, 370, 46), GUIContent.none, _panel);
            Text(new Rect(Screen.width - 374, 18, 360, 20), $"EARTH WATER: {eco.EarthRemainingL():N0} L", _label);
            Text(new Rect(Screen.width - 374, 38, 360, 18), $"Play {Clock(_sim.State.Session.SessionTimeS)}   ·   Esc pause   ·   M map", _dim);

            // Crosshair, prompt, hold ring, rejection.
            float cx = Screen.width / 2f, cy = Screen.height / 2f;
            GUI.Label(new Rect(cx - 6, cy - 14, 20, 24), "+", _label);
            string prompt = !string.IsNullOrEmpty(_player.Rejection) ? _player.Rejection : _player.Prompt;
            if (!string.IsNullOrEmpty(prompt))
            {
                var old = GUI.color;
                if (string.IsNullOrEmpty(_player.Rejection) && _player.PromptGrey) GUI.color = new Color(1, 1, 1, 0.55f);
                if (!string.IsNullOrEmpty(_player.Rejection)) GUI.color = new Color(1f, 0.6f, 0.5f);
                Text(new Rect(cx - 400, cy + 18, 800, 28), prompt, _center);
                GUI.color = old;
            }
            if (_player.HoldProgress >= 0) GUI.Label(new Rect(cx - 100, cy + 44, 200, 24), $"[{new string('#', Mathf.RoundToInt(_player.HoldProgress * 10)).PadRight(10, '.')}]", _center);
            if (Time.unscaledTime < _quipUntil) Text(new Rect(cx - 400, cy - 60, 800, 28), _quip, _center);
            if (!string.IsNullOrEmpty(_radioLine))
                Text(new Rect(cx - 500, Screen.height - 70, 1000, 30), _radioLine, _center);
        }

        GUIStyle _centerSmall => _cs ?? (_cs = new GUIStyle(_small) { alignment = TextAnchor.MiddleCenter });
        GUIStyle _cs;

        /// <summary>A label with a soft drop shadow so it reads over bright sky and sand.</summary>
        static void Text(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
        }

        void OnDestroy() { if (_panelTex != null) Destroy(_panelTex); }

        string Describe(HandsItem h)
        {
            switch (h.Kind)
            {
                case HandsKind.Entity:
                {
                    var e = _sim.Placement.Get(h.Id);
                    if (e == null) return "?";
                    return _sim.Tuning.TryItem(e.Item, out var row) ? row.displayName : e.Item;
                }
                case HandsKind.HoseEnd:
                {
                    var hose = _sim.Network.Hose(h.Id);
                    return hose == null ? "hose" : $"{hose.Kind.ToString().ToLower()} hose end";
                }
                case HandsKind.CableEnd: return "power cable end";
                default: return "nothing";
            }
        }

        static string Clock(double s) => $"{(int)(s / 60)}:{(int)(s % 60):00}";

        void PauseMenu()
        {
            float w = 320, h = 200, x = (Screen.width - w) / 2f, y = (Screen.height - h) / 2f;
            GUI.Box(new Rect(x, y, w, h), "PAUSED", _box);
            if (GUI.Button(new Rect(x + 40, y + 40, w - 80, 36), "Resume")) _root.Resume();
            if (GUI.Button(new Rect(x + 40, y + 86, w - 80, 36), "Restart (new game)")) _root.Restart();
            if (GUI.Button(new Rect(x + 40, y + 132, w - 80, 36), "Quit")) _root.Quit();
        }
    }
}
