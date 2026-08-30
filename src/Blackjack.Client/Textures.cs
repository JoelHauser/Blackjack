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
