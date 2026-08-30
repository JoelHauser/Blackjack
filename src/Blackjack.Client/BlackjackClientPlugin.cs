using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Blackjack.Client
{
    /// <summary>
    /// The in-game half of Blackjack.
    ///
    /// The server owns the game entirely -- it shuffles, deals, decides and moves the
    /// money. This side renders what it is handed and sends what the player asked for.
    /// It never sees the dealer's hole card during a hand, because the server does not
    /// send it.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BlackjackClientPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.joelhauser.blackjack.client";
        public const string PluginName = "Blackjack (client)";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log;

        /// <summary>
        /// The plugin itself, so code that is not a MonoBehaviour can still start a
        /// coroutine. The menu button needs one to wait a frame for other menu mods to
        /// finish before it copies what they left behind.
        /// </summary>
        internal static BlackjackClientPlugin Instance;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));

            Log.LogInfo("[Blackjack] client loaded");
        }

        /// <summary>
        /// Escape closes the table.
        ///
        /// Watched here rather than patched into EFT's own input handling: the table
        /// is our window, not one of the game's screens, so nothing in the game knows
        /// to close it. The key is only acted on while the table is open, so this
        /// cannot interfere with escape anywhere else.
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                BlackjackPanel.OnEscape();
            }
        }
    }
}
