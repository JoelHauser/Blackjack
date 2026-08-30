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
    /// drawn: a rounded ivory face, the rank in opposite corners the way a real card
    /// carries it, and the suit through the middle.
    ///
    /// Suits are shapes, not letters. EFT's UI font has no card suits in it, so
    /// spelling them put a giant C in the middle of every club.
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

            var go = new GameObject(
                faceDown ? "Card_back" : "Card_" + code,
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(Width, Height);

            var image = go.GetComponent<Image>();
            image.type = Image.Type.Sliced;

            // A drop shadow, so cards sit on the cloth rather than being printed on it.
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            shadow.effectDistance = new Vector2(3f, -4f);

            if (faceDown)
            {
                image.sprite = Textures.RoundedBox(10, BackFace, BackEdge, 3);

                var inner = NewImage("Pattern", rect, Color.white);
                inner.sprite = Textures.RoundedBox(8, BackPattern, BackPattern);
                inner.type = Image.Type.Sliced;

                var innerRect = (RectTransform)inner.transform;
                innerRect.anchorMin = Vector2.zero;
                innerRect.anchorMax = Vector2.one;
                innerRect.offsetMin = new Vector2(10f, 10f);
                innerRect.offsetMax = new Vector2(-10f, -10f);
                return go;
            }

            image.sprite = Textures.RoundedBox(10, Face, Edge, 2);

            var rank = RankOf(code);
            var suit = char.ToUpperInvariant(code[code.Length - 1]);
            var colour = IsRed(suit) ? Red : Black;

            Corner(rect, rank, suit, font, colour, false);
            Corner(rect, rank, suit, font, colour, true);

            // The pip through the middle.
            var pip = NewImage("Pip", rect, Color.white);
            pip.sprite = Textures.Suit(suit, colour);
            pip.preserveAspect = true;
            var pipRect = (RectTransform)pip.transform;
            pipRect.anchorMin = pipRect.anchorMax = new Vector2(0.5f, 0.5f);
            pipRect.pivot = new Vector2(0.5f, 0.5f);
            pipRect.sizeDelta = new Vector2(52f, 52f);
            pipRect.anchoredPosition = Vector2.zero;

            return go;
        }

        /// <summary>
        /// Rank over suit in a corner, the second copy rotated a half turn.
        ///
        /// The pivot is the middle of the corner block, not the corner of the card.
        /// Rotating about a pivot sitting on the card's edge swings the whole block
        /// outside it, which is what put a stray red D on the cloth below the dealer's
        /// hand.
        /// </summary>
        private static void Corner(RectTransform card, string rank, char suit, TMP_FontAsset font, Color colour, bool flipped)
        {
            var holder = new GameObject(flipped ? "CornerFlipped" : "Corner", typeof(RectTransform));
            holder.transform.SetParent(card, false);

            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(28f, 46f);
            rect.anchorMin = rect.anchorMax = flipped ? new Vector2(1f, 0f) : new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = flipped ? new Vector2(-22f, 30f) : new Vector2(22f, -30f);
            rect.localRotation = Quaternion.Euler(0f, 0f, flipped ? 180f : 0f);

            var label = Text(rect, rank, font, 23f, colour);
            label.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            var pip = NewImage("Pip", rect, Color.white);
            pip.sprite = Textures.Suit(suit, colour);
            pip.preserveAspect = true;
            var pipRect = (RectTransform)pip.transform;
            pipRect.anchorMin = new Vector2(0.5f, 0f);
            pipRect.anchorMax = new Vector2(0.5f, 0.42f);
            pipRect.pivot = new Vector2(0.5f, 0.5f);
            pipRect.sizeDelta = new Vector2(16f, 16f);
            pipRect.anchoredPosition = new Vector2(0f, 9f);
        }

        private static Image NewImage(string name, Transform parent, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Text(Transform parent, string value, TMP_FontAsset font, float size, Color colour)
        {
            var go = new GameObject("Rank", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = value;
            label.fontSize = size;
            label.color = colour;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }

        private static string RankOf(string code)
        {
            var rank = code[0];
            return rank == 'T' ? "10" : rank.ToString();
        }

        private static bool IsRed(char suit) => suit == 'H' || suit == 'D';
    }
}
