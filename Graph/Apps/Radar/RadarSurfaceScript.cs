using System;
using System.Collections.Generic;
using Graph.Apps.Abstract;
using Graph.Helpers;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Game.GUI.TextPanel;
using VRageMath;
using IMyCubeBlock  = VRage.Game.ModAPI.IMyCubeBlock;
using IMyCubeGrid   = VRage.Game.ModAPI.IMyCubeGrid;
using IMySlimBlock  = VRage.Game.ModAPI.IMySlimBlock;
using InGameSensor  = Sandbox.ModAPI.Ingame.IMySensorBlock;
using InGameTurret  = Sandbox.ModAPI.Ingame.IMyLargeTurretBase;
using DetectedInfo  = Sandbox.ModAPI.Ingame.MyDetectedEntityInfo;

namespace Graph.Apps.Radar
{
    internal enum ContactBehavior
    {
        Unknown,
        Approaching,
        MovingAway,
        Lateral,
        Stationary
    }

    internal class ContactRecord
    {
        public long   EntityId;
        public string Name;
        public MyRelationsBetweenPlayerAndBlock Relationship;
        public Vector3D WorldPosition;
        public bool IsTargeted;
        public ContactBehavior Behavior;
        public float ApproachRate; // m/s, positive = getting closer
        public int  MissedFrames;

        // Sweep-highlight alpha (0–255); set each frame by UpdateContactSweepState
        public byte SweepAlpha;

        // Two-second history snapshot
        public bool     HasHistory;
        public Vector3D HistoryPosition;
        public float    HistoryDistance;
        public int      HistoryAge; // update-frames since snapshot was taken
    }

    [MyTextSurfaceScript(ID, TITLE)]
    public class RadarSurfaceScript : SurfaceScriptBase
    {
        public const string ID    = "LCDMod_Radar";
        public const string TITLE = "LCDMod_Radar";

        protected override string DefaultTitle => TITLE;

        // Fixed ring distances (meters) — drawn regardless of dynamic range
        const float RING_1_M = 800f;
        const float RING_2_M = 1400f;
        const float RING_3_M = 2000f;
        const float DEFAULT_RANGE = 3000f;

        // Timing intervals (Run() calls; each call ≈ 166 ms at Update10)
        const int HISTORY_INTERVAL = 26; 
        const int CONTACT_TIMEOUT  = 36; 
        const int BLOCK_REFRESH    = 60;   // refresh block lists every ≈ 10 s

        const int MAX_CONTACTS = 32;

        // Visual sizes in logical pixels — scaled by Scale at render time
        const float RADAR_MARGIN_PX = 14f;
        const float RING_STROKE_PX  = 1.5f;
        const float LINE_STROKE_PX  = 1.0f;
        const float DIAG_STROKE_PX  = 0.8f;
        const float CONTACT_SIZE_PX = 6f;
        const float CONTACT_BIG_PX  = 10f;
        const float MARKER_FONT     = 0.55f;
        const float INFO_FONT       = 0.46f;
        const float CLOSE_RATIO     = 0.10f; // fraction of max range that counts as "close"
        const float APPROACH_MS     = 3f;    // m/s threshold for behaviour classification

        // Sweep-line visual
        const float SWEEP_LINE_STROKE_PX     = 2f;
        const int   SWEEP_AFTERGLOW_COUNT    = 5;
        const float SWEEP_AFTERGLOW_STEP_DEG = 6f;
        const float SWEEP_LINE_ALPHA         = 0.82f;

        // Entity contacts (sensor / turret) keyed by EntityId > 0
        readonly Dictionary<long, ContactRecord>  _contacts       = new Dictionary<long, ContactRecord>();
        // GPS contacts keyed by -(long)gps.Hash (always negative to avoid collisions)
        readonly Dictionary<long, ContactRecord>  _gpsContacts    = new Dictionary<long, ContactRecord>();

        readonly List<IMySensorBlock>             _sensors        = new List<IMySensorBlock>();
        readonly List<IMyLargeTurretBase>         _turrets        = new List<IMyLargeTurretBase>();
        readonly List<IMyRadioAntenna>            _antennas       = new List<IMyRadioAntenna>();
        readonly List<IMySlimBlock>               _tempSlims      = new List<IMySlimBlock>();
        readonly List<DetectedInfo>               _tempDetected   = new List<DetectedInfo>();
        readonly List<IMyGps>                     _tempGps        = new List<IMyGps>();
        readonly HashSet<long>                    _seenThisFrame  = new HashSet<long>();
        readonly List<long>                       _toRemove       = new List<long>();
        readonly List<ContactRecord>              _sortedContacts = new List<ContactRecord>();

        int   _frameCount;
        float _maxRange     = DEFAULT_RANGE;
        float _sweepAngleDeg; // degrees [0, 360); advanced each Run() call

        // Diagnostic counters — updated each frame, shown on LCD
        int _diagSensors;
        int _diagSensorsWorking;
        int _diagTurrets;
        int _diagTurretsTargeting;
        int _diagRawDetections;
        int _diagGps;
        int _diagEntities;

        readonly HashSet<IMyEntity>               _tempEntities   = new HashSet<IMyEntity>();

        // ------------------------------------------------------------------ constructor

        public RadarSurfaceScript(IMyTextSurface surface, IMyCubeBlock block, Vector2 size)
            : base(surface, block, size) { }

        // ------------------------------------------------------------------ Run

        public override void Run()
        {
            base.Run();
            if (Config == null)
                return;

            _frameCount++;

            // Advance sweep: one full rotation every ~60 frames (≈ 10 s at Update10).
            const float SWEEP_DEG_PER_FRAME = 360f / 60f;
            _sweepAngleDeg = (_sweepAngleDeg + SWEEP_DEG_PER_FRAME) % 360f;

            if (_frameCount == 1 || _frameCount % BLOCK_REFRESH == 0)
                RefreshBlocks();

            CollectContacts();
            PurgeStaleContacts();

            using (var frame = Surface.DrawFrame())
            {
                var sprites = new List<MySprite>();
                DrawTitle(sprites); // sets CaretY; respects Config.TitleVisible
                RenderRadar(sprites);
                frame.AddRange(sprites);
            }
        }

        // ------------------------------------------------------------------ block discovery

        void RefreshBlocks()
        {
            _sensors.Clear();
            _turrets.Clear();
            _antennas.Clear();
            _tempSlims.Clear();

            var grid = Block.CubeGrid as IMyCubeGrid;
            if (grid == null)
                return;

            grid.GetBlocks(_tempSlims, s =>
                s.FatBlock is IMySensorBlock    ||
                s.FatBlock is IMyLargeTurretBase ||
                s.FatBlock is IMyRadioAntenna);

            for (int i = 0; i < _tempSlims.Count; i++)
            {
                var fat = _tempSlims[i].FatBlock;

                var sensor = fat as IMySensorBlock;
                if (sensor != null) { _sensors.Add(sensor); continue; }

                var turret = fat as IMyLargeTurretBase;
                if (turret != null) { _turrets.Add(turret); continue; }

                var antenna = fat as IMyRadioAntenna;
                if (antenna != null) _antennas.Add(antenna);
            }

            _tempSlims.Clear();
        }

        // ------------------------------------------------------------------ contact collection

        void CollectContacts()
        {
            _seenThisFrame.Clear();
            float detectedRange = 0f;
            _diagRawDetections = 0;
            _diagSensorsWorking = 0;
            _diagTurretsTargeting = 0;

            // --- Global entity scan — primary source, runs before sensors -------
            CollectEntityContacts();

            // --- Sensors -------------------------------------------------------
            _diagSensors = _sensors.Count;
            for (int s = 0; s < _sensors.Count; s++)
            {
                var modSensor    = _sensors[s];
                var ingameSensor = modSensor as InGameSensor;
                if (!modSensor.IsWorking || ingameSensor == null) continue;
                _diagSensorsWorking++;

                float sensorRange = SensorMaxRange(ingameSensor);
                if (sensorRange > detectedRange) detectedRange = sensorRange;

                _tempDetected.Clear();
                ingameSensor.DetectedEntities(_tempDetected);
                _diagRawDetections += _tempDetected.Count;

                for (int d = 0; d < _tempDetected.Count; d++)
                {
                    var info = _tempDetected[d];
                    if (info.EntityId == 0) continue;
                    if (!IsGridContact(info.Type)) continue; // ignore characters, asteroids, etc.
                    if (_seenThisFrame.Contains(info.EntityId)) continue;
                    _seenThisFrame.Add(info.EntityId);
                    UpdateContact(info, false);
                }
            }

            // --- Turrets -------------------------------------------------------
            _diagTurrets = _turrets.Count;
            for (int t = 0; t < _turrets.Count; t++)
            {
                var modTurret    = _turrets[t];
                var ingameTurret = modTurret as InGameTurret;
                if (!modTurret.IsWorking || ingameTurret == null || !ingameTurret.HasTarget) continue;
                _diagTurretsTargeting++;

                try
                {
                    var info = ingameTurret.GetTargetedEntity();
                    if (info.EntityId == 0) continue;
                    if (!IsGridContact(info.Type)) continue; // ignore characters, asteroids, etc.

                    if (_seenThisFrame.Contains(info.EntityId))
                    {
                        // Already tracked by a sensor — only promote to targeted
                        ContactRecord existing;
                        if (_contacts.TryGetValue(info.EntityId, out existing))
                            existing.IsTargeted = true;
                    }
                    else
                    {
                        _seenThisFrame.Add(info.EntityId);
                        UpdateContact(info, true);
                    }
                }
                catch (Exception ex)
                {
                    ErrorHandlerHelper.LogError(ex, this);
                }
            }

            // --- Antennas — contribute range only, no contact data ─────────────
            for (int a = 0; a < _antennas.Count; a++)
            {
                var ant = _antennas[a];
                if (!ant.IsWorking || !ant.IsBroadcasting) continue;
                var ingameAnt = ant as Sandbox.ModAPI.Ingame.IMyRadioAntenna;
                if (ingameAnt == null) continue;
                float r = ingameAnt.Radius;
                if (r > detectedRange) detectedRange = r;
            }

            _maxRange = detectedRange > 1f ? detectedRange : DEFAULT_RANGE;

            // --- GPS contacts (player HUD markers) ─────────────────────────────
            CollectGpsContacts();

            // Increment missed-frame counters for entity contacts not seen this frame
            foreach (var kv in _contacts)
            {
                if (_seenThisFrame.Contains(kv.Key))
                    kv.Value.MissedFrames = 0;
                else
                    kv.Value.MissedFrames++;
            }
        }

        void CollectGpsContacts()
        {
            _tempGps.Clear();
            _diagGps = 0;

            try
            {
                var session = MyAPIGateway.Session;
                var player  = session?.LocalHumanPlayer;
                if (player == null) return;

                session.GPS.GetGpsList(player.IdentityId, _tempGps);
                _diagGps = _tempGps.Count;

                var seenHashes = new HashSet<long>();

                for (int g = 0; g < _tempGps.Count; g++)
                {
                    var gps = _tempGps[g];
                    if (!gps.ShowOnHud) continue;
                    // Skip GPS at exact world origin — likely invalid/unset
                    if (gps.Coords == Vector3D.Zero) continue;

                    long key = -(long)gps.Hash; // negative to avoid EntityId collision

                    seenHashes.Add(key);

                    ContactRecord rec;
                    if (!_gpsContacts.TryGetValue(key, out rec))
                    {
                        rec = new ContactRecord { EntityId = key };
                        _gpsContacts[key] = rec;
                    }

                    rec.WorldPosition = gps.Coords;
                    rec.Name          = gps.Name ?? string.Empty;
                    // GPS has no relationship data — show as neutral (warning color)
                    rec.Relationship  = MyRelationsBetweenPlayerAndBlock.Neutral;
                    rec.IsTargeted    = false;
                    rec.MissedFrames  = 0;
                }

                // Remove GPS contacts no longer in player list
                _toRemove.Clear();
                foreach (var kv in _gpsContacts)
                    if (!seenHashes.Contains(kv.Key))
                        _toRemove.Add(kv.Key);
                for (int i = 0; i < _toRemove.Count; i++)
                    _gpsContacts.Remove(_toRemove[i]);
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }
        }


        void CollectEntityContacts()
        {
            _diagEntities = 0;
            try
            {
                var session = MyAPIGateway.Session;
                var player  = session?.LocalHumanPlayer;
                if (player == null) return;

                long localIdentity = player.IdentityId;
                var  myFaction     = session.Factions.TryGetPlayerFaction(localIdentity);
                var  shipPos       = ((IMyEntity)Block.CubeGrid).WorldMatrix.Translation;
                float range        = _maxRange;
                long ownGridId     = Block.CubeGrid.EntityId;

                _tempEntities.Clear();
                MyAPIGateway.Entities.GetEntities(_tempEntities, e =>
                {
                    var g = e as IMyCubeGrid;
                    if (g == null || g.Physics == null) return false;
                    if (g.EntityId == ownGridId) return false;
                    return Vector3D.Distance(e.WorldMatrix.Translation, shipPos) <= range;
                });

                _diagEntities = _tempEntities.Count;

                foreach (var entity in _tempEntities)
                {
                    var grid = (IMyCubeGrid)entity;
                    long entityId = grid.EntityId;

                    if (_seenThisFrame.Contains(entityId)) continue;
                    _seenThisFrame.Add(entityId);

                    var   pos  = grid.WorldMatrix.Translation;
                    float dist = (float)Vector3D.Distance(pos, shipPos);

                    ContactRecord rec;
                    if (!_contacts.TryGetValue(entityId, out rec))
                    {
                        rec = new ContactRecord { EntityId = entityId };
                        _contacts[entityId] = rec;
                    }

                    AdvanceHistory(rec, pos, dist);
                    rec.Name          = grid.DisplayName ?? string.Empty;
                    rec.Relationship  = GetGridRelationship(grid, localIdentity, myFaction);
                    rec.WorldPosition = pos;
                    rec.IsTargeted    = false;
                    rec.MissedFrames  = 0;
                }
            }
            catch (Exception ex)
            {
                ErrorHandlerHelper.LogError(ex, this);
            }
        }

        MyRelationsBetweenPlayerAndBlock GetGridRelationship(IMyCubeGrid grid, long localIdentity, IMyFaction myFaction)
        {
            var owners = grid.BigOwners;
            if (owners == null || owners.Count == 0)
                return MyRelationsBetweenPlayerAndBlock.NoOwnership;

            long ownerId = owners[0];
            if (ownerId == localIdentity)
                return MyRelationsBetweenPlayerAndBlock.Owner;

            if (myFaction != null && myFaction.Members.ContainsKey(ownerId))
                return MyRelationsBetweenPlayerAndBlock.FactionShare;

            var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(ownerId);
            if (ownerFaction == null || myFaction == null)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            var relation = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(
                myFaction.FactionId, ownerFaction.FactionId);
            if (relation == MyRelationsBetweenFactions.Enemies)
                return MyRelationsBetweenPlayerAndBlock.Enemies;

            return MyRelationsBetweenPlayerAndBlock.Neutral;
        }

        static float SensorMaxRange(InGameSensor s)
        {
            return Math.Max(s.LeftExtend, Math.Max(s.RightExtend,
                   Math.Max(s.FrontExtend, Math.Max(s.BackExtend,
                   Math.Max(s.TopExtend, s.BottomExtend)))));
        }

        void UpdateContact(DetectedInfo info, bool isTargeted)
        {
            var gridMatrix  = ((IMyEntity)Block.CubeGrid).WorldMatrix;
            float currentDist = (float)Vector3D.Distance(info.Position, gridMatrix.Translation);

            ContactRecord rec;
            if (!_contacts.TryGetValue(info.EntityId, out rec))
            {
                rec = new ContactRecord { EntityId = info.EntityId };
                _contacts[info.EntityId] = rec;
            }

            AdvanceHistory(rec, info.Position, currentDist);

            rec.Name          = info.Name ?? string.Empty;
            rec.Relationship  = info.Relationship;
            rec.WorldPosition = info.Position;
            rec.IsTargeted    = isTargeted;
            rec.MissedFrames  = 0;
        }

        void AdvanceHistory(ContactRecord rec, Vector3D currentPos, float currentDist)
        {
            if (!rec.HasHistory)
            {
                rec.HasHistory      = true;
                rec.HistoryPosition = currentPos;
                rec.HistoryDistance = currentDist;
                rec.HistoryAge      = 0;
                rec.Behavior        = ContactBehavior.Unknown;
                return;
            }

            rec.HistoryAge++;
            if (rec.HistoryAge < HISTORY_INTERVAL)
                return;

            // Enough time has passed — classify behaviour using 2-second delta
            float elapsed    = HISTORY_INTERVAL * (10f / 60f); // seconds (Update10 ≈ 166 ms)
            float distDelta  = rec.HistoryDistance - currentDist; // positive = approaching
            float approachRate = distDelta / elapsed;

            float movementMag = (float)Vector3D.Distance(currentPos, rec.HistoryPosition);

            if (movementMag < 5f)
                rec.Behavior = ContactBehavior.Stationary;
            else if (Math.Abs(approachRate) >= APPROACH_MS)
                rec.Behavior = approachRate > 0f ? ContactBehavior.Approaching : ContactBehavior.MovingAway;
            else
                rec.Behavior = ContactBehavior.Lateral;

            rec.ApproachRate    = approachRate;
            rec.HistoryPosition = currentPos;
            rec.HistoryDistance = currentDist;
            rec.HistoryAge      = 0;
        }

        // ------------------------------------------------------------------ housekeeping

        void PurgeStaleContacts()
        {
            _toRemove.Clear();
            foreach (var kv in _contacts)
                if (kv.Value.MissedFrames > CONTACT_TIMEOUT)
                    _toRemove.Add(kv.Key);

            for (int i = 0; i < _toRemove.Count; i++)
                _contacts.Remove(_toRemove[i]);
        }

        // ------------------------------------------------------------------ rendering

        void RenderRadar(List<MySprite> sprites)
        {
            var fg        = ForegroundColor;        // texto  — all structural lines, text, circles
            var bg        = BackgroundColor;        // radar interior fill
            var errColor  = Config.ErrorColor;      // erro   — enemy contacts
            var warnColor = Config.WarningColor;    // warning — neutral contacts
            var frdColor  = Config.HeaderColor;     // titulo — friendly contacts

            // Available area starts BELOW the title bar (CaretY is set by DrawTitle)
            float margin      = RADAR_MARGIN_PX * Scale;
            float areaTop     = CaretY + margin;
            float areaBottom  = ViewBox.Bottom - margin;
            float areaLeft    = ViewBox.X + margin;
            float areaRight   = ViewBox.Right - margin;
            float areaW       = areaRight - areaLeft;
            float areaH       = areaBottom - areaTop;

            float radarRadius = Math.Min(areaW, areaH) / 2f;
            var   center      = new Vector2(areaLeft + areaW / 2f, areaTop + areaH / 2f);

            if (radarRadius <= 0f) return;

            // ── 1. Radar background circle ──────────────────────────────────
            sprites.Add(FilledCircle(center, radarRadius, bg));

            // ── 2. Dynamic outer ring (current max detectable range) ─────────
            DrawRing(sprites, center, radarRadius, RING_STROKE_PX * Scale, fg, bg);

            // ── 3. Fixed rings 800 / 1400 / 2000 m (outer → inner) ──────────
            float[] fixedMeters = { RING_3_M, RING_2_M, RING_1_M };
            for (int r = 0; r < fixedMeters.Length; r++)
            {
                float m = fixedMeters[r];
                if (m >= _maxRange) continue;
                float ringR = (m / _maxRange) * radarRadius;
                DrawRing(sprites, center, ringR, RING_STROKE_PX * Scale, fg, bg);
            }

            // ── 4. Radial lines ──────────────────────────────────────────────
            float lineLen = radarRadius * 2f;
            var   dimFg   = new Color(fg, 0.35f);

            DrawLine(sprites, center, lineLen, LINE_STROKE_PX * Scale, fg, 0f);
            DrawLine(sprites, center, lineLen, LINE_STROKE_PX * Scale, fg, MathHelper.PiOver2);
            DrawLine(sprites, center, lineLen, DIAG_STROKE_PX * Scale, dimFg,  MathHelper.Pi / 4f);
            DrawLine(sprites, center, lineLen, DIAG_STROKE_PX * Scale, dimFg, -MathHelper.Pi / 4f);

            // ── 5. Sweep line (above grid, below contacts) ───────────────────
            float sweepRad = _sweepAngleDeg * (float)(Math.PI / 180.0);
            DrawSweepLine(sprites, center, radarRadius, sweepRad);

            // ── 5b. Center dot ────────────────────────────────────────────────
            sprites.Add(FilledCircle(center, 3f * Scale, fg));

            // ── 6. Contacts ──────────────────────────────────────────────────
            BuildSortedContacts();
            var gridMatrix = ((IMyEntity)Block.CubeGrid).WorldMatrix;
            int shown = Math.Min(_sortedContacts.Count, MAX_CONTACTS);

            for (int i = 0; i < shown; i++)
            {
                var contact = _sortedContacts[i];
                bool isGps  = contact.EntityId < 0;

                // Compute normalised radar-plane coords once; reused for position, clamp, and sweep.
                float normX, normZ;
                ProjectToNorm(contact.WorldPosition, gridMatrix, out normX, out normZ);

                Vector2 screenPos;
                if (isGps)
                {
                    // GPS contacts: always show, clamped to radar edge when out of range.
                    float mag = (float)Math.Sqrt(normX * normX + normZ * normZ);
                    float cx = normX, cz = normZ;
                    if (mag > 1f) { cx /= mag; cz /= mag; }
                    screenPos = new Vector2(center.X + cx * radarRadius, center.Y + cz * radarRadius);
                }
                else
                {
                    // Entity contacts: skip if outside max range.
                    if (normX * normX + normZ * normZ > 1f) continue;
                    screenPos = new Vector2(center.X + normX * radarRadius, center.Y + normZ * radarRadius);
                }

                float dist   = (float)Vector3D.Distance(contact.WorldPosition, gridMatrix.Translation);
                bool isClose = !isGps && dist < _maxRange * CLOSE_RATIO;
                float dotR   = (contact.IsTargeted || isClose)
                    ? CONTACT_BIG_PX * Scale / 2f
                    : CONTACT_SIZE_PX * Scale / 2f;

                // Update sweep-highlight alpha before drawing the dot.
                UpdateContactSweepState(contact, normX, normZ, _sweepAngleDeg);

                Color baseColor = ContactColor(contact, errColor, warnColor, frdColor);
                sprites.Add(FilledCircle(screenPos, dotR, new Color(baseColor, contact.SweepAlpha / 255f)));
            }

            // ── 7. Direction markers — fixed, never rotate ───────────────────
            float mOff   = radarRadius + 10f * Scale;
            float mScale = MARKER_FONT * Scale;
            float halfH  = 7f * Scale;

            AddLabel(sprites, "N", new Vector2(center.X,          center.Y - mOff),              fg, mScale);
            AddLabel(sprites, "S", new Vector2(center.X,          center.Y + mOff - halfH * 2f), fg, mScale);
            AddLabel(sprites, "L", new Vector2(center.X + mOff,   center.Y - halfH),             fg, mScale);
            AddLabel(sprites, "O", new Vector2(center.X - mOff,   center.Y - halfH),             fg, mScale);

            // ── 8. Info + diagnostic — bottom-left corner ────────────────────
            float infoScale  = INFO_FONT * Scale;
            float lineH      = 13f * Scale;
            float bx         = ViewBox.X + 4f * Scale;           // left edge
            float by         = ViewBox.Bottom - lineH * 3f - 4f * Scale; // three lines from bottom
            var   diagColor  = new Color(fg, 0.55f);

            AddLabelLeft(sprites, "RANGE "   + FormatMeters(_maxRange), new Vector2(bx, by),             fg,       infoScale);
            AddLabelLeft(sprites, "TARGETS " + _contacts.Count,         new Vector2(bx, by + lineH),     fg,       infoScale);
            AddLabelLeft(sprites,
                "S:" + _diagSensorsWorking + "/" + _diagSensors +
                " T:" + _diagTurretsTargeting + "/" + _diagTurrets +
                " ANT:" + _antennas.Count +
                " GPS:" + _diagGps +
                " ENT:" + _diagEntities +
                " RAW:" + _diagRawDetections +
                " CTX:" + _contacts.Count,
                new Vector2(bx, by + lineH * 2f), diagColor, infoScale * 0.85f);
        }

        // ------------------------------------------------------------------ draw primitives

        // Draws a one-sided sweep beam (center → edge) plus a fading afterglow trail.
        // angleRad: 0 = East, increases clockwise in screen space (Y-down).
        void DrawSweepLine(List<MySprite> sprites, Vector2 center, float radarRadius, float angleRad)
        {
            var   sweepColor = new Color(0, 230, 80);
            float stroke     = SWEEP_LINE_STROKE_PX * Scale;
            float halfLen    = radarRadius / 2f;
            float stepRad    = SWEEP_AFTERGLOW_STEP_DEG * (float)(Math.PI / 180.0);

            // Afterglow trail — farthest first so closer segments render on top
            for (int i = SWEEP_AFTERGLOW_COUNT; i >= 1; i--)
            {
                float t          = (float)(i - 1) / Math.Max(1, SWEEP_AFTERGLOW_COUNT - 1); // 0 = closest, 1 = farthest
                float alpha      = 0.40f - t * (0.40f - 0.06f);
                float trailAngle = angleRad - i * stepRad;
                float dx = (float)Math.Cos(trailAngle) * halfLen;
                float dy = (float)Math.Sin(trailAngle) * halfLen;
                var   trailColor = new Color(sweepColor.R, sweepColor.G, sweepColor.B, (int)(255 * alpha));
                DrawLine(sprites, new Vector2(center.X + dx, center.Y + dy), radarRadius, stroke, trailColor, trailAngle);
            }

            // Primary sweep line
            float pdx = (float)Math.Cos(angleRad) * halfLen;
            float pdy = (float)Math.Sin(angleRad) * halfLen;
            var   primaryColor = new Color(sweepColor.R, sweepColor.G, sweepColor.B, (int)(255 * SWEEP_LINE_ALPHA));
            DrawLine(sprites, new Vector2(center.X + pdx, center.Y + pdy), radarRadius, stroke, primaryColor, angleRad);

            // Glow dot at center
            sprites.Add(FilledCircle(center, 4f * Scale, new Color(sweepColor.R, sweepColor.G, sweepColor.B, 100)));
        }

        void DrawRing(List<MySprite> sprites, Vector2 center, float radius, float stroke,
                      Color strokeColor, Color bgColor)
        {
            if (radius <= 0f) return;
            sprites.Add(FilledCircle(center, radius + stroke, strokeColor)); // outer filled
            float inner = radius - stroke;
            if (inner > 0f)
                sprites.Add(FilledCircle(center, inner, bgColor));           // inner erase
        }

        static void DrawLine(List<MySprite> sprites, Vector2 center, float length, float stroke,
                             Color color, float angleRad)
        {
            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXTURE,
                Data            = "SquareSimple",
                Position        = center,
                Size            = new Vector2(length, stroke),
                Color           = color,
                Alignment       = TextAlignment.CENTER,
                RotationOrScale = angleRad
            });
        }

        static MySprite FilledCircle(Vector2 center, float radius, Color color)
        {
            return new MySprite
            {
                Type      = SpriteType.TEXTURE,
                Data      = "Circle",
                Position  = center,
                Size      = new Vector2(radius * 2f),
                Color     = color,
                Alignment = TextAlignment.CENTER
            };
        }

        static void AddLabel(List<MySprite> sprites, string text, Vector2 pos, Color color, float scale)
        {
            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = text,
                Position        = pos,
                Color           = color,
                Alignment       = TextAlignment.CENTER,
                FontId          = "White",
                RotationOrScale = scale
            });
        }

        static void AddLabelLeft(List<MySprite> sprites, string text, Vector2 pos, Color color, float scale)
        {
            sprites.Add(new MySprite
            {
                Type            = SpriteType.TEXT,
                Data            = text,
                Position        = pos,
                Color           = color,
                Alignment       = TextAlignment.LEFT,
                FontId          = "White",
                RotationOrScale = scale
            });
        }

        // ------------------------------------------------------------------ sweep state

        // Updates contact.SweepAlpha based on its normalised radar position and the current sweep angle.
        // normX / normZ are the already-computed ProjectToNorm outputs for this contact.
        void UpdateContactSweepState(ContactRecord contact, float normX, float normZ, float sweepAngleDeg)
        {
            // Convert the contact's radar-plane angle to degrees (screen: +X right, +Z down → atan2(normZ, normX))
            float contactAngleDeg = (float)(Math.Atan2(normZ, normX) * (180.0 / Math.PI));
            if (contactAngleDeg < 0f) contactAngleDeg += 360f;

            float delta = sweepAngleDeg - contactAngleDeg;
            // Wrap delta into [0, 360) so we measure how far behind the sweep the contact is
            delta = delta % 360f;
            if (delta < 0f) delta += 360f;

            // Fade over a 90-degree trailing arc; contacts ahead of the sweep start fresh
            const float FADE_ARC = 90f;
            float alpha = delta < FADE_ARC ? (1f - delta / FADE_ARC) : 0f;
            contact.SweepAlpha = (byte)(alpha * 255f);
        }

        // ------------------------------------------------------------------ coordinate transform

        // Returns null if outside max range (entity contacts — clipped).
        Vector2? ToRadarScreen(Vector3D worldPos, Vector2 radarCenter, float radarRadius, MatrixD gridMatrix)
        {
            float normX, normZ;
            ProjectToNorm(worldPos, gridMatrix, out normX, out normZ);
            if (normX * normX + normZ * normZ > 1f) return null;
            return new Vector2(
                radarCenter.X + normX * radarRadius,
                radarCenter.Y + normZ * radarRadius);
        }

        // Always returns a position — clamps to radar edge for GPS contacts beyond range.
        Vector2 ToRadarScreenClamped(Vector3D worldPos, Vector2 radarCenter, float radarRadius, MatrixD gridMatrix)
        {
            float normX, normZ;
            ProjectToNorm(worldPos, gridMatrix, out normX, out normZ);
            float mag = (float)Math.Sqrt(normX * normX + normZ * normZ);
            if (mag > 1f) { normX /= mag; normZ /= mag; }
            return new Vector2(
                radarCenter.X + normX * radarRadius,
                radarCenter.Y + normZ * radarRadius);
        }

        void ProjectToNorm(Vector3D worldPos, MatrixD gridMatrix, out float normX, out float normZ)
        {
            var delta    = worldPos - gridMatrix.Translation;
            var localPos = Vector3D.TransformNormal(delta, MatrixD.Transpose(gridMatrix));
            // +X = ship-right → radar right
            // −Z = ship-forward → radar up (screen Y decreases)
            normX = (float)(localPos.X / _maxRange);
            normZ = (float)(localPos.Z / _maxRange);
        }

        // ------------------------------------------------------------------ contact sort

        void BuildSortedContacts()
        {
            _sortedContacts.Clear();
            var shipPos = ((IMyEntity)Block.CubeGrid).WorldMatrix.Translation;

            // Entity contacts: only those within detection range
            foreach (var kv in _contacts)
            {
                var c = kv.Value;
                if ((float)Vector3D.Distance(c.WorldPosition, shipPos) > _maxRange)
                    continue;
                _sortedContacts.Add(c);
            }

            // GPS contacts: always included regardless of range (shown clamped at edge)
            foreach (var kv in _gpsContacts)
                _sortedContacts.Add(kv.Value);

            // Targeted contacts first, then closest-first
            _sortedContacts.Sort((a, b) =>
            {
                if (a.IsTargeted != b.IsTargeted)
                    return a.IsTargeted ? -1 : 1;
                var pos = ((IMyEntity)Block.CubeGrid).WorldMatrix.Translation;
                float da = (float)Vector3D.Distance(a.WorldPosition, pos);
                float db = (float)Vector3D.Distance(b.WorldPosition, pos);
                return da.CompareTo(db);
            });
        }

        // ------------------------------------------------------------------ type filter

        static bool IsGridContact(Sandbox.ModAPI.Ingame.MyDetectedEntityType type)
        {
            return type == Sandbox.ModAPI.Ingame.MyDetectedEntityType.LargeGrid
                || type == Sandbox.ModAPI.Ingame.MyDetectedEntityType.SmallGrid;
        }

        // ------------------------------------------------------------------ colour mapping

        static Color ContactColor(ContactRecord c, Color errColor, Color warnColor, Color frdColor)
        {
            switch (c.Relationship)
            {
                case MyRelationsBetweenPlayerAndBlock.Enemies:
                    return errColor;
                case MyRelationsBetweenPlayerAndBlock.Owner:
                case MyRelationsBetweenPlayerAndBlock.FactionShare:
                    return frdColor;
                default:
                    return warnColor;
            }
        }

        // ------------------------------------------------------------------ utility

        static string FormatMeters(float meters)
        {
            if (meters >= 1000f)
                return string.Format("{0:F1}km", meters / 1000f);
            return string.Format("{0:F0}m", meters);
        }
    }
}
