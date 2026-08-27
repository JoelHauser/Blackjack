using System;
using System.Linq;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a BLACKJACK button on the main menu, beside the ones already there.
    ///
    /// The menu rather than the hideout, deliberately. The Rest Space was the obvious
    /// home -- EFT even has a game-disc system sitting in it -- but the part that can
    /// play a game needs Rest Space 2, a generator and burning fuel, which locks a new
    /// profile out of the mod entirely. A menu button works on a profile five minutes
    /// old.
    ///
    /// The button is cloned from one of the menu's own rather than built from nothing:
    /// it inherits the game's font, sizing, hover feedback and click sounds, none of
    /// which are worth reproducing by hand and all of which look wrong when they are
    /// slightly off.
    /// </summary>
    internal static class MenuButtonPatch
    {
        private const string ButtonName = "BlackjackButton";

        [HarmonyPatch(typeof(MenuScreen), nameof(MenuScreen.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void AfterAwake(MenuScreen __instance)
        {
            try
            {
                Install(__instance);
            }
            catch (Exception ex)
            {
                // A missing button is a disappointment. A menu that fails to build is
                // a game that does not start, so this never rethrows.
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not add the menu button: " + ex);
            }
        }

        private static void Install(MenuScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            // Awake can run again on a menu that is rebuilt between raids.
            var existing = screen.transform.Find(ButtonName);
            if (existing != null)
            {
                return;
            }

            var template = FindTemplate(screen);
            if (template == null)
            {
                BlackjackClientPlugin.Log.LogWarning(
                    "[Blackjack] no menu button to clone from; the menu's layout has changed.");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
            clone.name = ButtonName;

            // Sit directly under the button we copied, so the menu keeps its order
            // instead of gaining an entry in an arbitrary place.
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

            var button = clone.GetComponent<DefaultUIButton>();
            if (button != null)
            {
                button.SetHeaderText("BLACKJACK");
                button.Interactable = true;
            }

            // The clone carries the original's listeners, which would fire whatever
            // the copied button did. Clearing first is what stops BLACKJACK opening
            // the hideout.
            var unityButton = clone.GetComponentInChildren<Button>(true);
            if (unityButton == null)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] the cloned button has no Button component.");
                return;
            }

            unityButton.onClick.RemoveAllListeners();
            unityButton.onClick.AddListener(OnClicked);

            BlackjackClientPlugin.Log.LogInfo(
                $"[Blackjack] menu button added, cloned from '{template.name}'");
        }

        /// <summary>
        /// A button to copy. Preferring the hideout button because it is always present
        /// and never contextual -- the play button changes with matchmaking state and
        /// the disconnect button is not always shown.
        /// </summary>
        private static DefaultUIButton FindTemplate(MenuScreen screen)
        {
            var buttons = screen.GetComponentsInChildren<DefaultUIButton>(true);
            if (buttons == null || buttons.Length == 0)
            {
                return null;
            }

            return buttons.FirstOrDefault(b => b.name.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons.FirstOrDefault(b => b.name.IndexOf("trade", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons[0];
        }

        private static void OnClicked()
        {
            // Milestone one: prove the hook. The panel comes next.
            BlackjackClientPlugin.Log.LogInfo("[Blackjack] menu button clicked");
        }
    }
}
