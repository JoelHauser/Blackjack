using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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

        private void Awake()
        {
            Log = Logger;

            new Harmony(PluginGuid).PatchAll(typeof(MenuButtonPatch));

            Log.LogInfo("[Blackjack] client loaded");
        }
    }
}
