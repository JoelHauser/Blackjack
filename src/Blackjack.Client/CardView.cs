using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Blackjack.Client
{
    /// <summary>
    /// Draws one playing card.
    ///
    /// The server sends cards as two characters, rank then suit: "TD" is the ten of
    /// diamonds, "AS" the ace of spades. There are no card sprites to load, so a card
    /// is a pale rounded rectangle with the rank in the corners and a large suit in
    /// the middle, which reads correctly at a glance and costs nothing to ship.
    /// </summary>
    internal static class CardView
    {
        internal const float Width = 92f;
        internal const float Height = 132f;

        private static readonly Color Face = new Color(0.93f, 0.92f, 0.88f, 1f);
        private static readonly Color Red = new Color(0.72f, 0.13f, 0.13f, 1f);
        private static readonly Color Black = new Color(0.11f, 0.11f, 0.12f, 1f);

        /// <summary>Back of a card, for the dealer's hole card during the player's turn.</summary>
        private static readonly Color BackFill = new Color(0.35f, 0.11f, 0.13f, 1f);

        internal static GameObject Build(Transform parent, string code, TMP_FontAsset font)
        {
            var go = new GameObject("Card_" + code, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(Width, Height);

            var image = go.GetComponent<Image>();

            // An unknown or absent code is drawn face down rather than blank, which is
            // what the dealer's hidden card looks like while a hand is in play.
            if (string.IsNullOrEmpty(code) || code.Length < 2)
            {
                image.color = BackFill;
                return go;
            }

            image.color = Face;

            var rank = RankOf(code);
            var suit = SuitOf(code, font);
            var colour = IsRed(code) ? Red : Black;

            AddLabel(rect, "Rank", rank, font, 30f, colour, TextAlignmentOptions.TopLeft,
                new Vector2(8f, -6f));

            AddLabel(rect, "Suit", suit, font, 52f, colour, TextAlignmentOptions.Center,
                Vector2.zero);

            return go;
        }

        private static void AddLabel(
            RectTransform parent,
            string name,
            string text,
            TMP_FontAsset font,
            float size,
            Color colour,
            TextAlignmentOptions align,
            Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                label.font = font;
            }

            label.text = text;
            label.fontSize = size;
            label.color = colour;
            label.alignment = align;
            label.enableWordWrapping = false;

            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 6f);
            rect.offsetMax = new Vector2(-6f, -6f);
            rect.anchoredPosition += offset;
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
