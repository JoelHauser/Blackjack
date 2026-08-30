using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Draws one playing card.
    ///
    /// The server sends cards as two characters, rank then suit: "TD" is the ten of
    /// diamonds, "AS" the ace of spades. There is no card art to load, so a card is
    /// drawn: a rounded ivory face with a thin edge, the rank in opposite corners the
    /// way a real card carries it, and a large suit through the middle.
    ///
    /// The rank appearing twice, the second upside down, is most of what makes a
    /// rectangle read as a playing card.
    /// </summary>
    internal static class CardView
    {
        internal const float Width = 96f;
        internal const float Height = 138f;

        private static readonly Color Face = new Color(0.95f, 0.94f, 0.90f, 1f);
        private static readonly Color Edge = new Color(0.72f, 0.70f, 0.65f, 1f);
        private static readonly Color Red = new Color(0.70f, 0.11f, 0.12f, 1f);
        private static readonly Color Black = new Color(0.10f, 0.10f, 0.11f, 1f);

        // The back of a card, for the dealer's hole card while the hand is live.
        private static readonly Color BackFace = new Color(0.42f, 0.10f, 0.12f, 1f);
        private static readonly Color BackEdge = new Color(0.90f, 0.88f, 0.84f, 1f);
        private static readonly Color BackPattern = new Color(0.30f, 0.07f, 0.09f, 1f);

        internal static GameObject Build(Transform parent, string code, TMP_FontAsset font)
        {
            var faceDown = string.IsNullOrEmpty(code) || code.Length < 2;

            var go = new GameObject(faceDown ? "Card_back" : "Card_" + code,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(Width, Height);

            var image = go.GetComponent<Image>();
            image.type = Image.Type.Sliced;

            // A drop shadow, so cards sit on the cloth rather than being printed on it.
            Shadow(rect);

            if (faceDown)
            {
                image.sprite = Textures.RoundedBox(10, BackFace, BackEdge, 3);

                var inner = new GameObject("Pattern", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                inner.transform.SetParent(rect, false);
                var innerRect = (RectTransform)inner.transform;
                innerRect.anchorMin = Vector2.zero;
                innerRect.anchorMax = Vector2.one;
                innerRect.offsetMin = new Vector2(10f, 10f);
                innerRect.offsetMax = new Vector2(-10f, -10f);

                var innerImage = inner.GetComponent<Image>();
                innerImage.type = Image.Type.Sliced;
                innerImage.sprite = Textures.RoundedBox(8, BackPattern, BackPattern);
                return go;
            }

            image.sprite = Textures.RoundedBox(10, Face, Edge, 2);

            var rank = RankOf(code);
            var suit = SuitOf(code, font);
            var colour = IsRed(code) ? Red : Black;

            Corner(rect, rank, suit, font, colour, false);
            Corner(rect, rank, suit, font, colour, true);

            var pip = Text(rect, suit, font, 54f, colour, TextAlignmentOptions.Center);
            pip.rectTransform.anchorMin = Vector2.zero;
            pip.rectTransform.anchorMax = Vector2.one;
            pip.rectTransform.offsetMin = new Vector2(22f, 18f);
            pip.rectTransform.offsetMax = new Vector2(-22f, -18f);

            return go;
        }

        /// <summary>
        /// Rank over suit in a corner. The second copy is rotated a half turn, which is
        /// why this takes a flag rather than being written twice.
        /// </summary>
        private static void Corner(RectTransform card, string rank, string suit, TMP_FontAsset font, Color colour, bool flipped)
        {
            var holder = new GameObject(flipped ? "CornerFlipped" : "Corner", typeof(RectTransform));
            holder.transform.SetParent(card, false);

            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(30f, 46f);
            rect.anchorMin = rect.anchorMax = flipped ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            rect.pivot = flipped ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            rect.anchoredPosition = flipped ? new Vector2(-7f, 7f) : new Vector2(7f, -7f);
            rect.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);

            var rankLabel = Text(rect, rank, font, 24f, colour, TextAlignmentOptions.Center);
            rankLabel.rectTransform.anchorMin = new Vector2(0f, 0.45f);
            rankLabel.rectTransform.anchorMax = Vector2.one;
            rankLabel.rectTransform.offsetMin = Vector2.zero;
            rankLabel.rectTransform.offsetMax = Vector2.zero;

            var suitLabel = Text(rect, suit, font, 18f, colour, TextAlignmentOptions.Center);
            suitLabel.rectTransform.anchorMin = Vector2.zero;
            suitLabel.rectTransform.anchorMax = new Vector2(1f, 0.45f);
            suitLabel.rectTransform.offsetMin = Vector2.zero;
            suitLabel.rectTransform.offsetMax = Vector2.zero;
        }

        private static void Shadow(RectTransform card)
        {
            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(3f, -4f);
        }

        private static TextMeshProUGUI Text(Transform parent, string value, TMP_FontAsset font, float size, Color colour, TextAlignmentOptions align)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = value;
            label.fontSize = size;
            label.color = colour;
            label.alignment = align;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static string RankOf(string code)
        {
            var rank = code[0];
            return rank == 'T' ? "10" : rank.ToString();
        }

        /// <summary>
        /// A suit symbol where the font has one, a letter where it does not.
        ///
        /// EFT's UI font is not a full unicode face and there is no guarantee it
        /// carries the card suits. A missing glyph renders as an empty box, which is
        /// worse than the letter it replaced, so this asks first.
        /// </summary>
        private static string SuitOf(string code, TMP_FontAsset font)
        {
            var suit = char.ToUpperInvariant(code[code.Length - 1]);

            var symbol = suit switch
            {
                'S' => '♠',
                'H' => '♥',
                'D' => '♦',
                'C' => '♣',
                _ => '?',
            };

            if (symbol != '?' && font != null && font.HasCharacter(symbol))
            {
                return symbol.ToString();
            }

            return suit.ToString();
        }

        private static bool IsRed(string code)
        {
            var suit = char.ToUpperInvariant(code[code.Length - 1]);
            return suit == 'H' || suit == 'D';
        }
    }
}
