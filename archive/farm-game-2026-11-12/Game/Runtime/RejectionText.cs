using System.Collections.Generic;

namespace MarsNeedsWater.Game
{
    /// <summary>
    /// Player-facing text for rejection codes, looked up by (request type, code) with a per-code fallback
    /// (FPC Rule 4 binding table). Placeholder copy.
    /// </summary>
    public static class RejectionText
    {
        static readonly Dictionary<string, string> _byCode = new Dictionary<string, string>
        {
            { "too_far", "Too far: step closer" },
            { "blocked", "Can't pick that up" },
            { "hands_full", "Hands full: drop it first (G)" },
            { "not_empty", "Empty it first" },
            { "switch_off_first", "Switch it off first" },
            { "not_holding", "Pick up the hose end first" },
            { "not_owned", "You don't own that yet" },
            { "crate_unopened", "Not unboxed yet: pry the crate" },
            { "not_in_hand", "Take it in hand first" },
            { "tool_full", "Full: go pour it" },
            { "source_dry", "Dry. Nothing left here" },
            { "tool_empty", "Nothing to pour" },
            { "cannot_deposit", "Company water only flows one way" },
            { "not_installed", "Swap it in first" },
            { "pool_full", "Tank full: LAUNCH it" },
            { "wrong_socket", "Wrong fitting" },
            { "no_inlet", "No inlet: screw in the fitting" },
            { "socket_in_use", "Socket in use" },
            { "hose_too_short", "Hose won't reach" },
            { "fixed_end", "That end is fixed to the pump" },
            { "already_fitted", "Already fitted" },
            { "not_on_staging_spot", "Set it on the staging spot first" },
            { "not_an_upgrade", "Only a bigger, empty container" },
            { "not_placed", "Set the pump down first" },
            { "no_handle", "No handle: it's electric now" },
            { "not_holding_kit", "Hold the motor kit" },
            { "no_pump", "Needs a hand pump" },
            { "already_powered", "Already electric" },
            { "no_motor", "No motor to power" },
            { "not_plugged", "Not plugged in" },
            { "already_owned", "Already bought" },
            { "locked", "Requires the previous item" },
            { "cannot_afford", "Can't afford it yet" },
            { "already_open", "Already open" },
            { "pad_busy", "Next rocket is not here yet" },
            { "nothing_to_ship", "No cargo: fill the tank" },
        };

        static readonly Dictionary<string, string> _byPair = new Dictionary<string, string>
        {
            { "Drop|blocked", "No room to drop it here" },
            { "PlaceItem|blocked", "Can't put it there" },
            { "PlugCable|not_holding", "Pick up the cable end first" },
            { "PlugCable|too_far", "Too far, or the cable won't reach" },
            { "FitInlet|not_holding", "Hold the inlet fitting" },
            { "BoltMotor|already_fitted", "Motor already fitted" },
            { "SetSwitch|no_motor", "No motor to switch" },
        };

        /// <summary>Text for a rejection of <paramref name="request"/> with <paramref name="code"/>.</summary>
        public static string For(string request, string code)
        {
            if (_byPair.TryGetValue(request + "|" + code, out var t)) return t;
            if (_byCode.TryGetValue(code ?? "", out t)) return t;
            return "Can't do that";
        }
    }
}
