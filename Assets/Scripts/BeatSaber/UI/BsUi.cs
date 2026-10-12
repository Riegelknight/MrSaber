using UnityEngine;

namespace BeatSaber
{
    /// <summary>Gemeinsame UI-Helfer (deterministisch, ohne Abhängigkeit von importierten Sprites).</summary>
    public static class BsUi
    {
        private static Sprite _white;
        private static Sprite _circle;
        private static Sprite _rounded;

        // ---- Farbpalette des Handmenüs: dunkles, leicht bläuliches Glas + grüner Akzent ----
        /// <summary>Grundfläche der Tafel.</summary>
        public static readonly Color PanelColor = new Color(0.055f, 0.065f, 0.105f, 0.96f);
        /// <summary>Heller Rand der Tafel/Karte.</summary>
        public static readonly Color PanelEdge = new Color(0.42f, 0.86f, 1f, 0.42f);
        /// <summary>Hintergrundbalken der Titelzeile.</summary>
        public static readonly Color TitleBarColor = new Color(0.38f, 1f, 0.55f, 0.14f);
        /// <summary>Grundfläche normaler Zeilen (dezent, damit sie sich vom Panel abheben).</summary>
        public static readonly Color RowColor = new Color(1f, 1f, 1f, 0.045f);
        /// <summary>Grundfläche der Bedienfelder (−/+/◄/►), wenn sie NICHT anvisiert sind.</summary>
        public static readonly Color ChipColor = new Color(1f, 1f, 1f, 0.10f);
        /// <summary>Bedienfeld unter dem Laser (sattes Grün).</summary>
        public static readonly Color ChipHiColor = new Color(0.38f, 1f, 0.55f, 0.85f);
        /// <summary>Akzentfarbe für Symbole/Glyphen.</summary>
        public static readonly Color AccentColor = new Color(0.38f, 1f, 0.55f, 1f);
        /// <summary>Beschriftungstext der Bedienkarten (gedämpft).</summary>
        public static readonly Color LabelColor = new Color(0.72f, 0.80f, 0.92f, 1f);
        /// <summary>Wertetext (hell).</summary>
        public static readonly Color ValueColor = new Color(1f, 1f, 1f, 1f);
        /// <summary>Zeilen-Markierung (unter dem Laser) — bewusst neutral-transparent.</summary>
        public static readonly Color CursorColor = new Color(1f, 1f, 1f, 0.16f);

        public static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "BsUiWhite",
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _white = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
            return _white;
        }

        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "BsUiCircle" };
            var pixels = new Color[size * size];
            float radius = size * 0.5f - 1f;
            var center = new Vector2(size * 0.5f, size * 0.5f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    float alpha = Mathf.Clamp01(radius - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circle;
        }

        public static Sprite RoundedSprite()
        {
            if (_rounded != null) return _rounded;
            const int size = 64;
            const float radius = 18f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "BsUiRounded" };
            var pixels = new Color[size * size];
            var half = new Vector2(size * 0.5f, size * 0.5f);
            var inner = new Vector2(half.x - radius, half.y - radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Vector2 p = new Vector2(Mathf.Abs(x + 0.5f - half.x), Mathf.Abs(y + 0.5f - half.y)) - inner;
                    float outside = Vector2.Max(p, Vector2.zero).magnitude;
                    float inside = Mathf.Min(Mathf.Max(p.x, p.y), 0f);
                    float dist = outside + inside - radius;
                    // Zentrale Überlappung liegt bei dist <= -radius (unten geklemmt); die
                    // Ecken-SDF liefert dort <= 0. Kantenglättung über 1 px.
                    float alpha = Mathf.Clamp01(0.5f - dist);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
            return _rounded;
        }

        public static Font Font()
        {
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { return null; }
        }
    }
}
