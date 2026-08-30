using System;
using System.Collections;
using System.Linq;
using EFT.UI;
using HarmonyLib;
using TMPro;
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
    /// The button is a clone of one of the menu's own, and that is what makes it fit
    /// alongside other menu mods rather than in spite of them. See <see cref="Install"/>.
    /// </summary>
    internal static class MenuButtonPatch
    {
        private const string ButtonName = "BlackjackButton";

        [HarmonyPatch(typeof(MenuScreen), nameof(MenuScreen.Awake))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void AfterAwake(MenuScreen __instance) => Schedule(__instance);

        [HarmonyPatch(typeof(MenuScreen), nameof(MenuScreen.Show), typeof(MenuScreen.MainMenuBaseScreenController))]
        [HarmonyPostfix]
        // ReSharper disable once InconsistentNaming
        private static void AfterShow(MenuScreen __instance) => Schedule(__instance);

        /// <summary>
        /// Rebuilds at the end of the frame rather than immediately.
        ///
        /// This is the whole integration story with menu mods. MoxoPixel's Menu
        /// Overhaul restyles the main menu from a hardcoded list of five buttons --
        /// PlayButton, CharacterButton, TradeButton, HideoutButton, ExitButtonGroup --
        /// hiding each one's background, activating its icon and nudging it sideways by
        /// a per-button offset from its own config. A sixth button cannot be in that
        /// list, and asking to be added to it is not something this mod can do.
        ///
        /// It does not have to be. Waiting until every other Awake and Show handler has
        /// run means the button we copy has already been restyled, so the copy inherits
        /// the styling exactly -- background hidden, icon state, label size, whatever
        /// the other mod decided. Cloning early got a vanilla-looking button sitting
        /// next to five restyled ones, which is what looked wrong.
        ///
        /// It also means this needs no knowledge of that mod at all: anything that
        /// restyles the hideout button, now or later, is inherited for free.
        /// </summary>
        private static void Schedule(MenuScreen screen)
        {
            if (screen == null || BlackjackClientPlugin.Instance == null)
            {
                return;
            }

            BlackjackClientPlugin.Instance.StartCoroutine(InstallAtEndOfFrame(screen));
        }

        private static IEnumerator InstallAtEndOfFrame(MenuScreen screen)
        {
            yield return new WaitForEndOfFrame();

            try
            {
                Install(screen);
            }
            catch (Exception ex)
            {
                // A missing button is a disappointment. A menu that fails to build is a
                // game that does not start, so this never rethrows.
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not add the menu button: " + ex);
            }
        }

        private static void Install(MenuScreen screen)
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

            // Thrown away and cloned again rather than adjusted in place. Whatever
            // another mod did to the template between then and now is inherited by
            // copying it afresh, and there is no state of ours to get out of step.
            var existing = FindOurs(screen);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
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

            Relabel(button, "BLACKJACK");
            button.Interactable = true;
            Wire(button);
            Follow(button, template);

            BlackjackClientPlugin.Log.LogInfo($"[Blackjack] menu button added, cloned from '{template.name}'");
        }

        /// <summary>
        /// Renames the button without undoing anyone's styling.
        ///
        /// SetHeaderText re-applies the button's own font size, which throws away a
        /// size another mod set on the label. Putting it back afterwards keeps our
        /// button the same size as its neighbours.
        /// </summary>
        private static void Relabel(DefaultUIButton button, string text)
        {
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            var size = label != null ? label.fontSize : 0f;

            button.SetHeaderText(text);

            if (label != null && size > 0f)
            {
                label.fontSize = size;
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
        /// Sits our button one row under the template, matching whatever position and
        /// size it currently has -- including any sideways offset a menu mod applied,
        /// since that is baked into the template by the time this runs.
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

            // One row below, using the gap between two real buttons rather than a
            // number picked by eye, so it still lines up if a mod restyles them.
            mine.anchoredPosition = theirs.anchoredPosition + new Vector2(0f, -RowHeight(template));
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

            if (parent != null)
            {
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
            }

            return fallback > 1f ? fallback : 40f;
        }

        private static DefaultUIButton FindOurs(MenuScreen screen) =>
            screen.GetComponentsInChildren<DefaultUIButton>(true)
                .FirstOrDefault(b => b != null && b.name == ButtonName);

        /// <summary>
        /// A button to copy. The hideout button, because it is always present and never
        /// contextual -- the play button changes with matchmaking state and the exit
        /// button is a group rather than a plain button in at least one menu mod.
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
