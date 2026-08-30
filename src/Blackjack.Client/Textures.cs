using System.Collections.Generic;
using UnityEngine;

namespace Blackjack.Client
{
    /// <summary>
    /// Sprites drawn in code at load, because the mod ships no art.
    ///
    /// Everything here is small and nine-sliced rather than drawn at final size: a
    /// 64-pixel rounded box stretches to a card, a chip or the table itself without
    /// distorting its corners, and one texture serves every size it is asked for.
    /// Drawing each at its real size would mean a texture per widget and a rebuild
    /// whenever the table is resized.
    ///
    /// Sprites are cached by their parameters. Unity will happily let you allocate a
    /// new texture every frame and say nothing until the memory is gone.
    /// </summary>
    internal static class Textures
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        /// <summary>
        /// A rounded rectangle with an optional border, for nine-slicing.
        ///
        /// The corner radius is baked into the texture and protected by the sprite's
        /// border, so only the flat middle stretches. That is what keeps a card's
        /// corners the same shape as a chip's.
        /// </summary>
        internal static Sprite RoundedBox(int radius, Color fill, Color border, int borderWidth = 0)
        {
            var key = $"box:{radius}:{fill}:{border}:{borderWidth}";
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var size = radius * 4;
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = CornerDistance(x, y, size, radius);

                    // Antialiased edge: one pixel of falloff rather than a hard step,
                    // which is the difference between a rounded corner and a jagged one.
                    var alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    var colour = fill;

                    if (borderWidth > 0 && distance > radius - borderWidth - 0.5f)
                    {
                        colour = border;
                    }

                    colour.a *= alpha;
                    pixels[(y * size) + x] = colour;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));

            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A soft dark vignette, laid over the felt so the table is lit from the middle
        /// rather than being one flat colour. This is the single cheapest thing that
        /// stops a green rectangle reading as a green rectangle.
        /// </summary>
        internal static Sprite Vignette(Color edge, float strength = 1f)
        {
            var key = $"vignette:{edge}:{strength}";
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            const int size = 128;
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            var centre = (size - 1) * 0.5f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Normalised distance from the middle, eased so the darkening stays
                    // out of the way until it is near the rim.
                    var dx = (x - centre) / centre;
                    var dy = (y - centre) / centre;
                    var d = Mathf.Clamp01(Mathf.Sqrt((dx * dx) + (dy * dy)) / 1.414f);

                    var colour = edge;
                    colour.a = edge.a * Mathf.Pow(d, 2.2f) * strength;
                    pixels[(y * size) + x] = colour;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// An outlined circle, for the betting spot painted on the cloth.
        /// </summary>
        internal static Sprite Ring(Color colour, float thickness = 0.045f)
        {
            var key = $"ring:{colour}:{thickness}";
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            const int size = 256;
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            var centre = (size - 1) * 0.5f;
            var outer = centre - 2f;
            var inner = outer * (1f - thickness * 2f);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x - centre;
                    var dy = y - centre;
                    var d = Mathf.Sqrt((dx * dx) + (dy * dy));

                    var alpha = Mathf.Clamp01(outer - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);

                    var c = colour;
                    c.a *= alpha;
                    pixels[(y * size) + x] = c;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A card suit, drawn rather than typed.
        ///
        /// EFT's UI font has no card suits in it -- asked directly, HasCharacter says
        /// no for all four -- so spelling them meant a giant letter C in the middle of
        /// the club. These are the real shapes, from their implicit curves, which is
        /// the difference between a card and a rectangle with a letter on it.
        /// </summary>
        internal static Sprite Suit(char suit, Color colour)
        {
            var key = $"suit:{suit}:{colour}";
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            const int size = 160;
            var texture = NewTexture(size, size);
            var pixels = new Color[size * size];

            // Supersampled: four samples a pixel, because a heart's shoulders and a
            // spade's point are all curve and alias badly at this size.
            const int samples = 2;
            var step = 1f / (samples + 1);

            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var hits = 0;

                    for (var sy = 1; sy <= samples; sy++)
                    {
                        for (var sx = 1; sx <= samples; sx++)
                        {
                            // Normalised to roughly -1..1 with a small margin.
                            var x = (((px + (sx * step)) / size) - 0.5f) * 2.2f;
                            var y = (((py + (sy * step)) / size) - 0.5f) * 2.2f;

                            if (Inside(suit, x, y))
                            {
                                hits++;
                            }
                        }
                    }

                    var c = colour;
                    c.a *= hits / (float)(samples * samples);
                    pixels[(py * size) + px] = c;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            Cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Whether a point falls inside a suit. Y is up, so the shapes are described the
        /// way they are drawn rather than the way a texture is stored.
        /// </summary>
        private static bool Inside(char suit, float x, float y)
        {
            switch (char.ToUpperInvariant(suit))
            {
                case 'D':
                    // A rhombus, taller than it is wide, as on a real card.
                    return (Mathf.Abs(x) / 0.62f) + (Mathf.Abs(y) / 0.92f) <= 1f;

                case 'H':
                    return Heart(x, y);

                case 'S':
                    // A heart upside down, on a stem.
                    return Heart(x * 1.02f, -y + 0.12f) || Stem(x, y);

                case 'C':
                    return Club(x, y) || Stem(x, y);

                default:
                    return false;
            }
        }

        /// <summary>The classic implicit heart, scaled to sit in the box.</summary>
        private static bool Heart(float x, float y)
        {
            const float scale = 1.18f;
            var hx = x * scale;
            var hy = (y * scale) - 0.28f;

            var a = (hx * hx) + (hy * hy) - 0.62f;
            return (a * a * a) - (hx * hx * hy * hy * hy) <= 0f;
        }

        /// <summary>Three lobes, for the club.</summary>
        private static bool Club(float x, float y)
        {
            const float r = 0.42f;
            return Circle(x, y - 0.44f, r)
                   || Circle(x - 0.46f, y + 0.06f, r)
                   || Circle(x + 0.46f, y + 0.06f, r);
        }

        /// <summary>The tapered foot shared by the spade and the club.</summary>
        private static bool Stem(float x, float y)
        {
            if (y > 0.06f || y < -0.92f)
            {
                return false;
            }

            // Widens towards the base.
            var halfWidth = Mathf.Lerp(0.06f, 0.40f, Mathf.InverseLerp(0.06f, -0.92f, y));
            halfWidth *= halfWidth > 0.2f ? 1f : 1f;
            return Mathf.Abs(x) <= halfWidth * (y < -0.62f ? 1.1f : 0.7f);
        }

        private static bool Circle(float x, float y, float r) => (x * x) + (y * y) <= r * r;

        /// <summary>
        /// Distance from the nearest corner's centre of curvature, or zero along the
        /// flat edges. Everything the rounded box does follows from this.
        /// </summary>
        private static float CornerDistance(int x, int y, int size, int radius)
        {
            var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
            var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
            var dx = x + 0.5f - cx;
            var dy = y + 0.5f - cy;
            return Mathf.Sqrt((dx * dx) + (dy * dy));
        }

        private static Texture2D NewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                // Clamped, or the antialiased edge wraps and leaves a seam on the
                // opposite side when the sprite is stretched.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
