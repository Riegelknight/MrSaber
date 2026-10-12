// GameManager: Treffer-Erkennung, Arc-Interaktion, Ketten.
using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace BeatSaber
{
    public sealed partial class GameManager
    {

        /// <summary>
        /// Prüft pro Frame alle aktiven Noten gegen das ZUSTÄNDIGE Schwert
        /// (Farb-Layer-Trennung nach OpenSaberPlus: Rot→links, Blau→rechts,
        /// Bomben→beide) auf AABB-Überlappung. Allokationsfrei. Läuft im
        /// LATEUPDATE nach <see cref="ApplyRoomAnchor"/>.
        /// </summary>
        private void GeometricHitUpdate()
        {
            // WICHTIG: Auch nur mit aktiven Chain-Gliedern (ohne Noten) prüfen — sonst
            // würden Ketten nach dem Treffer der Kopfnote nicht mehr gewertet.
            if (_activeNotes.Count == 0 && _activeChains.Count == 0) return;

            for (int i = _activeNotes.Count - 1; i >= 0; i--)
            {
                var note = _activeNotes[i];
                if (note == null || note.IsConsumed) continue; // Ausdünnen macht PruneActiveNotes

                // FARB-LAYER-TRENNUNG (OpenSaberPlus `LightSaber.gd`: Maske pro
                // Schwertfarbe + Bomben): das LINKE Schwert kann NOTEN nur treffen,
                // wenn sie ROT sind, das RECHTE nur bei BLAU; Bomben = beide.
                // Kein Schwert kann der anderen Farbe die Note „stehlen", egal wie
                // nahe zwei verschieden-farbige Noten nebeneinander liegen. Ein Bad-Cut
                // durch falsche Hand ist konstruktionsbedingt ausgeschlossen; die
                // Farb-/Richtungsprüfung in NoteView.TrySlice bleibt als Sicherheitsnetz.
                if (note.Kind == NoteKind.Bomb)
                {
                    if (TryGeometricHit(_saberL, note)) continue;
                    TryGeometricHit(_saberR, note);
                }
                else
                {
                    SaberView own = note.Kind == NoteKind.Red ? _saberL : _saberR;
                    TryGeometricHit(own, note);
                }
            }

            // Chain-Glieder werden mit demselben Klingen-Segment-Test geprüft.
            UpdateChainHits();
        }

        /// <summary>
        /// Arc-Interaktion (1:1 Idee aus dem Original `SliderInteractionManager`): Für jeden
        /// aktiven Bogen wird geprüft, wie nah die farblich zuständige Saber-Klinge (rot→links,
        /// blau→rechts) an der Bogen-Kurve ist. Ergebnis steuert Bogen-Helligkeit, Haptik und
        /// Spitzen-Partikel. Nur Feedback — Punkte geben weiterhin die Kopf-/Endnoten.
        /// Allokationsfrei.
        /// </summary>
        private void UpdateArcInteraction()
        {
            float radius = GameConfig.ArcInteractRadius * _roomScale;
            float pLeft = 0f, pRight = 0f;

            for (int i = 0; i < _activeArcs.Count; i++)
            {
                var arc = _activeArcs[i];
                if (arc == null || !arc.gameObject.activeSelf) continue;

                SaberView saber = arc.ColorType == 0 ? _saberL : _saberR;
                if (saber == null || saber.Retracted || saber.Tip == null || saber.BaseTrans == null)
                {
                    arc.SetInteraction(0f);
                    continue;
                }

                float p = arc.EvaluateInteraction(saber.BaseTrans.position, saber.Tip.position, radius);
                arc.SetInteraction(p);
                if (arc.ColorType == 0) pLeft = Mathf.Max(pLeft, p);
                else pRight = Mathf.Max(pRight, p);
            }

            ArcInteractionFeedback(SaberSide.Left, pLeft);
            ArcInteractionFeedback(SaberSide.Right, pRight);
        }

        /// <summary>Haptik + Spitzen-Partikel, solange die Klinge im Bogen ist (gedrosselt).</summary>
        private void ArcInteractionFeedback(SaberSide side, float param)
        {
            int idx = (int)side;
            var saber = SaberFor(side);
            if (param <= 0.001f || saber == null)
            {
                _arcHapticNext[idx] = 0f;
                _arcParticleNext[idx] = 0f;
                return;
            }

            float now = Time.time;
            if (now >= _arcHapticNext[idx])
            {
                _arcHapticNext[idx] = now + GameConfig.HapticContactInterval;
                Haptics(side, GameConfig.HapticLightAmplitude * (0.6f + 0.4f * param),
                    GameConfig.HapticContactPulseDuration);
            }
        }

        /// <summary>
        /// Prüft alle offenen Glieder aller aktiven Ketten gegen das farblich zuständige
        /// Schwert (AABB-Überlappung wie bei Noten). Ein Treffer gibt fest
        /// <see cref="GameConfig.ChainLinkScore"/> × Multiplikator (ArcViewer: ChainLink = 20,
        /// ohne Winkel-/Mitte-Anteil). Verpasste Glieder brechen die Combo nicht.
        /// </summary>
        private void UpdateChainHits()
        {
            if (_activeChains.Count == 0) return;
            for (int c = 0; c < _activeChains.Count; c++)
            {
                var chain = _activeChains[c];
                if (chain == null || chain.Expired || !chain.gameObject.activeSelf) continue;
                SaberView own = chain.ColorType == 0 ? _saberL : _saberR;
                if (own == null || own.Retracted || own.Tip == null || own.BaseTrans == null
                    || !own.HasCurrentPose) continue;

                int n = chain.LinkCount;
                for (int i = 0; i < n; i++)
                {
                    if (chain.IsLinkHit(i)) continue;
                    Transform lt = chain.LinkAt(i);
                    if (lt == null || !lt.gameObject.activeSelf) continue;
                    // Trefferbox = Referenz-Chain-Box (Web-Replays `toBigBox` isChain /
                    // OpenSaberPlus CollisionBig): 0,80 × 0,20 × 1,00, um das Glied zentriert
                    // — plus Klingenstärke.
                    Vector3 half = new Vector3(
                        GameConfig.NoteHitboxWidth * 0.5f,
                        GameConfig.NoteHitboxHeightChain * 0.5f,
                        GameConfig.NoteHitboxDepth * 0.5f);
                    half += Vector3.one * GameConfig.HitBoxPadding;
                    Bounds linkBox = BoxWorldBounds(lt, new Bounds(Vector3.zero, half * 2f));
                    if (!SaberBladeHitsBox(own, linkBox)) continue;
                    // Glied optisch zerschneiden (EzySlice, BeatSaberClone-Optik) + Punkte.
                    Vector3 bladeDir = own.GetBladeDirection();
                    Vector3 planeNormal = Vector3.Cross(bladeDir, own.TipVelocity);
                    if (planeNormal.sqrMagnitude < 0.0001f) planeNormal = Vector3.up;
                    else planeNormal.Normalize();
                    chain.SliceLink(i, linkBox.center, planeNormal, own.CrossSectionMaterial, _roomScale);
                    AwardChainLink(lt.position);
                }
            }
        }

        /// <summary>Vergibt die festen Chain-Glied-Punkte (20 × Multiplikator) und zählt sie
        /// wie eine Note zur Combo/zum Multiplikator-Fortschritt.</summary>
        private void AwardChainLink(Vector3 worldPosition)
        {
            int baseScore = Mathf.RoundToInt(GameConfig.ChainLinkScore);
            _combo++;
            if (_multTier < GameConfig.ComboMultipliers.Length - 1)
            {
                _comboProgress++;
                if (_comboProgress >= GameConfig.HitsNeededForComboIncrease[_multTier])
                {
                    _multTier++;
                    _comboProgress = 0;
                }
            }
            int multiplier = GameConfig.ComboMultipliers[_multTier];
            _score += (long)baseScore * multiplier;
            UpdateHud();
            TrySaveBestImmediately();
            ShowSliceScore(worldPosition, baseScore);
        }

        /// <summary>
        /// Prüft die KLINGE des laufenden Frames — Segment Basis→Spitze, inklusive
        /// VORFRAME-Sweep (interpolierte Zwischen-Posen; nur bei frischer Historie, damit
        /// Pause/Menü keine Sprünge erzeugt) — gegen eine Box. Die Klinge wird NICHT
        /// verdickt/verlängert; allein die Box-Größe bestimmt die Treffer-Toleranz.
        /// </summary>
        private static bool SaberBladeHitsBox(SaberView saber, Bounds box) =>
            SaberBladeHitsBox(saber, box, out _, out _);

        /// <summary>Wie <see cref="SaberBladeHitsBox(SaberView, Bounds)"/>, liefert aber
        /// zusätzlich das TREFFER-Segment (für den Schnittpunkt der Optik).</summary>
        private static bool SaberBladeHitsBox(SaberView saber, Bounds box,
            out Vector3 segA, out Vector3 segB)
        {
            Vector3 b = saber.CurrentBasePosition;
            Vector3 t = saber.CurrentTipPosition;
            Vector3 pb = b, pt = t;
            bool sweep = saber.HasPreviousPose && saber.SampleTimeDelta > 0.0001f
                         && saber.SampleTimeDelta < 0.25f;
            if (sweep)
            {
                pb = saber.PreviousBasePosition;
                pt = saber.PreviousTipPosition;
            }

            int n = sweep ? Mathf.Max(1, GameConfig.SaberSweepSamples) : 1;
            for (int k = 1; k <= n; k++)
            {
                float f = (float)k / n;
                segA = Vector3.Lerp(pb, b, f);
                segB = Vector3.Lerp(pt, t, f);
                if (SegmentIntersectsBounds(segA, segB, box)) return true;
            }
            segA = segB = Vector3.zero;
            return false;
        }

        private void PruneActiveNotes()
        {
            for (int i = _activeNotes.Count - 1; i >= 0; i--)
            {
                var note = _activeNotes[i];
                if (note == null || note.IsConsumed) _activeNotes.RemoveAt(i);
            }
        }

        /// <summary>
        /// Eine Note gegen die KLINGE: das Klingen-SEGMENT (Basis→Spitze) des laufenden
        /// Frames wird — siehe <see cref="SaberBladeHitsBox"/> — von der VORFRAME-Pose
        /// interpoliert (Sweep) und gegen die (orientierte) Noten-Box getestet
        /// (<see cref="SegmentIntersectsBounds"/>: Segment/Box-Slab-Test).
        ///
        /// Warum NICHT AABB-gegen-AABB (SuperSaber `checkCollisions`): die Achsen-Box eines
        /// schnellen Schwungs umfasst die GANZE Bogenfläche. Sie „trifft" dadurch Noten, die
        /// die Klinge nie berührt — Treffer registrieren sich dann weit vor der Zone und mit
        /// falscher Blickrichtung, und eine falsch angeschnittene Note kann später trotzdem
        /// sauber zählen. Der Segment-Test trifft nur, was die Klinge WIRKLICH schneidet
        /// (präzise wie Beat Saber/Web-Replays), ohne Tunneling: der Sweep deckt die
        /// Bogenbahn zwischen zwei Frames ab. <see cref="GameConfig.HitGateForward"/> bleibt
        /// als billiger Vorabschnitt für viel zu weit entfernte Noten.
        /// </summary>
        private bool TryGeometricHit(SaberView saber, NoteView note)
        {
            if (saber == null || saber.Retracted || saber.Tip == null || saber.BaseTrans == null
                || !saber.HasCurrentPose) return false;

            if (note.transform.localPosition.z > GameConfig.HitGateForward) return false;

            Bounds noteBox = NoteWorldBounds(note);
            if (!SaberBladeHitsBox(saber, noteBox, out Vector3 winA, out Vector3 winQ)) return false;

            // Schnittpunkt für Optik/Mitte: der Punkt der Klinge, der der Notenmitte am
            // nächsten liegt (die Klinge liegt in der Schnittebene -> exakt auf dem Schnitt).
            Vector3 point = ClosestPointOnSegment(winA, winQ, note.transform.position);

            Vector3 bladeDir = saber.GetBladeDirection();
            // Schneide-Geschwindigkeit: geglättete Spitze (Clone `ObjectSlicer.Velocity`-
            // Prinzip, 5-Frame-Mittel — hier an der SPITZE, bei Handgelenksschwüngen ist
            // die Wurzel zu bewegungsarm). Ohne Schwung: zero -> TrySlice: Berührung.
            Vector3 velocity = saber.TipVelocity;
            Vector3 planeNormal = Vector3.Cross(bladeDir, velocity);
            if (planeNormal.sqrMagnitude < 0.0001f) planeNormal = Vector3.up;
            else planeNormal.Normalize();

            HandleSaberHit(saber, note, point, planeNormal, velocity);
            return true;
        }

        /// <summary>
        /// WORLD-AABB der Trefferbox einer Note = Referenz-Kollisionsbox (Web-Replays `toBigBox`
        /// / OpenSaberPlus CollisionBig): 0,80 × 0,50 × 1,00, Dot-Noten 0,80 hoch — um die
        /// Notenmitte ZENTRIERT (kein Vorversatz; die Score-Zone liegt exakt auf der Note).
        /// Bewusst GRÖSSER als das sichtbare 0,5er-Mesh: das gibt dieselbe Treffer-Toleranz wie
        /// Beat Saber / die v1.0-Referenz. Die Rotation steckt in <see cref="BoxWorldBounds"/>
        /// (45°-Noten bekommen so ihre größere AABB).
        /// </summary>
        private static Bounds NoteWorldBounds(NoteView note)
        {
            float h = note.CutDirection >= 8 ? GameConfig.NoteHitboxHeightDot : GameConfig.NoteHitboxHeight;
            Vector3 half = new Vector3(
                GameConfig.NoteHitboxWidth * 0.5f, h * 0.5f, GameConfig.NoteHitboxDepth * 0.5f);
            // Klingenstärke als Toleranz (die Klinge wird als Mittellinie getestet).
            half += Vector3.one * GameConfig.HitBoxPadding;
            return BoxWorldBounds(note.transform, new Bounds(Vector3.zero, half * 2f));
        }

        /// <summary>
        /// WORLD-AABB eines lokalen Quaders inklusive Rotation und Raum-Skalierung (OBB→AABB über
        /// die Matrix-Kolumnen, entspricht `Box3.setFromObject`). Allokationsfrei.
        /// </summary>
        private static Bounds BoxWorldBounds(Transform t, Bounds local)
        {
            Matrix4x4 m = Matrix4x4.TRS(Vector3.zero, t.rotation, t.lossyScale);
            Vector3 h = local.extents;
            // Unity 2022 hat kein Vector3.Abs — komponentenweises Betragen.
            Vector3 ax = AbsComponents(m.MultiplyVector(new Vector3(h.x, 0f, 0f)));
            Vector3 ay = AbsComponents(m.MultiplyVector(new Vector3(0f, h.y, 0f)));
            Vector3 az = AbsComponents(m.MultiplyVector(new Vector3(0f, 0f, h.z)));
            Vector3 center = t.position + m.MultiplyPoint3x4(local.center);
            return new Bounds(center, (ax + ay + az) * 2f);
        }

        private static Vector3 AbsComponents(Vector3 v) =>
            new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        /// <summary>
        /// Schneidet das Segment <paramref name="p0"/>→<paramref name="p1"/> die
        /// achsenparallele Box? Klassischer Slab-Test (Ericson, Real-Time Collision
        /// Detection) mit t ∈ [0,1]. Startet das Segment innerhalb der Box, gilt es als
        /// Treffer. Allokationsfrei.
        /// </summary>
        private static bool SegmentIntersectsBounds(Vector3 p0, Vector3 p1, Bounds box)
        {
            Vector3 o = p0 - box.center;
            Vector3 d = p1 - p0;
            Vector3 h = box.extents;
            float tmin = 0f, tmax = 1f;
            for (int axis = 0; axis < 3; axis++)
            {
                float origin = o[axis];
                float dir = d[axis];
                float ext = h[axis];
                if (Mathf.Abs(dir) < 1e-9f)
                {
                    // Parallel zur Slab-Ebene: nur treffend, wenn der Ursprung drinliegt.
                    if (origin < -ext || origin > ext) return false;
                    continue;
                }
                float inv = 1f / dir;
                float t1 = (-ext - origin) * inv;
                float t2 = (ext - origin) * inv;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmin > tmax) return false;
            }
            return true;
        }

        /// <summary>Punkt auf dem Segment <paramref name="a"/>→<paramref name="b"/>, der
        /// <paramref name="p"/> am nächsten liegt (Klinge beim Schnitt). Allokationsfrei.</summary>
        private static Vector3 ClosestPointOnSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-12f) return a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return a + ab * t;
        }

        private void Haptics(SaberSide side, float amplitude, float duration)
        {
            float amp = Mathf.Clamp01(amplitude);
            if (amp <= 0f || duration <= 0f) return;

            if (Application.platform == RuntimePlatform.Android && PicoHaptics(side, amp, duration))
                return;

            UnityHaptics(side, amp, duration);
        }

        /// <summary>Nativer PICO-Impuls (Amplitude 0..1, Dauer in ms; SDK: 0..65535 ms).
        /// Der zuverlässigste Weg auf der PICO 4. Auf Nicht-Android würde der native Aufruf
        /// werfen — deshalb ruft <see cref="Haptics"/> ihn nur bei Android aus; der
        /// try/catch bleibt als Sicherheitsnetz.</summary>
        private static bool PicoHaptics(SaberSide side, float amplitude, float duration)
        {
            try
            {
                var vibrate = side == SaberSide.Left
                    ? Unity.XR.PXR.PXR_Input.VibrateType.LeftController
                    : Unity.XR.PXR.PXR_Input.VibrateType.RightController;
                int ms = Mathf.Clamp(Mathf.RoundToInt(duration * 1000f), 1, 65535);
                Unity.XR.PXR.PXR_Input.SendHapticImpulse(vibrate, amplitude, ms);
                return true;
            }
            catch { return false; }
        }

        /// <summary>Unity-XR-Impuls (Editor/Fallback) über den gecachten Controller-Device.
        /// Bewusst OHNE `supportsImpulse`-Vorprüfung — die PICO meldet die Fähigkeit nicht
        /// zuverlässig; `SendHapticImpulse` liefert den Erfolg selbst zurück.</summary>
        private bool UnityHaptics(SaberSide side, float amplitude, float duration)
        {
            try
            {
                if (!_devices.TryGetValue(side, out var device) || !device.isValid)
                {
                    _scratchDevices.Clear(); // wiederverwendete Liste (keine Allokation)
                    InputDevices.GetDevicesAtXRNode(
                        side == SaberSide.Left ? XRNode.LeftHand : XRNode.RightHand, _scratchDevices);
                    if (_scratchDevices.Count > 0)
                    {
                        device = _scratchDevices[0];
                        _devices[side] = device;
                    }
                }
                if (!device.isValid) return false;
                return device.SendHapticImpulse(0u, amplitude, duration);
            }
            catch { return false; }
        }

        /// <summary>
        /// Minen (Bomben): mittelstarker Hauptimpuls, danach eine kurze, schwächere
        /// Nachvibration (unscaled, damit sie auch bei angehaltener Zeit sicher feuert).
        /// </summary>
        private IEnumerator MineHapticsRoutine(SaberSide side)
        {
            Haptics(side, GameConfig.HapticMineAmplitude, GameConfig.HapticMineDuration);
            yield return new WaitForSecondsRealtime(GameConfig.HapticMineAfterDelay);
            Haptics(side, GameConfig.HapticMineAfterAmplitude, GameConfig.HapticMineAfterDuration);
        }

        /// <summary>
        /// Kontakt-Haptik: Solange die Klinge ein anderes Objekt berührt (Schwert↔Schwert
        /// oder Schwert↔Wand), wird eine LEICHTE Vibration gesendet. Pro Hand gedrosselt
        /// (HapticContactInterval), damit der Hot Path allokationsfrei bleibt und die
        /// Impulse nicht jeden Frame gesendet werden. Bei Ende des Kontakts wird der
        /// Timer zurückgesetzt, damit ein neuer Kontakt sofort spürbar ist.
        /// </summary>
        private void UpdateContactHaptics()
        {
            bool leftTouch = false, rightTouch = false;
            bool saberClash = false;
            Vector3 clashPos = Vector3.zero;

            if (IsBladeReady(_saberL) && IsBladeReady(_saberR))
            {
                // Absicherung: Solange die Controller noch nicht getrackt sind, säßen beide
                // Schwerter am Ursprung und würden fälschlich Dauerkontakt melden. Echte
                // Hände sind immer deutlich weiter auseinander als dieser Mindestabstand.
                float handDist = Vector3.Distance(_saberL.transform.position, _saberR.transform.position);
                if (handDist > 0.05f)
                {
                    Vector3 aL = _saberL.transform.TransformPoint(_saberL.BladeClashBaseLocal);
                    Vector3 qL = _saberL.transform.TransformPoint(_saberL.BladeClashTipLocal);
                    Vector3 aR = _saberR.transform.TransformPoint(_saberR.BladeClashBaseLocal);
                    Vector3 qR = _saberR.transform.TransformPoint(_saberR.BladeClashTipLocal);
                    Vector3 laL = _saberL.transform.InverseTransformPoint(aL);
                    Vector3 lqL = _saberL.transform.InverseTransformPoint(qL);
                    Vector3 laR = _saberL.transform.InverseTransformPoint(aR);
                    Vector3 lqR = _saberL.transform.InverseTransformPoint(qR);
                    float touch = _saberL.BladeClashRadiusBase + _saberR.BladeClashRadiusBase;
                    float dist = SegmentDistance(laL, lqL, laR, lqR, out var localClash);
                    if (dist <= touch)
                    {
                        leftTouch = rightTouch = true;
                        saberClash = true;
                        clashPos = _saberL.transform.TransformPoint(localClash);
                    }
                }
            }

            // Schwert ↔ Wand: Klingen-Segment gegen den Wand-Quader. Bewusst manuell —
            // die Wand trägt keinen Collider (siehe WallView/Invariante 9).
            bool leftWall = false, rightWall = false;
            Vector3 sizzlePos = Vector3.zero;
            Vector3 leftWallPos = Vector3.zero, rightWallPos = Vector3.zero;
            if (_activeWalls.Count > 0)
            {
                if (IsBladeReady(_saberL) && BladeTouchesWall(_saberL, out leftWallPos))
                { leftTouch = true; leftWall = true; sizzlePos = leftWallPos; }
                if (IsBladeReady(_saberR) && BladeTouchesWall(_saberR, out rightWallPos))
                { rightTouch = true; rightWall = true; if (!leftWall) sizzlePos = rightWallPos; }
            }

            Fx.SetWallContact(SaberSide.Left, leftWall, leftWallPos, _roomScale);
            Fx.SetWallContact(SaberSide.Right, rightWall, rightWallPos, _roomScale);

            ApplyContactHaptic(SaberSide.Left, leftTouch);
            ApplyContactHaptic(SaberSide.Right, rightTouch);

            if (saberClash && !_saberClashing)
            {
                Haptics(SaberSide.Left, GameConfig.HapticLightAmplitude, GameConfig.HapticContactPulseDuration);
                Haptics(SaberSide.Right, GameConfig.HapticLightAmplitude, GameConfig.HapticContactPulseDuration);
            }
            _saberClashing = saberClash;
            Fx.SetClashContact(saberClash, clashPos, _roomScale);

            // (4) Wand-Brutzeln: anhaltend an der Schnittstelle, solange ein Schwert in der
            // Wand steckt — räumliches, elektrisches Geräusch.
            if (_audio != null) _audio.SetWallSizzle(leftWall || rightWall, sizzlePos);
        }

        private static bool IsBladeReady(SaberView saber) =>
            saber != null && !saber.Retracted && saber.Tip != null && saber.BaseTrans != null;

        private bool BladeTouchesWall(SaberView saber, out Vector3 contactPoint)
        {
            Vector3 a = saber.transform.TransformPoint(saber.BladeClashBaseLocal);
            Vector3 b = saber.transform.TransformPoint(saber.BladeClashTipLocal);
            contactPoint = b;
            for (int i = 0; i < _activeWalls.Count; i++)
            {
                var w = _activeWalls[i];
                if (w != null && w.TryIntersectSegment(a, b, out var p))
                {
                    contactPoint = p; // echte Eintrittsstelle der Klinge (Wand-Funken)
                    return true;
                }
            }
            return false;
        }

        private void ApplyContactHaptic(SaberSide side, bool touching)
        {
            int i = (int)side;
            if (!touching)
            {
                _contactHapticNext[i] = 0f;
                return;
            }
            float now = Time.unscaledTime;
            if (now < _contactHapticNext[i]) return;
            // (4) Unruhige Reibungs-Haptik: Zufalls-Impulsrate 40..80 Hz + Zufalls-Amplitude
            // (simuliert das „Brutzeln"/Reiben an der Wand).
            float hz = UnityEngine.Random.Range(GameConfig.WallSizzleHapticMinHz, GameConfig.WallSizzleHapticMaxHz);
            _contactHapticNext[i] = now + 1f / Mathf.Max(1f, hz);
            float amp = GameConfig.WallSizzleHapticAmplitude * UnityEngine.Random.Range(0.6f, 1f);
            Haptics(side, amp, GameConfig.HapticContactPulseDuration);
        }

        /// <summary>
        /// Kürzester Abstand zweier Strecken (Klingen-Mittellinien). Standard-Kleinstabstand
        /// (Ericson, Real-Time Collision Detection) — allokationsfrei, kein Physik-Overhead.
        /// </summary>
        /// <summary>Kürzester Abstand zweier Strecken — liefert als <paramref name="contactPoint"/>
        /// den Mittelpunkt der beiden nächstgelegenen Punkte (Schwert-Clash-Berührungsstelle).</summary>
        private static float SegmentDistance(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2,
            out Vector3 contactPoint)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);
            const float eps = 1e-8f;

            float s, t;
            if (a <= eps && e <= eps)
            {
                contactPoint = (p1 + p2) * 0.5f;
                return r.magnitude;
            }

            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            contactPoint = (c1 + c2) * 0.5f;
            return (c1 - c2).magnitude;
        }

    }
}
