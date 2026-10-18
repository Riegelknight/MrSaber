using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BeatSaber
{
    /// <summary>
    /// Hand-Menü (am Handgelenk fixiert, folgt der Controller-Bewegung):
    /// eine Zeilenliste mit Cursor; die ANDERE Hand zeigt mit ihrem Lichtschwert
    /// (Pointer-Strahl) auf die Tafel. Bestätigt wird mit dem Index-Trigger
    /// (der seitliche Grip ist für das Easter Egg reserviert).
    /// Das Menü selbst ist nur Anzeige — die Logik (was Zeilen tun) steckt im GameManager.
    /// Für lange Listen (Songliste) gibt es ein Scroll-Window; geblättert wird per Joystick.
    /// </summary>
    public sealed class HandMenu : MonoBehaviour
    {
        public struct Row
        {
            public string Text;
            public int Id;          // -1 = nicht wählbar
            public bool Selectable;
            public float Height;    // 0 = Standard-Zeilenhöhe
            public bool Title;      // Titelzeile (größer, fett)
            public Color? Background; // z. B. blauer Balken hinter dem aktiven Spieler
            public int FontSize;    // 0 = Standard (48); kleiner für lange Werte-Zeilen
            public bool MultiControl; // Bedienkarte mit Feldern (−/+ bzw. ◄/►)
            public string Value;    // Wert (Bedienkarte mittig bzw. rechtsbündig bei RightValue)
            public bool Cycle;      // Bedienkarte mit Wechsel-Pfeilen ◄/► statt −/+
            public bool Button;     // als Schaltfläche hinterlegt (dezent grau, wie die Bedienkarten)
            public bool RightValue; // Text linksbündig, Value rechtsbündig (z. B. Spieler-Punkte)
            public bool Card;       // dezente graue Hinterlegung (wie die Bedienkarten)
            public bool Deletable;  // Songliste: Zeile darf per „X" gelöscht werden (nie das Demo-Lied)
        }

        private const float TitleHeight = 96f;
        private const float ControlRowHeight = 96f; // zweizeilige Bedienkarte (Label + Wert/Felder)
        private const int SongListViewport = 7;   // sichtbare Songzeilen im Listen-Untermenü

        // Bedienkarte: feste Plätze der Felder links/rechts (Tafel-lokal relativ zur Zeilenmitte)
        private const float ControlChipX = 160f;
        private const float ControlChipY = -16f;
        private static readonly Vector2 ControlChipSize = new Vector2(74f, 50f);

        public bool Visible { get; private set; }
        public int Cursor { get; private set; }
        public int SelectableCount { get; private set; }

        /// <summary>Treffer-X des Laserpunkts in der Tafel (lokal, links negativ / rechts positiv).</summary>
        public float LastPointerX => _lastPointerX;

        private Canvas _canvas;
        private RectTransform _panel;
        private Image _cursorHighlight;
        private Image _controlHighlight;   // Markierung des aktiven Bedienfelds (−/+ bzw. ◄/►)
        private readonly List<GameObject> _rowObjects = new();
        private readonly List<Row> _rows = new();
        private readonly List<Text> _rowValueTexts = new(); // Wert-Text je Zeile (null = keine Wertanzeige)

        // Modaler Bestätigungs-Dialog (z. B. Spieler löschen): EIGENES Overlay-Canvas, das
        // MITTIG über der weiterhin sichtbaren Tafel liegt. Der Grund wird nur leicht abgedunkelt.
        private Transform _confirmRoot;
        private Image _confirmScrim;
        private Image _confirmCard;
        private Text _confirmTitle;
        private Text _confirmDetail;
        private Image _confirmYesBg, _confirmNoBg;
        private Text _confirmYesText, _confirmNoText;
        private bool _confirmVisible;
        public bool ConfirmVisible => _confirmVisible;
        /// <summary>0 = Ja, 1 = Nein — zuletzt vom Laser markierte Auswahl im Dialog.</summary>
        public int ConfirmChoice { get; private set; } = 1;
        private const float ConfirmW = 660f;
        private const float ConfirmH = 320f;
        private static readonly Vector2 ConfirmNoPos = new Vector2(-165f, -95f);
        private static readonly Vector2 ConfirmYesPos = new Vector2(165f, -95f);
        private static readonly Vector2 ConfirmBtnSize = new Vector2(280f, 76f);

        // Layout je (globalem) Zeilen-Index — nur sichtbare Zeilen sind gefüllt.
        // Bewusst NICHT auf 64 begrenzt: Die Spielerverwaltung ist (anders als die Songliste)
        // nicht scrollbar; mit vielen Profilen kann sie sonst über die feste Länge laufen.
        // RefreshGeometry vergrößert die Puffer bei Bedarf.
        private float[] _rowCenterY = new float[64];
        private float[] _rowHeights = new float[64];
        private LineRenderer _laser;
        private Image _reticle;
        private Transform _handAnchor;
        private Camera _cam;
        private float _rowHeight = 72f;
        private float _lastPointerX;    // lokale Treffer-X (für (−)/(+)-Zeilen)
        // Laser-Treffer (Tafel-lokal). Wird JEDEN Frame aktualisiert, wenn der Laser die
        // Tafel trifft. In der Songliste bestimmt er die Zeile unter dem Laser neu — auch
        // dann, wenn die Liste sich unter dem ruhenden Laser wegbewegt (Joystick-Scroll).
        private Vector2 _lastPointerLocal;
        private bool _hasPointerLocal;

        // Laufschrift der Liednamen (nur Songlisten-Untermenü): Nur die Zeile unter dem Laser
        // läuft durch, damit ein langer Name trotz gekappter Fensterbreite lesbar bleibt.
        private const float SongTextWidth = 470f;   // sichtbare Textbreite (= Maskenbreite)
        private const float SongTextClipX = -40f;   // Clip links versetzt -> rechts Platz für das X
        private const float MarqueeSpeedPx = 70f;   // Laufgeschwindigkeit
        private float _marqueeTimer;

        // Lösch-„X" je Liedzeile (nur Songliste): erscheint rechts in der Zeile unter dem Laser.
        // DUNKELrot, solange nur die Zeile anvisiert ist — HELLrot, sobald der Laser direkt auf
        // dem X steht. Aus zwei gekreuzten Balken gezeichnet (kein Font-Zeichen nötig).
        private const float SongDeleteSize = 52f;
        private const float SongDeleteVisualRadius = 16f; // Balken 41,6 px lang, ±45° -> ±15 px
        private const float SongDeleteCenterX = 242f;
        private static readonly Color SongDeleteDim = new Color(0.85f, 0.30f, 0.30f, 0.40f);
        private static readonly Color SongDeleteHot = new Color(1.00f, 0.16f, 0.16f, 1.00f);
        private RectTransform _songDeleteIcon;
        private Image _songDeleteBarA, _songDeleteBarB;
        private bool _pointerOnSongDelete;

        /// <summary>True, solange der Laser direkt auf dem Lösch-„X" der anvisierten Liedzeile
        /// steht. Der GameManager öffnet dann den „Lied löschen?"-Dialog statt das Lied zu wählen.</summary>
        public bool PointerOnSongDeleteIcon => _pointerOnSongDelete;
        private sealed class SongTextRef
        {
            public int Index;
            public Text Text;
            public float Overflow;
            public bool Measured;
        }
        private readonly List<SongTextRef> _songTextRefs = new();

        // Scroll-Zustand (nur Songlisten-Untermenü)
        private bool _scrollable;
        // Sichtbare Zeilen des Scroll-Fensters. Wird (falls eine Zielhöhe übergeben wird)
        // aus der Höhe des Hauptmenüs abgeleitet, damit die Songliste GENAU so hoch ist.
        private int _viewportRows = SongListViewport;
        private float _targetPanelHeight; // 0 = natürliche Höhe (kein Auffüllen)
        // Erster an DISPLAY-Position 0 gezeigter Inhalt-Zeilenindex. In der Songliste
        // zyklisch (modulo Liederzahl) — die Liste hat kein Ende, die Lieder wiederholen sich.
        private int _scrollOffset;
        // Zeile unter dem Laser in der Songliste (-1 = keine). Die Songliste hat bewusst
        // KEINE dauerhafte Auswahl: Nur die gerade anvisierte Zeile wird grün hervorgehoben.
        private int _hoverRow = -1;

        // ---- Position/Ausrichtung der Tafel ----
        // Die Tafel liegt IMMER über dem Controller; der Abstand zwischen Controller und
        // Tafel-UNTERKANTE ist fest (etwas Luft dazwischen). Sie schaut zum Headset, ist aber
        // leicht nach hinten gekippt, und sitzt durch den Vorwärts-Offset weiter weg.
        private const float MenuBottomClearance = 0.06f; // m Luft zwischen Controller u. Unterkante
        private const float MenuForwardOffset = 0.10f;   // m weiter weg (+ Blickrichtung)
        private const float MenuTiltDeg = -10f;           // Neigung: 20° minus 30° = 350° (−10°)

        public void Build(Transform handAnchor, Camera cam)
        {
            _handAnchor = handAnchor;
            _cam = cam;
            name = "HandMenu";
            transform.SetParent(handAnchor, false);

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            transform.localScale = new Vector3(0.0004f, 0.0004f, 0.0004f);
            _canvas.enabled = false;
            Visible = false;

            var panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(transform, false);
            _panel = panelGo.AddComponent<RectTransform>();
            var img = panelGo.AddComponent<Image>();
            img.sprite = BsUi.RoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = BsUi.PanelColor;
            var panelOutline = panelGo.AddComponent<Outline>();
            panelOutline.effectColor = BsUi.PanelEdge;
            panelOutline.effectDistance = new Vector2(3f, 3f);

            _cursorHighlight = new GameObject("Cursor").AddComponent<Image>();
            _cursorHighlight.transform.SetParent(_panel, false);
            _cursorHighlight.sprite = BsUi.RoundedSprite();
            _cursorHighlight.type = Image.Type.Sliced;
            _cursorHighlight.color = BsUi.CursorColor;
            _cursorHighlight.rectTransform.sizeDelta = new Vector2(560f, _rowHeight - 10f);

            // Markierung des gerade anvisierten Bedienfelds einer Bedienkarte.
            _controlHighlight = new GameObject("ControlField").AddComponent<Image>();
            _controlHighlight.transform.SetParent(_panel, false);
            _controlHighlight.sprite = BsUi.RoundedSprite();
            _controlHighlight.type = Image.Type.Sliced;
            _controlHighlight.color = BsUi.ChipHiColor;
            _controlHighlight.rectTransform.sizeDelta = ControlChipSize + new Vector2(4f, 4f);
            _controlHighlight.enabled = false;

            // Lösch-„X" je Liedzeile (Songliste): Container mit zwei gekreuzten Balken,
            // unsichtbar bis die Zeile unter dem Laser löschbar ist (RefreshSongDeleteIcon).
            var delGo = new GameObject("SongDeleteX");
            delGo.transform.SetParent(_panel, false);
            _songDeleteIcon = delGo.AddComponent<RectTransform>();
            CenterAnchors(_songDeleteIcon);
            _songDeleteIcon.sizeDelta = new Vector2(SongDeleteSize, SongDeleteSize);
            _songDeleteBarA = MakeDeleteBar(delGo.transform, 45f);
            _songDeleteBarB = MakeDeleteBar(delGo.transform, -45f);
            delGo.SetActive(false);

            BuildConfirmUi();

            // Zeige-Laser: zeigt von der anderen Hand zur Menü-Tafel
            var laserGo = new GameObject("PointerLaser");
            laserGo.transform.SetParent(transform, false);
            _laser = laserGo.AddComponent<LineRenderer>();
            _laser.positionCount = 2;
            _laser.startWidth = 0.004f;
            _laser.endWidth = 0.0015f;
            _laser.useWorldSpace = true;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _laser.material = new Material(sh);
            _laser.startColor = _laser.endColor = new Color(1f, 1f, 1f, 0.9f);
            _laser.enabled = false;

            // Reticle am Laser-Endpunkt (Standard-MR-Look: Laser + Fadenkreuz)
            _reticle = new GameObject("LaserReticle").AddComponent<Image>();
            _reticle.transform.SetParent(_panel, false);
            _reticle.sprite = BsUi.CircleSprite();
            _reticle.color = new Color(1f, 1f, 1f, 0.95f);
            _reticle.rectTransform.sizeDelta = new Vector2(40f, 40f);
            _reticle.enabled = false;
        }

        /// <summary>Erzeugt einen Text als Kind (zentriert verankert, feste Größe/Position).</summary>
        private static Text MakeText(Transform parent, string text, int fontSize, TextAnchor align,
            Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = BsUi.Font();
            t.fontSize = fontSize;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = color;
            t.text = text;
            t.rectTransform.sizeDelta = size;
            CenterAnchors(t.rectTransform);
            t.rectTransform.anchoredPosition = pos;
            return t;
        }

        private static Image MakeRounded(Transform parent, Color color, Vector2 size, Vector2 pos)
        {
            var go = new GameObject("Rounded");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = BsUi.RoundedSprite();
            img.type = Image.Type.Sliced;
            img.color = color;
            img.rectTransform.sizeDelta = size;
            CenterAnchors(img.rectTransform);
            img.rectTransform.anchoredPosition = pos;
            return img;
        }

        /// <summary>
        /// Zentriert die Anker eines UI-Elements (Mittelpunkt = Mitte des ELTERN-Elements,
        /// Pivot zentral). Damit ist <c>anchoredPosition</c> garantiert relativ zur
        /// Eltern-Mitte — genau der Raum, den auch der Laser-Treffer prüft.
        /// </summary>
        private static void CenterAnchors(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>Ein Balken des Lösch-„X" (zwei davon um ±45° gekreuzt).</summary>
        private static Image MakeDeleteBar(Transform parent, float angle)
        {
            var go = new GameObject("Bar");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = BsUi.WhiteSprite();
            img.raycastTarget = false;
            CenterAnchors(img.rectTransform);
            img.rectTransform.sizeDelta = new Vector2(SongDeleteSize * 0.80f, 5f);
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
            return img;
        }

        private static Image CreateButton(Transform parent, string label, Vector2 pos, Vector2 size,
            Color bg, out Text text)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = BsUi.RoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = bg;
            CenterAnchors(image.rectTransform);
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = pos;

            var tgo = new GameObject("Label");
            tgo.transform.SetParent(go.transform, false);
            text = tgo.AddComponent<Text>();
            text.font = BsUi.Font();
            text.fontSize = 44;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.text = label;
            text.color = Color.white;
            text.rectTransform.sizeDelta = size;
            CenterAnchors(text.rectTransform);
            return image;
        }

        /// <summary>
        /// Baut den modalen Bestätigungs-Dialog als EIGENES Overlay-Canvas auf (versteckt).
        /// Ein eigenes Canvas mit übersteuerndem Sortierschlüssel garantiert, dass der Dialog
        /// unabhängig von der Panel-Hierarchie SICHTBAR über allem liegt.
        /// </summary>
        private void BuildConfirmUi()
        {
            var rootGo = new GameObject("ConfirmPopup");
            rootGo.transform.SetParent(transform, false);

            // Eigenes Canvas: erzwingt die Anzeige über der Tafel (Sortierschlüssel).
            // Das Canvas legt selbst die RectTransform an; danach konfigurieren.
            var popupCanvas = rootGo.AddComponent<Canvas>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = 100;
            var rootRt = rootGo.GetComponent<RectTransform>();
            CenterAnchors(rootRt);
            rootRt.sizeDelta = new Vector2(ConfirmW, ConfirmH);
            rootRt.anchoredPosition = Vector2.zero;

            _confirmRoot = rootGo.transform;

            // Leichte Abdunklung der Tafel (das Fenster darunter bleibt erkennbar).
            var scrimGo = new GameObject("Scrim");
            scrimGo.transform.SetParent(_confirmRoot, false);
            _confirmScrim = scrimGo.AddComponent<Image>();
            _confirmScrim.sprite = BsUi.WhiteSprite();
            _confirmScrim.color = new Color(0.01f, 0.01f, 0.03f, 0.55f);
            CenterAnchors(_confirmScrim.rectTransform);
            _confirmScrim.rectTransform.sizeDelta = new Vector2(ConfirmW, ConfirmH);

            // Karte
            _confirmCard = MakeRounded(_confirmRoot, BsUi.PanelColor, new Vector2(ConfirmW, ConfirmH), Vector2.zero);
            var cardOutline = _confirmCard.gameObject.AddComponent<Outline>();
            cardOutline.effectColor = BsUi.PanelEdge;
            cardOutline.effectDistance = new Vector2(3f, 3f);

            _confirmTitle = MakeText(_confirmRoot, "", 50, TextAnchor.MiddleCenter, Color.white,
                new Vector2(ConfirmW - 40f, 60f), new Vector2(0f, 100f));
            _confirmDetail = MakeText(_confirmRoot, "", 40, TextAnchor.MiddleCenter, BsUi.LabelColor,
                new Vector2(ConfirmW - 60f, 56f), new Vector2(0f, 40f));

            // Genau ZWEI Auswahlmöglichkeiten: grünes „Nein" (sicher) und rotes „Ja" (löschen).
            _confirmNoBg = CreateButton(_confirmRoot, "Nein", ConfirmNoPos, ConfirmBtnSize,
                new Color(0.2f, 0.85f, 0.4f, 0.22f), out _confirmNoText);
            _confirmYesBg = CreateButton(_confirmRoot, "Ja", ConfirmYesPos, ConfirmBtnSize,
                new Color(0.9f, 0.3f, 0.3f, 0.22f), out _confirmYesText);

            rootGo.SetActive(false);
        }

        /// <summary>Zeigt den modalen Bestätigungs-Dialog mittig über der Tafel.</summary>
        public void ShowConfirm(string title, string detail = null)
        {
            _confirmVisible = true;
            ConfirmChoice = 1; // Standard: Nein (sicher)
            if (_confirmTitle != null) _confirmTitle.text = title;
            if (_confirmDetail != null)
            {
                _confirmDetail.text = detail ?? "";
                _confirmDetail.gameObject.SetActive(!string.IsNullOrEmpty(detail));
            }
            if (_confirmRoot != null)
            {
                if (_confirmScrim != null)
                    _confirmScrim.rectTransform.sizeDelta = _panel.sizeDelta + new Vector2(60f, 60f);
                PlaceConfirmOverPanel(); // sofort mittig über der Tafel (nicht erst im LateUpdate)
                _confirmRoot.gameObject.SetActive(true);
                _confirmRoot.SetAsLastSibling();
            }
            if (_cursorHighlight != null) _cursorHighlight.enabled = false;
            if (_controlHighlight != null) _controlHighlight.enabled = false;
            RefreshSongDeleteIcon(); // das Lösch-„X" hinter dem Dialog ausblenden
            UpdateConfirmVisual();
            Debug.Log($"[BeatSaber] HandMenu.ShowConfirm '{title}' visible={_confirmVisible} root={_confirmRoot != null}.");
        }

        /// <summary>Positioniert den Dialog in die Mitte der Tafel (Weltposition).</summary>
        private void PlaceConfirmOverPanel()
        {
            if (_confirmRoot == null || _panel == null) return;
            Vector3 center = _panel.TransformPoint(new Vector3(0f, -_panel.sizeDelta.y * 0.5f, 0f));
            _confirmRoot.position = center;
            _confirmRoot.rotation = transform.rotation;
        }

        /// <summary>Versteckt den Bestätigungs-Dialog (Tafel darunter wird wieder bedienbar).</summary>
        public void HideConfirm()
        {
            _confirmVisible = false;
            if (_confirmRoot != null) _confirmRoot.gameObject.SetActive(false);
            RefreshCursorPosition(); // Zeilen-Markierung wieder aufnehmen
            UpdateControlHighlight();
        }

        /// <summary>
        /// Pfadfinder für den Dialog: markiert die Auswahl unter dem Laser. Geprüft wird an
        /// den TATSÄCHLICH gezeichneten Buttons (Weltposition -> Tafel-Lokal), damit Anzeige
        /// und Trefferprüfung nie auseinanderlaufen können.
        /// </summary>
        private void UpdateConfirmPointer(Vector2 local)
        {
            if (!_confirmVisible || _confirmYesBg == null || _confirmNoBg == null) return;
            if (IsPointerOn(local, _confirmYesBg)) ConfirmChoice = 0;                 // Ja
            else if (IsPointerOn(local, _confirmNoBg)) ConfirmChoice = 1;             // Nein
            UpdateConfirmVisual(); // Bestätigt bleibt die zuletzt getroffene Auswahl
        }

        /// <summary>True, wenn der Laser-Punkt (Tafel-Lokal) über dem Button-Rect liegt.</summary>
        private bool IsPointerOn(Vector2 local, Image button)
        {
            if (button == null) return false;
            // Beide Elemente hängen unter derselben Tafel mit identischer Skalierung —
            // die Rect-Größe in eigenen Einheiten entspricht daher Tafel-Lokal-Einheiten.
            Vector3 center = _panel.InverseTransformPoint(button.rectTransform.position);
            Vector2 half = button.rectTransform.rect.size * 0.5f;
            return Mathf.Abs(local.x - center.x) <= half.x
                && Mathf.Abs(local.y - center.y) <= half.y;
        }

        private void UpdateConfirmVisual()
        {
            if (_confirmYesBg == null || _confirmNoBg == null) return;
            // „Ja" ist rot (Löschen), „Nein" ist grün (sicher) — jeweils hell hervorgehoben.
            _confirmYesBg.color = ConfirmChoice == 0 ? new Color(0.95f, 0.35f, 0.35f, 0.95f)
                                                     : new Color(0.9f, 0.3f, 0.3f, 0.22f);
            _confirmNoBg.color = ConfirmChoice == 1 ? new Color(0.2f, 0.95f, 0.45f, 0.95f)
                                                    : new Color(0.2f, 0.85f, 0.4f, 0.22f);
            if (_confirmYesText != null) _confirmYesText.color = ConfirmChoice == 0 ? Color.black : Color.white;
            if (_confirmNoText != null) _confirmNoText.color = ConfirmChoice == 1 ? Color.black : Color.white;
        }

        /// <summary>
        /// Zeilenliste anzeigen. Bei scrollable=true wird die Liste als ENDLOSES, zyklisches
        /// Fenster aus SongListViewport Zeilen gezeichnet (die Lieder wiederholen sich); es
        /// gibt KEINE dauerhafte Auswahl, nur die Zeile unter dem Laser wird hervorgehoben.
        /// `scrollToRow` (z. B. das aktuelle Lied) zentriert nur das Fenster, wählt aber nichts an.
        /// Geblättert wird ausschließlich per Joystick (ScrollList).
        /// </summary>
        public void Show(List<Row> rows, bool scrollable = false, int scrollToRow = -1,
            float targetPanelHeight = 0f)
        {
            Cursor = 0;
            _hoverRow = -1;
            _marqueeTimer = 0f;
            _hasPointerLocal = false;
            _pointerOnSongDelete = false;
            _targetPanelHeight = scrollable ? targetPanelHeight : 0f;

            // Schneller Pfad: Ist die Zeilenstruktur identisch (nur Werte haben sich geändert,
            // z. B. „Skalierung ±" oder Spielerpunkte), werden NUR die Wert-Texte gesetzt —
            // kein Zerstören/Neuerzeugen aller Zeilen. Das war der Ruckler bei jedem Klick.
            if (!scrollable && TryUpdateValuesInPlace(rows))
            {
                _canvas.enabled = true;
                Visible = true;
                if (_laser != null)
                {
                    _laser.gameObject.SetActive(true);
                    _laser.enabled = false;
                }
                return;
            }

            _rows.Clear();
            _rows.AddRange(rows);

            SelectableCount = 0;
            foreach (var r in _rows) if (r.Selectable) SelectableCount++;

            _scrollable = scrollable;
            if (_scrollable)
            {
                // Sichtbare Zeilen: Standard-Viewport oder – bei übergebener Zielhöhe – so
                // viele Zeilen, wie in diese Höhe passen (damit die Songliste GENAU so hoch
                // wie das Hauptmenü ist und mehr Lieder zeigt).
                _viewportRows = SongListViewport;
                if (_targetPanelHeight > 0f)
                {
                    int fit = Mathf.FloorToInt((_targetPanelHeight - 16f) / _rowHeight);
                    _viewportRows = Mathf.Max(1, fit);
                }
                // Songliste: Fenster auf das übergebene Lied zentrieren. Das aktuelle Lied ist
                // dabei nur per Hintergrund hervorgehoben, NICHT angewählt (kein Cursor darauf).
                _scrollOffset = _rows.Count > 0 && scrollToRow >= 0
                    ? Mod(scrollToRow - _viewportRows / 2, _rows.Count)
                    : 0;
            }
            else
            {
                _scrollOffset = 0;
                _viewportRows = SongListViewport;
            }

            RefreshGeometry();
            _canvas.enabled = true;
            Visible = true;
            // Laser erst nach dem ersten Pointer-Abgleich zeigen (er trifft die Tafel nur,
            // solange die Hand dorthin zeigt) — nie mit alter Position aufblitzen lassen.
            if (_laser != null)
            {
                _laser.gameObject.SetActive(true);
                _laser.enabled = false;
            }
        }

        private bool TryUpdateValuesInPlace(List<Row> rows)
        {
            if (rows == null || rows.Count != _rows.Count || _rowValueTexts.Count != _rows.Count)
                return false;
            for (int i = 0; i < rows.Count; i++)
            {
                Row a = rows[i], b = _rows[i];
                if (a.Id != b.Id || a.Selectable != b.Selectable || a.MultiControl != b.MultiControl
                    || a.RightValue != b.RightValue || a.Button != b.Button || a.Title != b.Title
                    || a.Card != b.Card || a.FontSize != b.FontSize || a.Cycle != b.Cycle
                    || a.Deletable != b.Deletable
                    || a.Text != b.Text || a.Background != b.Background)
                    return false;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                _rows[i] = rows[i];
                var t = _rowValueTexts[i];
                if (t != null) t.text = rows[i].Value ?? "";
            }
            RefreshCursorPosition();
            UpdateControlHighlight();
            return true;
        }

        public void Hide()
        {
            _canvas.enabled = false;
            Visible = false;
            _hasPointerLocal = false;
            _pointerOnSongDelete = false;
            if (_songDeleteIcon != null) _songDeleteIcon.gameObject.SetActive(false);
            if (_laser != null)
            {
                _laser.enabled = false;
                _laser.gameObject.SetActive(false);
            }
            if (_reticle != null) _reticle.enabled = false;
            HideConfirm();
        }

        /// <summary>
        /// Harte Sicherung: Der Laser-Strahl (und das Fadenkreuz) existieren AUSSCHLIESSLICH,
        /// solange das Menü offen ist. Der LineRenderer liegt NICHT im Canvas, blendet also
        /// nicht automatisch über <c>_canvas.enabled</c> mit — hier wird sein Zustand deshalb
        /// jedes Frame erzwungen.
        /// </summary>
        private void Update()
        {
            UpdateMarquee();
            bool menuOpen = Visible && _canvas != null && _canvas.enabled;
            if (_laser != null && !menuOpen)
            {
                if (_laser.enabled) _laser.enabled = false;
                if (_laser.gameObject.activeSelf) _laser.gameObject.SetActive(false);
            }
            if (_reticle != null && !menuOpen && _reticle.enabled) _reticle.enabled = false;
        }

        /// <summary>
        /// Laufschrift der Liednamen: Nur die Zeile unter dem Laser läuft durch, sobald der
        /// Name breiter ist als die Zeile. Kurz stehen → nach links durchlaufen → kurz stehen.
        /// Alle anderen Zeilen stehen wieder auf Position 0.
        /// </summary>
        private void UpdateMarquee()
        {
            if (!_scrollable || _songTextRefs.Count == 0) return;
            _marqueeTimer += Time.unscaledDeltaTime;
            for (int i = 0; i < _songTextRefs.Count; i++)
            {
                var r = _songTextRefs[i];
                if (r.Text == null) continue;
                if (!r.Measured)
                {
                    r.Overflow = Mathf.Max(0f, r.Text.preferredWidth - SongTextWidth);
                    r.Measured = true;
                }
                float x = 0f;
                if (r.Index == _hoverRow && r.Overflow > 1f)
                {
                    const float hold = 0.7f; // Halt am Anfang/Ende (s)
                    float scrollTime = r.Overflow / MarqueeSpeedPx;
                    float cycle = hold + scrollTime + hold;
                    float tau = _marqueeTimer % cycle;
                    if (tau < hold) x = 0f;
                    else if (tau < hold + scrollTime) x = (tau - hold) * MarqueeSpeedPx;
                    else x = r.Overflow;
                }
                r.Text.rectTransform.anchoredPosition = new Vector2(-x, 0f);
            }
        }

        public void SetAnchor(Transform anchor)
        {
            _handAnchor = anchor;
            if (anchor != null) transform.SetParent(anchor, false);
        }

        public bool ScrollList(int delta)
        {
            if (!Visible || !_scrollable || delta == 0) return false;
            ScrollBy(delta);
            return true;
        }

        /// <summary>Id der aktuell anvisierten Zeile (Songliste: Zeile unter dem Laser; -1 = keine).</summary>
        public int CurrentRowId()
        {
            if (_scrollable)
            {
                return _hoverRow >= 0 && _hoverRow < _rows.Count ? _rows[_hoverRow].Id : -1;
            }
            int selectableSeen = 0;
            foreach (var r in _rows)
            {
                if (!r.Selectable) continue;
                if (selectableSeen == Cursor) return r.Id;
                selectableSeen++;
            }
            return -1;
        }

        private static int Mod(int a, int n) => ((a % n) + n) % n;

        /// <summary>Index der aktuell angewählten Zeile (oder -1).</summary>
        private int CurrentRowIndex()
        {
            int selectableSeen = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].Selectable) continue;
                if (selectableSeen == Cursor) return i;
                selectableSeen++;
            }
            return -1;
        }

        /// <summary>
        /// Markiert bei Bedienkarten zusätzlich das gerade anvisierte Bedienfeld (links = −/◄,
        /// rechts = +/►) über der Zeilen-Markierung.
        /// </summary>
        private void UpdateControlHighlight()
        {
            if (_controlHighlight == null) return;
            if (_confirmVisible || !Visible)
            {
                _controlHighlight.enabled = false;
                return;
            }
            int idx = CurrentRowIndex();
            if (idx < 0 || !_rows[idx].MultiControl)
            {
                _controlHighlight.enabled = false;
                return;
            }
            var rt = _controlHighlight.rectTransform;
            rt.sizeDelta = ControlChipSize + new Vector2(8f, 8f);
            float x = _lastPointerX < 0f ? -ControlChipX : ControlChipX;
            rt.anchoredPosition = new Vector2(x, _rowCenterY[idx] + ControlChipY + _panel.sizeDelta.y * 0.5f);
            _controlHighlight.enabled = true;
        }

        /// <summary>Zeilenhöhe einer Zeile nach ihrer Art (auch extern nutzbar, z. B. um die
        /// Gesamthöhe des Hauptmenüs zu berechnen und die Songliste darauf aufzufüllen).</summary>
        public static float HeightOf(Row r)
        {
            if (r.Height > 0f) return r.Height;
            if (r.MultiControl) return ControlRowHeight;
            if (r.Button) return 84f;
            return r.Title ? TitleHeight : 72f; // 72 = Standard-Zeilenhöhe (_rowHeight)
        }

        private float RowHeightFor(int index) => HeightOf(_rows[index]);

        private void RefreshGeometry()
        {
            // Alte Zeilen-Objekte (inkl. Kinder) entfernen.
            foreach (var o in _rowObjects) if (o != null) Destroy(o);
            _rowObjects.Clear();
            _songTextRefs.Clear();
            _rowValueTexts.Clear();
            for (int i = 0; i < _rows.Count; i++) _rowValueTexts.Add(null);
            // Puffer an die Zeilenzahl anpassen (nicht-scrollbare Listen wie die
            // Spielerverwaltung können beliebig viele Zeilen haben — kein Indexfehler).
            if (_rowCenterY.Length < _rows.Count)
            {
                int n = Mathf.Max(_rows.Count, 64);
                _rowCenterY = new float[n];
                _rowHeights = new float[n];
            }
            for (int i = 0; i < _rowCenterY.Length; i++)
            {
                _rowCenterY[i] = float.MinValue;
                _rowHeights[i] = 0f;
            }

            // Sichtbare Zeilen: Songliste = zyklisches Fenster der Länge `_viewportRows`
            // (Display-Position p -> Inhalt (offset + p) modulo Liederzahl).
            var visible = new List<int>();
            if (_scrollable && _rows.Count > 0)
            {
                int n = Mathf.Min(_viewportRows, _rows.Count);
                for (int p = 0; p < n; p++) visible.Add(Mod(_scrollOffset + p, _rows.Count));
            }
            else
            {
                for (int i = 0; i < _rows.Count; i++) visible.Add(i);
            }

            // Panel-Höhe vorab berechnen: Oberkante liegt am Anker (kein Leerraum oben).
            float totalH = 0f;
            foreach (int i in visible) totalH += RowHeightFor(i);
            // Zielhöhe (z. B. Höhe des Hauptmenüs): restlichen Platz gleichmäßig oben/unten
            // als Rand verteilen, damit die Tafel GENAU diese Höhe hat.
            float extra = 0f;
            if (_scrollable && _targetPanelHeight > 0f)
            {
                float natural = totalH + 16f;
                if (natural < _targetPanelHeight) extra = _targetPanelHeight - natural;
            }
            float half = (totalH + 16f + extra) * 0.5f; // +8 Rand oben, +8 unten (+ evtl. Auffüllung)
            float y = -8f - extra * 0.5f; // Oberkante der ersten Zeile

            foreach (int i in visible)
            {
                bool isTitle = _rows[i].Title;
                float h = RowHeightFor(i);
                float centerY = y - h * 0.5f;
                _rowCenterY[i] = centerY;
                _rowHeights[i] = h;
                // Panel-Pivot liegt oben (0.5/1): Kinder um half versetzen,
                // damit ihre Weltposition exakt centerY bleibt (Layout-Umrechnung).
                Vector2 childPos = new Vector2(0f, centerY + half);

                var rowGo = new GameObject("Row" + i);
                rowGo.transform.SetParent(_panel, false);
                var rt = rowGo.AddComponent<RectTransform>();
                CenterAnchors(rt);
                rt.sizeDelta = new Vector2(560f, h);
                rt.anchoredPosition = childPos;
                _rowObjects.Add(rowGo);

                // Optionaler Zeilen-Hintergrund (z. B. blauer Balken für den aktiven Spieler)
                if (_rows[i].Background.HasValue)
                    MakeRounded(rowGo.transform, _rows[i].Background.Value, new Vector2(556f, h - 4f), Vector2.zero);

                if (_rows[i].MultiControl) _rowValueTexts[i] = BuildControlRow(rowGo.transform, _rows[i], h);
                else if (_rows[i].Button) BuildButtonRow(rowGo.transform, _rows[i], h);
                else if (_rows[i].RightValue) _rowValueTexts[i] = BuildRightValueRow(rowGo.transform, _rows[i], h);
                else if (_scrollable) BuildSongRow(rowGo, i, h); // beschnitten + Laufschrift
                else BuildTextRow(rowGo.transform, _rows[i], isTitle, h);

                y -= h;
            }

            _panel.sizeDelta = new Vector2(580f, totalH + 16f + extra);
            _panel.pivot = new Vector2(0.5f, 1f); // Oberkante am Anker -> keine leere Fläche oben
            // Cursor zuletzt (halbtransparent, bleibt lesbar), Bedienfeld-Markierung darüber.
            if (_cursorHighlight != null) _cursorHighlight.transform.SetAsLastSibling();
            if (_controlHighlight != null) _controlHighlight.transform.SetAsLastSibling();
            if (_songDeleteIcon != null) _songDeleteIcon.SetAsLastSibling();
            RefreshCursorPosition();
            UpdateControlHighlight();
        }

        /// <summary>Normale Textzeile (auch Titel).</summary>
        private void BuildTextRow(Transform row, Row r, bool isTitle, float h)
        {
            if (isTitle)
                MakeRounded(row, BsUi.TitleBarColor, new Vector2(556f, h - 6f), Vector2.zero);

            var text = MakeText(row, r.Text,
                isTitle ? 56 : (r.FontSize > 0 ? r.FontSize : 48),
                isTitle ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft,
                RowTextColor(r, isTitle),
                new Vector2(540f, h), new Vector2(8f, 0f));
            text.fontStyle = isTitle ? FontStyle.Bold : FontStyle.Normal;
        }

        /// <summary>
        /// Schaltfläche: dezente graue Hinterlegung wie bei den Bedienkarten (Audio-Offset),
        /// damit die Zeile schon VOR dem Hover als Schaltfläche lesbar ist, ohne aufzufallen.
        /// </summary>
        private void BuildButtonRow(Transform row, Row r, float h)
        {
            MakeRounded(row, BsUi.RowColor, new Vector2(548f, h - 6f), Vector2.zero);

            var text = MakeText(row, r.Text, r.FontSize > 0 ? r.FontSize : 46,
                TextAnchor.MiddleCenter,
                r.Selectable ? Color.white : new Color(0.6f, 0.62f, 0.7f),
                new Vector2(520f, h - 8f), Vector2.zero);
            text.fontStyle = FontStyle.Bold;
        }

        /// <summary>
        /// Name linksbündig, Wert (z. B. Punkte oder Ein/Aus-Status) rechtsbündig.
        /// Mit <see cref="Row.Card"/> zusätzlich dezent grau hinterlegt.
        /// </summary>
        private Text BuildRightValueRow(Transform row, Row r, float h)
        {
            if (r.Card)
                MakeRounded(row, BsUi.RowColor, new Vector2(548f, h - 6f), Vector2.zero);

            MakeText(row, r.Text, r.FontSize > 0 ? r.FontSize : 46, TextAnchor.MiddleLeft,
                RowTextColor(r, false), new Vector2(360f, h), new Vector2(-95f, 0f));
            return MakeText(row, r.Value ?? "", r.FontSize > 0 ? r.FontSize : 46, TextAnchor.MiddleRight,
                r.Selectable ? Color.white : new Color(0.68f, 0.70f, 0.78f),
                new Vector2(180f, h), new Vector2(180f, 0f));
        }

        /// <summary>
        /// Liedzeile der Songliste: Text wird auf den LINKEN Zeilenteil beschnitten
        /// (RectMask2D auf einem nach links versetzten Kind), damit lange Namen weder über
        /// den Fensterrand laufen NOCH das Lösch-„X" ganz rechts erreichen. Läuft bei Bedarf
        /// als Laufschrift durch (siehe <see cref="UpdateMarquee"/>).
        /// </summary>
        private void BuildSongRow(GameObject rowGo, int contentIndex, float h)
        {
            // Clip-Container (Maske) nach links versetzt -> der rechte Zeilenteil bleibt
            // für das Lösch-„X" frei.
            var clipGo = new GameObject("Clip");
            clipGo.transform.SetParent(rowGo.transform, false);
            var clipRt = clipGo.AddComponent<RectTransform>();
            CenterAnchors(clipRt);
            clipRt.sizeDelta = new Vector2(SongTextWidth, h);
            clipRt.anchoredPosition = new Vector2(SongTextClipX, 0f);
            clipGo.AddComponent<RectMask2D>(); // schneidet überlaufende Namen am Clip-Rand ab

            var t = MakeText(clipGo.transform, _rows[contentIndex].Text,
                _rows[contentIndex].FontSize > 0 ? _rows[contentIndex].FontSize : 48,
                TextAnchor.MiddleLeft, RowTextColor(_rows[contentIndex], false),
                new Vector2(SongTextWidth, h), Vector2.zero);
            _songTextRefs.Add(new SongTextRef { Index = contentIndex, Text = t, Overflow = 0f, Measured = false });
        }

        private static Color RowTextColor(Row r, bool isTitle)
        {
            if (r.Selectable && !isTitle && !string.IsNullOrEmpty(r.Text) && r.Text.Contains("★"))
                return new Color(1f, 0.9f, 0.4f); // Top-Scorer in Gold
            return r.Selectable ? Color.white : new Color(0.68f, 0.70f, 0.78f);
        }

        /// <summary>
        /// Bedienkarte: Name MITTIG, darunter links/rechts je ein sichtbares Bedienfeld
        /// (−/◄ bzw. +/►) und der aktuelle Wert mittig.
        /// </summary>
        private Text BuildControlRow(Transform row, Row r, float h)
        {
            MakeRounded(row, BsUi.RowColor, new Vector2(556f, h - 6f), Vector2.zero);

            MakeText(row, r.Text, 30, TextAnchor.MiddleCenter, BsUi.LabelColor,
                new Vector2(520f, 30f), new Vector2(0f, 22f));
            var value = MakeText(row, r.Value ?? "", 44, TextAnchor.MiddleCenter, BsUi.ValueColor,
                new Vector2(230f, 46f), new Vector2(0f, ControlChipY));

            string leftGlyph = r.Cycle ? "◄" : "−";
            string rightGlyph = r.Cycle ? "►" : "+";
            MakeChip(row, leftGlyph, new Vector2(-ControlChipX, ControlChipY));
            MakeChip(row, rightGlyph, new Vector2(ControlChipX, ControlChipY));
            return value;
        }

        private static void MakeChip(Transform row, string glyph, Vector2 pos)
        {
            var chip = MakeRounded(row, BsUi.ChipColor, ControlChipSize, pos);
            MakeText(chip.transform, glyph, 40, TextAnchor.MiddleCenter, BsUi.AccentColor,
                ControlChipSize, Vector2.zero);
        }

        private void RefreshCursorPosition()
        {
            if (_cursorHighlight == null) return;
            if (_rows.Count == 0)
            {
                _cursorHighlight.enabled = false;
                RefreshSongDeleteIcon();
                return;
            }

            // Songliste: KEINE dauerhafte Auswahl. Nur die Zeile unter dem Laser wird grün
            // hervorgehoben; zeigt der Laser auf kein Lied, ist auch keine Zeile markiert.
            if (_scrollable)
            {
                bool show = !_confirmVisible && _hoverRow >= 0
                    && _hoverRow < _rowHeights.Length && _rowHeights[_hoverRow] > 0f;
                _cursorHighlight.enabled = show;
                if (show)
                {
                    _cursorHighlight.rectTransform.sizeDelta =
                        new Vector2(560f, Mathf.Max(24f, _rowHeights[_hoverRow] - 10f));
                    _cursorHighlight.rectTransform.anchoredPosition =
                        new Vector2(0f, _rowCenterY[_hoverRow] + _panel.sizeDelta.y * 0.5f);
                }
                RefreshSongDeleteIcon(); // „X" an die Zeile unter dem Laser setzen
                return;
            }

            _cursorHighlight.enabled = !_confirmVisible;
            RefreshSongDeleteIcon(); // keine Liedliste -> „X" ausblenden
            int selectableSeen = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!_rows[i].Selectable) continue;
                if (selectableSeen == Cursor)
                {
                    if (i >= _rowCenterY.Length) return;
                    _cursorHighlight.rectTransform.sizeDelta =
                        new Vector2(560f, Mathf.Max(24f, _rowHeights[i] - 10f));
                    _cursorHighlight.rectTransform.anchoredPosition =
                        new Vector2(0f, _rowCenterY[i] + _panel.sizeDelta.y * 0.5f);
                    return;
                }
                selectableSeen++;
            }
        }

        /// <summary>
        /// Bewegt das Songlisten-Fenster um `dir` Einträge, ZYKLISCH (kein Ende; die Lieder
        /// wiederholen sich). Danach wird die Zeile unter dem (ruhenden) Laser neu bestimmt.
        /// </summary>
        private void ScrollBy(int dir)
        {
            if (!_scrollable || _rows.Count <= 1 || dir == 0) return;
            _scrollOffset = Mod(_scrollOffset + dir, _rows.Count);
            _marqueeTimer = 0f; // Laufschrift nach dem Blättern neu starten
            RefreshGeometry();
            if (_hasPointerLocal) RowHitScan(_lastPointerLocal);
            else if (_hoverRow != -1) { _hoverRow = -1; RefreshCursorPosition(); }
        }

        /// <summary>
        /// Zeige-Laser: Strahl von der anderen Hand auf die Menü-Tafel werfen; ein Treffer
        /// bewegt den Cursor direkt zur gezeigten Zeile ("Über-Zeigen"-Navigation).
        /// Wird jeden Frame vom GameManager gerufen, solange das Menü offen ist.
        /// </summary>
        public void UpdatePointer(Vector3 origin, Vector3 direction)
        {
            if (_laser == null) return;
            if (!Visible || _panel == null)
            {
                SetLaserVisible(false);
                if (_reticle != null) _reticle.enabled = false;
                return;
            }

            Vector3 dirN = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 end = origin + dirN * 2f;
            var ray = new Ray(origin, dirN);

            // Strahl erst zeigen, wenn er die Tafel wirklich trifft (Referenz: SuperSaber
            // cursor-laser) — kein dauerhaft ins Leere zeigender Beam.
            bool laserOn = false;

            var plane = new Plane(transform.forward, transform.position);
            if (plane.Raycast(ray, out float enter) && enter >= 0f)
            {
                Vector3 hit = ray.GetPoint(enter);
                Vector3 local = _panel.InverseTransformPoint(hit);

                // Panel reicht vom Anker (0) nach unten (-sizeDelta.y). Der Treffbereich folgt.
                bool inPanel = Mathf.Abs(local.x) <= 340f
                               && local.y <= 8f
                               && local.y >= -_panel.sizeDelta.y;
                if (inPanel)
                {
                    laserOn = true;
                    _lastPointerX = local.x; // für (−)/(+)-Zeilen: links = Minus, rechts = Plus
                    _lastPointerLocal = local;
                    _hasPointerLocal = true;
                    end = hit;
                    if (_reticle != null)
                    {
                        _reticle.enabled = true;
                        _reticle.rectTransform.position = hit;
                    }

                    if (_confirmVisible)
                    {
                        // Modaler Dialog: nur Ja/Nein markieren; das Menü dahinter ist eingefroren.
                        _pointerOnSongDelete = false;
                        UpdateConfirmPointer(local);
                    }
                    else
                    {
                        // Songliste: Zeile unter dem Laser bestimmen (KEINE dauerhafte Auswahl).
                        // Andere Menüs: Auswahl-Cursor auf die Zeile setzen. Steht der Laser
                        // direkt auf dem Lösch-„X", wird dieses hell hervorgehoben.
                        RowHitScan(local);
                        _pointerOnSongDelete = HitSongDeleteIcon(local);
                        UpdateControlHighlight();
                        RefreshSongDeleteIcon();
                    }
                }
                else
                {
                    _hasPointerLocal = false;
                    _pointerOnSongDelete = false;
                    ClearHover();
                    if (_reticle != null) _reticle.enabled = false;
                }
            }
            else
            {
                _hasPointerLocal = false;
                _pointerOnSongDelete = false;
                ClearHover();
                if (_reticle != null) _reticle.enabled = false;
            }

            SetLaserVisible(laserOn);
            if (laserOn)
            {
                _laser.SetPosition(0, origin);
                _laser.SetPosition(1, end);
            }
        }

        /// <summary>
        /// Laser nur zeigen, wenn er die Menü-Tafel wirklich trifft — und nur bei offenem
        /// Menü (Referenz: SuperSaber `cursor-laser.js`).
        /// </summary>
        private void SetLaserVisible(bool on)
        {
            if (_laser == null) return;
            bool show = on && Visible && _canvas != null && _canvas.enabled;
            if (_laser.gameObject.activeSelf != show) _laser.gameObject.SetActive(show);
            _laser.enabled = show;
        }

        /// <summary>
        /// Songliste: Zeile unter dem Laser bestimmen (keine dauerhafte Auswahl). Andere
        /// Menüs: Auswahl-Cursor auf die Zeile unter dem Laser setzen.
        /// </summary>
        private void RowHitScan(Vector2 local)
        {
            if (_scrollable)
            {
                int hover = HitRowAt(local);
                if (hover != _hoverRow)
                {
                    _hoverRow = hover;
                    _marqueeTimer = 0f; // Laufschrift neu starten
                    RefreshCursorPosition();
                }
                return;
            }

            int selectableSeen = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                bool inY = i < _rowCenterY.Length
                    && Mathf.Abs(local.y - _rowCenterY[i]) <= _rowHeights[i] * 0.5f;
                if (inY)
                {
                    if (_rows[i].Selectable && selectableSeen != Cursor)
                    {
                        Cursor = selectableSeen;
                        RefreshCursorPosition();
                    }
                    break;
                }
                if (_rows[i].Selectable) selectableSeen++;
            }
        }

        /// <summary>Inhalt-Zeilenindex unter dem Laser in der Songliste (zyklisches Fenster), sonst -1.</summary>
        private int HitRowAt(Vector2 local)
        {
            if (_rows.Count == 0) return -1;
            int n = Mathf.Min(_viewportRows, _rows.Count);
            for (int p = 0; p < n; p++)
            {
                int i = Mod(_scrollOffset + p, _rows.Count);
                if (i < _rowHeights.Length && _rowHeights[i] > 0f
                    && Mathf.Abs(local.y - _rowCenterY[i]) <= _rowHeights[i] * 0.5f)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>Songliste: grüne Hover-Markierung entfernen (Laser zeigt auf kein Lied).</summary>
        private void ClearHover()
        {
            _pointerOnSongDelete = false;
            if (_scrollable && _hoverRow != -1)
            {
                _hoverRow = -1;
                _marqueeTimer = 0f;
                RefreshCursorPosition();
            }
            else
            {
                RefreshSongDeleteIcon(); // „X" ausblenden (Laser verlässt die Tafel/Zeile)
            }
        }

        /// <summary>
        /// Songliste: setzt das Lösch-„X" an den rechten Rand der Zeile unter dem Laser
        /// (nur bei löschbaren Zeilen) und färbt es DUNKEL/HELLrot, je nachdem ob der Laser
        /// direkt darauf steht. Ausgeblendet, solange der Bestätigungs-Dialog offen ist oder
        /// keine löschbare Zeile anvisiert wird.
        /// </summary>
        private void RefreshSongDeleteIcon()
        {
            if (_songDeleteIcon == null) return;
            bool show = _scrollable && !_confirmVisible && _hoverRow >= 0
                        && _hoverRow < _rows.Count && _rows[_hoverRow].Deletable
                        && _hoverRow < _rowHeights.Length && _rowHeights[_hoverRow] > 0f;
            if (_songDeleteIcon.gameObject.activeSelf != show) _songDeleteIcon.gameObject.SetActive(show);
            if (!show) return;
            _songDeleteIcon.anchoredPosition =
                new Vector2(SongDeleteCenterX, _rowCenterY[_hoverRow] + _panel.sizeDelta.y * 0.5f);
            Color c = _pointerOnSongDelete ? SongDeleteHot : SongDeleteDim;
            if (_songDeleteBarA != null) _songDeleteBarA.color = c;
            if (_songDeleteBarB != null) _songDeleteBarB.color = c;
        }

        /// <summary>True, wenn der Laser-Punkt (Tafel-Lokal) auf dem Lösch-„X" der anvisierten
        /// Liedzeile liegt (gleiche Geometrie wie das gezeichnete Symbol — Anzeige und Treffer
        /// laufen nie auseinander).</summary>
        private bool HitSongDeleteIcon(Vector2 local)
        {
            if (!_scrollable || _confirmVisible || _hoverRow < 0 || _hoverRow >= _rows.Count
                || !_rows[_hoverRow].Deletable
                || _hoverRow >= _rowHeights.Length || _rowHeights[_hoverRow] <= 0f) return false;
            // Das Symbol wird bei anchoredPosition.y = rowCenterY + panelH/2 gezeichnet, also
            // auf Tafel-Lokal-y = rowCenterY (vom oberen Pivot gemessen) — genau `local.y`.
            // Die Balken reichen ~±15 px über die Mitte hinaus (41,6 px bei 45°).
            return Mathf.Abs(local.x - SongDeleteCenterX) <= SongDeleteVisualRadius
                && Mathf.Abs(local.y - _rowCenterY[_hoverRow]) <= SongDeleteVisualRadius;
        }

        private void LateUpdate()
        {
            if (!Visible || _handAnchor == null || _cam == null) return;

            // Billboards zum Headset (NICHT in Richtung der anderen Hand): die Tafel darf sich
            // nicht mit der Pointer-Hand mitdrehen. Nur leicht nach hinten gekippt.
            Vector3 toHeadset = _handAnchor.position - _cam.transform.position;
            if (toHeadset.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.LookRotation(toHeadset.normalized, Vector3.up)
                                 * Quaternion.Euler(MenuTiltDeg, 0f, 0f);

            // Feste Unterkante: Die Tafel-UNTERKANTE liegt um `MenuBottomClearance` über dem
            // Controller (in Blickrichtung um `MenuForwardOffset` weiter weg). Der Pivot ist die
            // Oberkante, also die volle Panelhöhe entlang der Tafel-„Hoch"-Achse aufschlagen.
            float panelH = (_panel != null ? _panel.sizeDelta.y : 0f) * Mathf.Abs(transform.lossyScale.y);
            Vector3 bottom = _handAnchor.position
                             + Vector3.up * MenuBottomClearance
                             + _cam.transform.forward * MenuForwardOffset;
            transform.position = bottom + transform.up * panelH;

            // Dialog (eigenes Overlay-Canvas) folgt der Tafel und bleibt mittig darüber.
            if (_confirmVisible) PlaceConfirmOverPanel();
        }
    }
}
