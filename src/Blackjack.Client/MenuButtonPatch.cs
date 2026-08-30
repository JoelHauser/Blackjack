using System;
using System.Linq;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace Blackjack.Client
{
    /// <summary>
    /// Puts a BLACKJACK button on the main menu, beside the ones already there.
    ///
    /// The menu rather than the hideout, deliberately. The Rest Space was the obvious
    /// home, and EFT even has a game-disc system sitting in it, but the part that can
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
        private static void AfterAwake(MenuScreen __instance) => Install(__instance, "Awake");

        /// <summary>
        /// Also on Show, because other menu mods rearrange these buttons after Awake.
        /// MoxoPixel's Menu Overhaul, for one, positions Play, Character, Trade,
        /// Hideout and Exit individually from its own config -- a fixed list of five
        /// that cannot know about a sixth. Re-applying later lets our button follow
        /// whatever the template ended up looking like instead of sitting where the
        /// menu used to be.
        /// </summary>
        [HarmonyPatch(typeof(MenuScreen), nameof(MenuScreen.Show), typeof(MenuScreen.MainMenuBaseScreenController))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void AfterShow(MenuScreen __instance) => Install(__instance, "Show");

        private static void Install(MenuScreen screen, string source)
        {
            try
            {
                if (screen == null)
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

                var existing = FindOurs(screen);
                if (existing != null)
                {
                    // Already there. Re-follow the template, which another mod may have
                    // moved since we were created.
                    Follow(existing, template);
                    return;
                }

                var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
                clone.name = ButtonName;
                clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);

                var button = clone.GetComponent<DefaultUIButton>();
                if (button == null)
                {
                    BlackjackClientPlugin.Log.LogWarning("[Blackjack] the clone has no DefaultUIButton.");
                    UnityEngine.Object.Destroy(clone);
                    return;
                }

                button.SetHeaderText("BLACKJACK");
                button.Interactable = true;
                ClearIcon(button);
                Wire(button);
                Follow(button, template);

                BlackjackClientPlugin.Log.LogInfo(
                    $"[Blackjack] menu button added from {source}, cloned from '{template.name}'");
            }
            catch (Exception ex)
            {
                // A missing button is a disappointment. A menu that fails to build is a
                // game that does not start, so this never rethrows.
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not add the menu button: " + ex);
            }
        }

        /// <summary>
        /// Attaches the click handler.
        ///
        /// Not a UnityEngine.UI.Button: EFT's DefaultUIButton descends from
        /// ButtonFeedback, which implements IPointerClickHandler itself and exposes a
        /// plain UnityEvent field called OnClick. Looking for a Button component finds
        /// nothing, which is exactly what the first attempt did -- the button appeared,
        /// looked right, and did nothing at all when clicked.
        ///
        /// Clearing first matters as much as adding: a clone carries the original's
        /// listeners, so without this BLACKJACK would open the hideout.
        /// </summary>
        private static void Wire(DefaultUIButton button)
        {
            var field = AccessTools.Field(typeof(DefaultUIButton), "OnClick");
            if (field?.GetValue(button) is not UnityEvent onClick)
            {
                BlackjackClientPlugin.Log.LogWarning(
                    "[Blackjack] DefaultUIButton has no OnClick event; the button will do nothing.");
                return;
            }

            onClick.RemoveAllListeners();
            onClick.AddListener(OnClicked);
        }

        /// <summary>
        /// Drops the borrowed icon. Without this the button wears whichever icon the
        /// template had, so BLACKJACK shows the hideout's.
        /// </summary>
        private static void ClearIcon(DefaultUIButton button)
        {
            try
            {
                button.SetIcon(null, null);
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] could not clear the button icon: " + ex.Message);
            }
        }

        /// <summary>
        /// Sits our button under the template, matching whatever position and size the
        /// template currently has. Called again on every Show so a menu mod that moves
        /// the originals takes ours with it.
        /// </summary>
        private static void Follow(DefaultUIButton ours, DefaultUIButton template)
        {
            var mine = ours.GetComponent<RectTransform>();
            var theirs = template.GetComponent<RectTransform>();
            if (mine == null || theirs == null)
            {
                return;
            }

            mine.anchorMin = theirs.anchorMin;
            mine.anchorMax = theirs.anchorMax;
            mine.pivot = theirs.pivot;
            mine.sizeDelta = theirs.sizeDelta;
            mine.localScale = theirs.localScale;

            // One row below the template, using the gap between two real buttons rather
            // than a number picked by eye, so it still lines up if a mod restyles them.
            var step = RowHeight(template);
            mine.anchoredPosition = theirs.anchoredPosition + new Vector2(0f, -step);

            ours.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
        }

        /// <summary>
        /// The vertical distance between two adjacent menu buttons, measured rather
        /// than assumed. Falls back to the template's own height.
        /// </summary>
        private static float RowHeight(DefaultUIButton template)
        {
            var parent = template.transform.parent;
            var rect = template.GetComponent<RectTransform>();
            var fallback = rect != null ? Mathf.Abs(rect.sizeDelta.y) : 40f;

            if (parent == null)
            {
                return fallback > 1f ? fallback : 40f;
            }

            var rows = parent.GetComponentsInChildren<DefaultUIButton>(true)
                .Where(b => b != null && b.name != ButtonName)
                .Select(b => b.GetComponent<RectTransform>())
                .Where(r => r != null)
                .Select(r => r.anchoredPosition.y)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            for (var i = 1; i < rows.Count; i++)
            {
                var gap = Mathf.Abs(rows[i - 1] - rows[i]);
                if (gap > 1f)
                {
                    return gap;
                }
            }

            return fallback > 1f ? fallback : 40f;
        }

        private static DefaultUIButton FindOurs(MenuScreen screen) =>
            screen.GetComponentsInChildren<DefaultUIButton>(true)
                .FirstOrDefault(b => b != null && b.name == ButtonName);

        /// <summary>
        /// A button to copy. Preferring the hideout button because it is always present
        /// and never contextual -- the play button changes with matchmaking state and
        /// the disconnect button is not always shown.
        /// </summary>
        private static DefaultUIButton FindTemplate(MenuScreen screen)
        {
            var buttons = screen.GetComponentsInChildren<DefaultUIButton>(true)
                .Where(b => b != null && b.name != ButtonName)
                .ToList();

            if (buttons.Count == 0)
            {
                return null;
            }

            return buttons.FirstOrDefault(b => b.name.IndexOf("hideout", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons.FirstOrDefault(b => b.name.IndexOf("trade", StringComparison.OrdinalIgnoreCase) >= 0)
                   ?? buttons[0];
        }

        private static void OnClicked()
        {
            BlackjackClientPlugin.Log.LogInfo("[Blackjack] menu button clicked");
            BlackjackPanel.Toggle();
        }
    }
}
