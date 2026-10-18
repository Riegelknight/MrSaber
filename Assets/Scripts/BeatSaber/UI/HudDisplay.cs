using UnityEngine;
using UnityEngine.UI;

namespace BeatSaber
{
    /// <summary>
    /// Boden-Anzeige (rund, fensterlos, Skalierung ~0.0008 laut Spezifikation):
    /// aktueller Score, Multiplikator x1–x8, Combo und Fortschritt zur NÄCHSTEN
    /// Multiplikator-Stufe (Treffer innerhalb der Stufe / benötigte Treffer).
    /// Hängt am Spielwelten-Root (BeatSaberWorld) und ist damit an der AUSRICHTUNG DES
    /// RAUMS orientiert (feste Position/Rotation), nicht an der Ausrichtung des Spielers:
    /// Sie folgt dem Kopf nicht mehr und bleibt beim Raum-X/Z-Reset (Taste A) im Raum stehen.
    /// </summary>
    public sealed class HudDisplay : MonoBehaviour
    {
        private Canvas _canvas;
        private RectTransform _panel;
        private Text _scoreText;
        private Text _comboText;

        public void Build(Camera cam)
        {
            name = "FloorHUD";
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            // Canvas-Einheit: Skala ~0.0008 => 1 Canvas-Einheit = 0.0008 m
            transform.localScale = new Vector3(0.0008f, 0.0008f, 0.0008f);

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            _panel.sizeDelta = new Vector2(900f, 320f);

            var img = panelGo.AddComponent<Image>();
            img.sprite = BsUi.CircleSprite();
            img.color = new Color(0f, 0f, 0f, 0.55f);

            var font = BsUi.Font();

            _scoreText = CreateText("Score", panelGo.transform, font, new Vector2(860f, 150f),
                new Vector2(0f, 75f), 110, "SCORE 0000000", Color.white);
            _comboText = CreateText("Combo", panelGo.transform, font, new Vector2(860f, 140f),
                new Vector2(0f, -80f), 56, "COMBO 0  x1\n[░░░░░░░░░░]", new Color(1f, 0.85f, 0.4f));
        }

        private static Text CreateText(
            string name, Transform parent, Font font, Vector2 size, Vector2 anchoredPos,
            int fontSize, string content, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            var text = go.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = content;
            text.color = color;
            return text;
        }

        public void Show(bool visible) => _canvas.enabled = visible;

        public void SetScore(int score, int combo, int multiplier, float progress)
        {
            if (_scoreText == null || _comboText == null) return;
            _scoreText.text = "SCORE " + score.ToString("N0");

            int progressDots = Mathf.Clamp(Mathf.RoundToInt(progress * 10f), 0, 10);
            string dots = new string('█', progressDots) + new string('░', 10 - progressDots);
            _comboText.text = $"COMBO {combo}  x{multiplier}\n[{dots}]";
        }
    }
}
