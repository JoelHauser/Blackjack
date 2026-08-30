using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// The window the BLACKJACK button opens.
    ///
    /// Built from Unity primitives rather than cloned from an EFT screen. Cloning was
    /// right for the menu button, where matching the game's look exactly is the whole
    /// point and the thing being copied is one small widget. A whole screen carries
    /// controllers and bindings that expect a session and a real controller behind
    /// them, and fighting those is more work than laying out a panel.
    ///
    /// It does borrow EFT's font, though, because a menu in Arial next to a menu in
    /// Bender is the kind of detail that makes a mod look broken.
    ///
    /// This is milestone two: prove the panel opens and the round trip to the server
    /// works. It shows what /blackjack/ping reports and nothing more. The table
    /// itself comes next.
    /// </summary>
    internal static class BlackjackPanel
    {
        private const string RootName = "BlackjackPanel";

        private static GameObject _root;
        private static TextMeshProUGUI _body;

        internal static bool IsOpen => _root != null && _root.activeSelf;

        internal static void Toggle()
        {
            if (IsOpen)
            {
                Close();
                return;
            }

            Open();
        }

        internal static void Open()
        {
            try
            {
                if (_root == null)
                {
                    Build();
                }

                if (_root == null)
                {
                    return;
                }

                _root.SetActive(true);
                Refresh();
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogError("[Blackjack] could not open the panel: " + ex);
            }
        }

        internal static void Close()
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }
        }

        /// <summary>
        /// Asks the server what it knows and shows it. Deliberately synchronous: this
        /// is one small request on a menu click, and the alternative is a coroutine
        /// whose failure modes are harder to see than a brief hitch.
        /// </summary>
        private static void Refresh()
        {
            if (_body == null)
            {
                return;
            }

            _body.text = "contacting the server...";

            var ping = BlackjackApi.Ping();
            if (ping == null)
            {
                _body.text =
                    "<color=#d9534f>No answer from the server.</color>\n\n"
                    + "Is it running, and is the Blackjack server mod installed in\n"
                    + "SPT_Runtime\\user\\mods\\Blackjack ?";
                return;
            }

            var text = new StringBuilder();
            text.AppendLine($"server mod   v{ping["ModVersion"]}");
            text.AppendLine($"session      {ping["SessionId"]}");
            text.AppendLine();

            var balances = ping["Balances"];
            if (balances == null || !balances.HasValues)
            {
                text.AppendLine("<color=#d9534f>No profile for this session.</color>");
            }
            else
            {
                foreach (var entry in balances.Children<JProperty>())
                {
                    var value = entry.Value.ToObject<long>();
                    text.AppendLine($"{entry.Name,-12} {value,16:N0}");
                }
            }

            _body.text = text.ToString();
        }

        private static void Build()
        {
            var font = BorrowFont();

            // Its own canvas, well above the menu's, so nothing in the menu can draw
            // over it and we do not have to reason about EFT's sorting.
            var canvasObject = new GameObject(RootName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            _root = canvasObject;

            // A dimmed backdrop that also swallows clicks, so the menu behind cannot
            // be operated while this is open.
            var backdrop = NewImage("Backdrop", canvasObject.transform, new Color(0f, 0f, 0f, 0.75f));
            Stretch(backdrop.rectTransform);

            var panel = NewImage("Panel", canvasObject.transform, new Color(0.09f, 0.09f, 0.10f, 0.98f));
            var panelRect = panel.rectTransform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(760f, 520f);
            panelRect.anchoredPosition = Vector2.zero;

            var title = NewText("Title", panelRect, font, 34f, FontStyles.Bold);
            title.text = "BLACKJACK";
            title.alignment = TextAlignmentOptions.Center;
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.sizeDelta = new Vector2(-60f, 60f);
            titleRect.anchoredPosition = new Vector2(0f, -30f);

            _body = NewText("Body", panelRect, font, 22f, FontStyles.Normal);
            _body.alignment = TextAlignmentOptions.TopLeft;
            // Monospaced spacing is not available without a mono font asset, so the
            // balances are padded in code instead.
            var bodyRect = _body.rectTransform;
            bodyRect.anchorMin = new Vector2(0f, 0f);
            bodyRect.anchorMax = new Vector2(1f, 1f);
            bodyRect.offsetMin = new Vector2(40f, 90f);
            bodyRect.offsetMax = new Vector2(-40f, -100f);

            BuildCloseButton(panelRect, font);

            BlackjackClientPlugin.Log.LogInfo("[Blackjack] panel built");
        }

        private static void BuildCloseButton(RectTransform parent, TMP_FontAsset font)
        {
            var image = NewImage("Close", parent, new Color(0.18f, 0.18f, 0.20f, 1f));
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(220f, 52f);
            rect.anchoredPosition = new Vector2(0f, 24f);

            var label = NewText("Label", rect, font, 22f, FontStyles.Bold);
            label.text = "CLOSE";
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);

            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(Close);
        }

        /// <summary>
        /// EFT's own UI font, taken from any label already on screen. Falling back to
        /// TMP's default keeps the panel readable rather than blank if the menu's
        /// labels cannot be found.
        /// </summary>
        private static TMP_FontAsset BorrowFont()
        {
            try
            {
                var label = UnityEngine.Object.FindObjectsOfType<TextMeshProUGUI>()
                    .FirstOrDefault(t => t != null && t.font != null);

                if (label != null)
                {
                    return label.font;
                }
            }
            catch (Exception ex)
            {
                BlackjackClientPlugin.Log.LogWarning("[Blackjack] could not borrow a font: " + ex.Message);
            }

            return TMP_Settings.defaultFontAsset;
        }

        private static Image NewImage(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            return image;
        }

        private static TextMeshProUGUI NewText(string name, Transform parent, TMP_FontAsset font, float size, FontStyles style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = size;
            text.fontStyle = style;
            text.color = new Color(0.88f, 0.87f, 0.83f, 1f);
            text.richText = true;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
