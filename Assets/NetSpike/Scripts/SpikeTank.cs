using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.NetSpike
{
    /// <summary>
    /// One networked tank. Server: queues the owner's commands, steps the simulation, resolves shots with lag compensation,
    /// sends a snapshot every tick. Owner client: predicts with the same simulation, replays unacknowledged commands when a
    /// snapshot disagrees. Other clients: buffer snapshots and interpolate ~3 ticks behind.
    /// </summary>
    public sealed class SpikeTank : NetworkBehaviour
    {
        public static SpikeTank Local { get; private set; }
        public static readonly List<SpikeTank> All = new List<SpikeTank>();
        public static float RemoteViewTick;           // server tick the remote tanks are being drawn at (sent with every shot)
        public static uint LatestServerTick;
        public static int HudHp, HudShield; public static bool HudDmg; public static float HudSpeedTime, HudDashCd; public static int HudWeapon, HudAmmo;   // local tank, for the screen
        public static int JitterTicks;                // server: wait for this many queued commands before consuming (0 = off)

        [Header("Visual")]
        public Transform visual;
        public Transform turret;
        public TextMesh hpText;
        public GameObject flash;
        public Material shellMaterial;
        public GameObject shellPrefab;                // the prototype's projectile look (no Projectile script)

        public bool botMode;                          // scripted input instead of the keyboard (automated tests)

        // ------------------------------------------------------------------ server side
        public TankState ServerState;
        public bool ServerDead;
        byte m_Hp = SpikeSim.MaxHp, m_Epoch = 1;
        float m_Shield, m_ShieldTime, m_DmgTime;     // server-only buffs (not part of the movement simulation)
        uint m_Ack, m_LastQueued;
        readonly Queue<InputCmd> m_Queue = new Queue<InputCmd>();
        InputCmd m_Last, m_FireCmd;
        bool m_HasLast, m_WantFire, m_Primed;
        int m_FireWeapon;
        float m_RespawnAt;
        readonly TankState[] m_SHist = new TankState[64];
        readonly uint[] m_SHistTick = new uint[64];

        public override void OnStartServer()
        {
            SpikeServer.I.Register(this);
            ServerState = new TankState { pos = SpikeServer.I.PickSpawn(this) };
            ServerState.yaw = ServerState.turretYaw = Mathf.Atan2(-ServerState.pos.x, -ServerState.pos.z) * Mathf.Rad2Deg;
        }

        public override void OnStopServer() { if (SpikeServer.I != null) SpikeServer.I.Unregister(this); }

        [Command(channel = Channels.Unreliable)]
        void CmdInput(InputCmd[] cmds)
        {
            foreach (InputCmd c in cmds)
                if (c.seq > m_LastQueued) { m_Queue.Enqueue(c); m_LastQueued = c.seq; }
        }

        public void ServerStep(uint tick)
        {
            if (ServerDead)
            {
                m_Queue.Clear();
                if (Time.time >= m_RespawnAt) ServerRespawn();
                Record(tick);
                return;
            }
            while (m_Queue.Count > 4) { m_Queue.Dequeue(); SpikeMetrics.ServerDropped++; }
            InputCmd c; bool real = false;
            if (m_Queue.Count > 0 && (JitterTicks == 0 || m_Primed || m_Queue.Count >= JitterTicks)) { m_Primed = true; c = m_Queue.Dequeue(); m_Last = c; m_HasLast = true; real = true; }
            else
            {
                if (m_HasLast) SpikeMetrics.ServerStarved++;
                m_Primed = false;
                c = m_HasLast ? m_Last : default;
                c.buttons = 0;                                  // never repeat a shot
            }
            float cdBefore = ServerState.dashCd;
            bool fired = SpikeSim.Step(ref ServerState, c);
            if (ServerState.dashCd > cdBefore) SpikeMetrics.DashesStarted++;
            if (m_ShieldTime > 0f) { m_ShieldTime -= SpikeSim.Dt; if (m_ShieldTime <= 0f) m_Shield = 0f; }
            if (m_DmgTime > 0f) m_DmgTime -= SpikeSim.Dt;
            if (real) m_Ack = c.seq;
            m_WantFire = real && fired;
            if (m_WantFire) { m_FireCmd = c; m_FireWeapon = SpikeSim.LastFiredWeapon; }
            Record(tick);
        }

        void Record(uint tick) { m_SHist[tick % 64] = ServerState; m_SHistTick[tick % 64] = tick; }

        TankState StateAt(uint tick) { return m_SHistTick[tick % 64] == tick ? m_SHist[tick % 64] : ServerState; }

        /// <summary>Resolve the shot against targets as the shooter saw them (lag compensation), then apply damage when the shell would arrive.</summary>
        public void ServerResolveFire(uint tick)
        {
            if (!m_WantFire) return;
            m_WantFire = false;
            SpikeMetrics.ServerFires++;
            TankState s = ServerState;
            SpikeSim.WeaponSpec w = SpikeSim.Weapons[m_FireWeapon];
            Vector3 origin = SpikeSim.MuzzleOrigin(s);
            uint view = m_FireCmd.viewTick;
            if (view > tick || tick - view > 20) view = tick;                 // never rewind more than ~0.66 s
            float mult = m_DmgTime > 0f ? SpikeSim.DamageMult : 1f;
            var dirs = new Vector3[w.pellets]; var dists = new float[w.pellets];
            bool confirmSent = false;
            for (int i = 0; i < w.pellets; i++)
            {
                Vector3 dir = SpikeSim.PelletDir(s, m_FireWeapon, m_FireCmd.seq, i);
                float best = SpikeWorld.RayDistance(origin, dir, w.range);
                SpikeTank target = null;
                foreach (SpikeTank o in SpikeServer.I.Tanks)
                {
                    if (o == this || o.ServerDead) continue;
                    if (SpikeSim.RayHitsTank(origin, dir, o.StateAt(view), w.range, out float d) && d < best) { best = d; target = o; }
                }
                dirs[i] = dir; dists[i] = best;
                bool wall = target == null && best < w.range - 0.01f;
                if (target == null && !(w.splashRadius > 0f && wall)) continue;
                float travel = best / w.speed;
                SpikeServer.I.Pending.Add(new SpikeServer.PendingHit
                {
                    target = target, shooter = this, fireSeq = m_FireCmd.seq, travel = travel, dmg = Mathf.RoundToInt(w.damage * mult), confirm = target != null && !confirmSent,
                    impact = origin + dir * best, splashR = w.splashRadius, splashDmg = Mathf.RoundToInt(w.damage * w.splashFactor * mult),
                    applyTick = tick + (uint)Mathf.CeilToInt(travel / SpikeSim.Dt),
                });
                if (target != null) confirmSent = true;
            }
            RpcShot(origin, dirs, dists, (byte)m_FireWeapon);
        }

        /// <summary>Applies an item to this tank. Returns false when it would be wasted (a repair kit at full health), so the item stays.</summary>
        public bool ServerApplyPickup(SpikePickup.Kind kind)
        {
            if (ServerDead) return false;
            switch (kind)
            {
                case SpikePickup.Kind.Repair: if (m_Hp >= SpikeSim.MaxHp) return false; m_Hp = (byte)Mathf.Min(SpikeSim.MaxHp, m_Hp + SpikeSim.HealAmount); return true;
                case SpikePickup.Kind.Shield: m_Shield = SpikeSim.ShieldAmount; m_ShieldTime = SpikeSim.ShieldSeconds; return true;
                case SpikePickup.Kind.Speed: ServerState.speedTime = SpikeSim.SpeedSeconds; return true;
                case SpikePickup.Kind.Damage: m_DmgTime = SpikeSim.DamageSeconds; return true;
                case SpikePickup.Kind.MachineGun: return GrantWeapon(1);
                case SpikePickup.Kind.Shotgun: return GrantWeapon(2);
                case SpikePickup.Kind.Rocket: return GrantWeapon(3);
            }
            return false;
        }

        bool GrantWeapon(int idx)
        {
            if (ServerState.weapon == idx) return false;
            ServerState.weapon = (byte)idx; ServerState.ammo = (byte)SpikeSim.Weapons[idx].ammo;
            ServerState.fireCd = Mathf.Min(ServerState.fireCd, 0.15f);
            return true;
        }

        public void ServerDamage(SpikeTank shooter, int dmg, uint fireSeq, float travel, bool confirm)
        {
            if (m_Shield > 0f)
            {
                float absorbed = Mathf.Min(m_Shield, dmg);
                m_Shield -= absorbed; dmg -= Mathf.RoundToInt(absorbed);
                if (m_Shield <= 0f) m_ShieldTime = 0f;
            }
            int hp = Mathf.Max(0, m_Hp - dmg);
            m_Hp = (byte)hp;
            bool kill = hp == 0;
            if (kill && shooter != null && SpikeMatch.I != null) SpikeMatch.I.ServerKill(shooter, this);
            if (kill) RpcExplodeFx(ServerState.pos + Vector3.up * 0.8f); else RpcHitFx(ServerState.pos + Vector3.up * 0.9f);
            if (kill) { ServerDead = true; m_RespawnAt = Time.time + 2f; m_Shield = 0f; m_ShieldTime = 0f; m_DmgTime = 0f; }
            if (confirm && shooter != null && shooter.connectionToClient != null) shooter.TargetHitConfirm(shooter.connectionToClient, fireSeq, travel, kill);
        }

        void ServerRespawn()
        {
            ServerDead = false;
            m_Hp = SpikeSim.MaxHp; m_Shield = 0f; m_ShieldTime = 0f; m_DmgTime = 0f;
            ServerState = new TankState { pos = SpikeServer.I.PickSpawn(this) };
            ServerState.yaw = ServerState.turretYaw = Mathf.Atan2(-ServerState.pos.x, -ServerState.pos.z) * Mathf.Rad2Deg;
            m_Epoch++;
        }

        public void ServerSendSnapshot(uint tick)
        {
            RpcSnapshot(new Snap
            {
                serverTick = tick, ackSeq = m_Ack, x = ServerState.pos.x, z = ServerState.pos.z, speed = ServerState.speed, fireCd = ServerState.fireCd,
                dashTime = ServerState.dashTime, dashCd = ServerState.dashCd, dashYaw = ServerState.dashYaw, speedTime = ServerState.speedTime, weapon = ServerState.weapon, ammo = ServerState.ammo,
                yaw = (ushort)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(ServerState.yaw, 360f) / 360f * 65536f), 0, 65535),
                turret = (ushort)Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(ServerState.turretYaw, 360f) / 360f * 65536f), 0, 65535),
                hp = m_Hp, shield = (byte)Mathf.CeilToInt(m_Shield), flags = (byte)((ServerDead ? 1 : 0) | (m_DmgTime > 0f ? 2 : 0)), epoch = m_Epoch,
            });
        }

        [ClientRpc(channel = Channels.Unreliable)]
        void RpcSnapshot(Snap s)
        {
            SpikeMetrics.SnapshotsIn++;
            if (s.serverTick > LatestServerTick) LatestServerTick = s.serverTick;
            m_ClientDead = (s.flags & 1) != 0;
            if (hpText != null) hpText.text = m_ClientDead ? "DEAD" : s.hp + (s.shield > 0 ? " +" + s.shield : "") + ((s.flags & 2) != 0 ? " x1.5" : "") + (s.weapon != 0 ? "\n" + SpikeSim.Weapons[s.weapon].name + " " + s.ammo : "");
            if (isLocalPlayer) { HudHp = s.hp; HudShield = s.shield; HudDmg = (s.flags & 2) != 0; HudSpeedTime = s.speedTime; HudDashCd = s.dashCd; HudWeapon = s.weapon; HudAmmo = s.ammo; }
            if (isLocalPlayer) Reconcile(s); else AddRemote(s);
        }

        [ClientRpc(includeOwner = false, channel = Channels.Reliable)]
        void RpcShot(Vector3 origin, Vector3[] dirs, float[] dists, byte weapon) { for (int i = 0; i < dirs.Length; i++) SpawnShell(origin, dirs[i], dists[i], weapon); }

        [TargetRpc]
        void TargetHitConfirm(NetworkConnectionToClient target, uint fireSeq, float travel, bool kill)
        {
            double sent = m_FireTime[fireSeq % 256];
            if (sent > 0.0) SpikeMetrics.FireConfirmMs.Add((float)((Time.realtimeSinceStartupAsDouble - sent) * 1000.0 - travel * 1000.0));
            SpikeMetrics.Hits++;
            if (m_ExpectedHit[fireSeq % 256]) { SpikeMetrics.ConfirmedOfExpected++; m_ExpectedHit[fireSeq % 256] = false; }
            if (kill) SpikeMetrics.Kills++;
        }

        // ------------------------------------------------------------------ client side
        struct Entry { public InputCmd cmd; public TankState after; public bool valid; }

        readonly Entry[] m_CHist = new Entry[256];
        readonly double[] m_FireTime = new double[256];
        readonly InputCmd[] m_Recent = new InputCmd[3];
        TankState m_Pred;
        Vector3 m_VisOff;
        float m_RemoteDashCd;
        TankState m_Prev; bool m_PrevValid;      // state before the last tick, for render interpolation at display rate
        uint m_Seq, m_LastAck;
        byte m_ClientEpoch;
        bool m_Init, m_ClientDead;
        float m_Acc, m_BotStart;
        double m_LastSnapTime;

        struct Sample { public uint tick; public TankState state; }
        readonly List<Sample> m_Buf = new List<Sample>();
        float m_RenderTick;
        bool m_RenderInit;
        public Vector3 RenderPos { get; private set; }
        public TankState RenderState { get; private set; }
        public bool ClientDead => m_ClientDead;
        readonly bool[] m_ExpectedHit = new bool[256];

        static readonly Color[] RingColors = { new Color(0.3f, 0.7f, 1f), new Color(1f, 0.4f, 0.35f), new Color(0.45f, 1f, 0.5f), new Color(1f, 0.85f, 0.3f), new Color(0.8f, 0.5f, 1f) };
        public override void OnStartClient()
        {
            if (!All.Contains(this)) All.Add(this);
            Transform ring = transform.Find("Visual/TeamRing");
            if (ring != null && ring.TryGetComponent(out Renderer rr))
            {
                var mpb = new MaterialPropertyBlock(); rr.GetPropertyBlock(mpb);
                Color c = SpikeMatch.TeamColors[SpikeMatch.TeamOf(netId)]; c.a = 0.8f;
                mpb.SetColor("_BaseColor", c); rr.SetPropertyBlock(mpb);
            }
        }
        public override void OnStopClient() { All.Remove(this); if (Local == this) Local = null; }
        public override void OnStartLocalPlayer() { Local = this; m_BotStart = Time.time; }

        void Update()
        {
            if (!isClient) return;
            if (isLocalPlayer)
            {
                if (!m_Init) return;
                m_Acc += Time.unscaledDeltaTime;
                int guard = 0;
                while (m_Acc >= SpikeSim.Dt && guard++ < 5) { m_Acc -= SpikeSim.Dt; m_Prev = m_Pred; m_PrevValid = true; ClientTick(); }
                m_VisOff = Vector3.Lerp(m_VisOff, Vector3.zero, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                TankState r = m_Pred;
                if (m_PrevValid)
                {
                    float a = Mathf.Clamp01(m_Acc / SpikeSim.Dt);
                    r.pos = Vector3.Lerp(m_Prev.pos, m_Pred.pos, a);
                    r.yaw = Mathf.LerpAngle(m_Prev.yaw, m_Pred.yaw, a);
                    r.turretYaw = Mathf.LerpAngle(m_Prev.turretYaw, m_Pred.turretYaw, a);
                }
                Apply(r, r.pos + m_VisOff);
            }
            else RenderRemote();
        }

        void Apply(TankState s, Vector3 pos)
        {
            RenderPos = pos;
            RenderState = new TankState { pos = pos, yaw = s.yaw, turretYaw = s.turretYaw, speed = s.speed };
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
            if (turret != null) turret.rotation = Quaternion.Euler(0f, s.turretYaw, 0f);
            if (visual != null) visual.gameObject.SetActive(!m_ClientDead);
        }

        void ClientTick()
        {
            SampleInput(out Vector2 move, out float aimYaw, out bool fire, out bool dash);
            SpikeMetrics.DbgTicks++; if (move.sqrMagnitude > 0.01f) SpikeMetrics.DbgMoving++; if (botMode) SpikeMetrics.DbgBotOn++; if (fire) SpikeMetrics.DbgFireWanted++;
            uint view = (uint)Mathf.Max(0, Mathf.RoundToInt(RemoteViewTick > 0f ? RemoteViewTick : LatestServerTick));
            InputCmd c = SpikeSim.MakeCmd(++m_Seq, move, aimYaw, fire && !m_ClientDead, view, dash && !m_ClientDead);
            float dashBefore = m_Pred.dashCd;
            bool fired = !m_ClientDead && SpikeSim.Step(ref m_Pred, c);
            if (m_Pred.dashCd > dashBefore && Fx != null) Fx.SpawnDash(m_Pred.pos + Vector3.up * 0.5f, Quaternion.Euler(0f, m_Pred.dashYaw, 0f) * Vector3.forward);
            m_CHist[m_Seq % 256] = new Entry { cmd = c, after = m_Pred, valid = true };

            m_Recent[0] = m_Recent[1]; m_Recent[1] = m_Recent[2]; m_Recent[2] = c;
            CmdInput(new[] { m_Recent[0].seq == 0 ? c : m_Recent[0], m_Recent[1].seq == 0 ? c : m_Recent[1], c });

            if (fired)
            {
                SpikeMetrics.Shots++;
                m_FireTime[m_Seq % 256] = Time.realtimeSinceStartupAsDouble;
                int fw = SpikeSim.LastFiredWeapon;
                SpikeSim.WeaponSpec w = SpikeSim.Weapons[fw];
                Vector3 o = SpikeSim.MuzzleOrigin(m_Pred);
                bool expected = false;
                for (int i = 0; i < w.pellets; i++)
                {
                    Vector3 d = SpikeSim.PelletDir(m_Pred, fw, m_Seq, i);
                    float wall = SpikeWorld.RayDistance(o, d, w.range);
                    SpawnShell(o, d, wall, (byte)fw);
                    // would this shot hit what is drawn on this screen (target alive, not behind cover)? The server should agree.
                    if (!expected)
                        foreach (SpikeTank other in All)
                            if (other != this && !other.ClientDead && other.RenderPos != Vector3.zero && SpikeSim.RayHitsTank(o, d, other.RenderState, w.range, out float hd) && hd < wall) { expected = true; break; }
                }
                m_ExpectedHit[m_Seq % 256] = expected;
                if (expected) SpikeMetrics.ExpectedHits++;
            }
        }

        void Reconcile(Snap s)
        {
            TankState server = FromSnap(s);
            bool dead = (s.flags & 1) != 0;
            if (!m_Init || s.epoch != m_ClientEpoch)
            {
                m_ClientEpoch = s.epoch;
                m_Pred = server;
                if (!m_Init) { m_Init = true; m_Seq = s.ackSeq; }
                m_LastAck = s.ackSeq;
                m_VisOff = Vector3.zero;
                for (int i = 0; i < m_CHist.Length; i++) m_CHist[i].valid = false;
                return;
            }
            if (s.ackSeq <= m_LastAck) return;
            m_LastAck = s.ackSeq;

            Entry e = m_CHist[s.ackSeq % 256];
            if (!e.valid || e.cmd.seq != s.ackSeq) { m_Pred = server; return; }
            float err = Vector3.Distance(e.after.pos, server.pos);
            float yawErr = Mathf.Abs(Mathf.DeltaAngle(e.after.yaw, server.yaw));
            bool skillErr = Mathf.Abs(e.after.speedTime - server.speedTime) > 0.2f || Mathf.Abs(e.after.dashCd - server.dashCd) > 0.2f || e.after.weapon != server.weapon;
            if (err <= 0.05f && yawErr <= 2f && !skillErr) return;

            Vector3 visBefore = m_Pred.pos + m_VisOff;
            TankState st = server;
            for (uint q = s.ackSeq + 1; q <= m_Seq; q++)
            {
                Entry en = m_CHist[q % 256];
                if (!en.valid || en.cmd.seq != q) break;
                if (!dead) SpikeSim.Step(ref st, en.cmd);
                m_CHist[q % 256].after = st;
            }
            m_Pred = st;
            m_VisOff = visBefore - m_Pred.pos;
            if (m_VisOff.magnitude > 3f) m_VisOff = Vector3.zero;             // too far to smooth: snap
            SpikeMetrics.ReconCount++;
            SpikeMetrics.ReconMax = Mathf.Max(SpikeMetrics.ReconMax, err);
            if (err > 0.5f) SpikeMetrics.ReconOver05++;
        }

        static TankState FromSnap(Snap s)
        {
            return new TankState { pos = new Vector3(s.x, 0f, s.z), yaw = s.yaw * (360f / 65536f), turretYaw = s.turret * (360f / 65536f), speed = s.speed, fireCd = s.fireCd, dashTime = s.dashTime, dashCd = s.dashCd, dashYaw = s.dashYaw, speedTime = s.speedTime, weapon = s.weapon, ammo = s.ammo };
        }

        void AddRemote(Snap s)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (m_LastSnapTime > 0.0) SpikeMetrics.SnapGapMs.Add((float)((now - m_LastSnapTime) * 1000.0));
            m_LastSnapTime = now;
            if (m_Buf.Count > 0 && s.serverTick <= m_Buf[m_Buf.Count - 1].tick) return;
            if (s.dashCd > m_RemoteDashCd + 0.5f && Fx != null) Fx.SpawnDash(new Vector3(s.x, 0.5f, s.z), Quaternion.Euler(0f, s.dashYaw, 0f) * Vector3.forward);
            m_RemoteDashCd = s.dashCd;
            m_Buf.Add(new Sample { tick = s.serverTick, state = FromSnap(s) });
            if (m_Buf.Count > 40) m_Buf.RemoveAt(0);
            m_ClientEpoch = s.epoch;
        }

        void RenderRemote()
        {
            if (m_Buf.Count == 0) return;
            float latest = m_Buf[m_Buf.Count - 1].tick;
            if (!m_RenderInit) { m_RenderTick = latest - 3f; m_RenderInit = true; }
            float depth = latest - m_RenderTick;                                  // how far behind the newest snapshot we are
            float rate = 1f + 0.25f * Mathf.Clamp((depth - 3f) / 3f, -1f, 1f);   // drift back toward ~3 ticks of buffer
            m_RenderTick += Time.unscaledDeltaTime * SpikeSim.TickRate * rate;
            RemoteViewTick = m_RenderTick;
            if (m_RenderTick > latest) { m_RenderTick = latest; SpikeMetrics.Underruns++; }

            int b = 0;
            while (b < m_Buf.Count - 1 && m_Buf[b + 1].tick <= m_RenderTick) b++;
            Sample a = m_Buf[b], c = m_Buf[Mathf.Min(b + 1, m_Buf.Count - 1)];
            float k = c.tick > a.tick ? Mathf.Clamp01((m_RenderTick - a.tick) / (c.tick - a.tick)) : 0f;
            var st = new TankState
            {
                pos = Vector3.Lerp(a.state.pos, c.state.pos, k),
                yaw = Mathf.LerpAngle(a.state.yaw, c.state.yaw, k),
                turretYaw = Mathf.LerpAngle(a.state.turretYaw, c.state.turretYaw, k),
                speed = Mathf.Lerp(a.state.speed, c.state.speed, k),
            };
            if (b > 1) m_Buf.RemoveRange(0, b - 1);
            Apply(st, st.pos);
        }

        // ------------------------------------------------------------------ input
        void SampleInput(out Vector2 move, out float aimYaw, out bool fire, out bool dash)
        {
            move = Vector2.zero; aimYaw = m_Pred.turretYaw; fire = false; dash = false;
            if (botMode) { BotInput(ref move, ref aimYaw, ref fire); dash = m_BotDash; return; }
            Keyboard kb = Keyboard.current; Mouse mouse = Mouse.current; Camera cam = Camera.main;
            if (Touchscreen.current != null && (kb == null || mouse == null)) { TouchInput(ref move, ref aimYaw, ref fire); dash = m_DashLatch; m_DashLatch = false; return; }
            if (kb == null || mouse == null || cam == null) return;
            move = new Vector2((kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f), (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f));
            Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            if (new Plane(Vector3.up, new Vector3(0f, 0.8f, 0f)).Raycast(ray, out float d))
            {
                Vector3 p = ray.GetPoint(d) - m_Pred.pos;
                aimYaw = Mathf.Atan2(p.x, p.z) * Mathf.Rad2Deg;
            }
            fire = mouse.leftButton.isPressed;
            dash = kb.spaceKey.isPressed;
        }

        // Spike-grade touch controls: left half = floating move stick, right half = floating aim stick (fires while held).
        public static Rect DashButton { get { float r = Mathf.Max(70f, Screen.dpi > 0f ? Screen.dpi * 0.3f : 110f); return new Rect(Screen.width - r * 2.6f, r * 0.5f, r * 1.6f, r * 1.6f); } }   // bottom-left origin (touch space)
        bool m_DashLatch;
        int m_MoveId = -1, m_AimId = -1;
        Vector2 m_MoveOrigin, m_AimOrigin;
        void TouchInput(ref Vector2 move, ref float aimYaw, ref bool fire)
        {
            float radius = Mathf.Max(60f, Screen.dpi > 0f ? Screen.dpi * 0.38f : 120f);
            bool moveSeen = false, aimSeen = false;
            foreach (var t in Touchscreen.current.touches)
            {
                if (!t.press.isPressed) continue;
                int id = t.touchId.ReadValue(); Vector2 pos = t.position.ReadValue();
                if (id == m_MoveId) { moveSeen = true; move = Vector2.ClampMagnitude((pos - m_MoveOrigin) / radius, 1f); continue; }
                if (id == m_AimId) { aimSeen = true; AimFrom(pos - m_AimOrigin, radius, ref aimYaw, ref fire); continue; }
                if (t.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Began) continue;
                if (DashButton.Contains(pos)) { m_DashLatch = true; continue; }
                if (pos.x < Screen.width * 0.5f && m_MoveId < 0) { m_MoveId = id; m_MoveOrigin = pos; moveSeen = true; }
                else if (pos.x >= Screen.width * 0.5f && m_AimId < 0) { m_AimId = id; m_AimOrigin = pos; aimSeen = true; }
            }
            if (!moveSeen) m_MoveId = -1;
            if (!aimSeen) m_AimId = -1;
        }

        void AimFrom(Vector2 d, float radius, ref float aimYaw, ref bool fire)
        {
            if (d.magnitude > radius * 0.25f) aimYaw = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
            fire = true;
        }

        SpikePickup[] m_BotPickups;
        bool m_BotDash;
        float m_BotCheck, m_BotWanderUntil, m_BotLastDash;
        Vector3 m_BotLastPos;
        Vector2 m_BotWanderDir;

        void BotInput(ref Vector2 move, ref float aimYaw, ref bool fire)
        {
            float t = Time.time - m_BotStart;
            // orbit the arena at radius 24 m (outside every cover block); the two bots circle in opposite directions
            Vector3 p = m_Pred.pos;
            Vector2 p2 = new Vector2(p.x, p.z);
            float r = p2.magnitude;
            Vector2 radial = r > 0.1f ? p2 / r : Vector2.up;
            Vector2 tangent = new Vector2(-radial.y, radial.x) * ((netId % 2 == 0) ? 1f : -1f);
            move = tangent + radial * Mathf.Clamp((24f - r) * 0.25f, -1f, 1f);
            // go for the nearest item when there is one; wander briefly when blocked
            if (m_BotPickups == null || m_BotPickups.Length == 0) m_BotPickups = FindObjectsByType<SpikePickup>(FindObjectsSortMode.None);
            SpikePickup goal = null; float gd = 30f;
            foreach (SpikePickup pk in m_BotPickups)
            {
                if (pk == null || !pk.Available) continue;
                float d = Vector3.Distance(pk.transform.position, p);
                if (d < gd) { gd = d; goal = pk; }
            }
            if (goal != null) { Vector3 to2 = goal.transform.position - p; move = new Vector2(to2.x, to2.z).normalized; }
            else if (SpikeMatch.I != null)
            {
                int myTeam = SpikeMatch.TeamOf(netId);
                TankGame.Prototype.ControlPoint fp = null; float fd = float.MaxValue;
                foreach (var cp in SpikeMatch.I.Points)
                {
                    if (cp.Model.Owner == myTeam && cp.Model.OwnerHold > 0.6f) continue;     // ours and safe
                    float d = Vector3.Distance(cp.transform.position, p);
                    if (d < fd) { fd = d; fp = cp; }
                }
                if (fp != null)
                {
                    Vector3 to2 = fp.transform.position - p;
                    move = fd < 3f ? Vector2.zero : new Vector2(to2.x, to2.z).normalized;       // stand in the zone while it fills
                }
            }
            if (Time.time >= m_BotCheck)
            {
                m_BotCheck = Time.time + 1f;
                if (move.sqrMagnitude > 0.1f && (p - m_BotLastPos).magnitude < 1f) m_BotWanderUntil = Time.time + 1.5f;
                if (Time.time < m_BotWanderUntil) m_BotWanderDir = Random.insideUnitCircle.normalized;
                m_BotLastPos = p;
            }
            if (Time.time < m_BotWanderUntil) move = m_BotWanderDir;
            m_BotDash = move.sqrMagnitude > 0.1f && Time.time - m_BotLastDash > 5f;
            if (m_BotDash) m_BotLastDash = Time.time;
            SpikeTank other = null;
            foreach (SpikeTank o in All) if (o != this && o.RenderPos != Vector3.zero) { other = o; break; }
            if (other != null)
            {
                SpikeMetrics.DbgTargetSeen++;
                Vector3 to = other.RenderPos - m_Pred.pos;
                aimYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float ae = Mathf.Abs(Mathf.DeltaAngle(m_Pred.turretYaw, aimYaw));
                SpikeMetrics.DbgMinDist = Mathf.Min(SpikeMetrics.DbgMinDist, to.magnitude); SpikeMetrics.DbgMinAngle = Mathf.Min(SpikeMetrics.DbgMinAngle, ae);
                SpikeMetrics.DbgLastDist = to.magnitude; SpikeMetrics.DbgLastAngle = ae;
                fire = to.magnitude < 55f && ae < 6f;
            }
            else aimYaw = t * 90f;
        }

        // ------------------------------------------------------------------ cosmetics
        static TankGame.Prototype.CombatFx s_Fx;
        public static TankGame.Prototype.CombatFx Fx
        {
            get { if (s_Fx == null && !Application.isBatchMode) s_Fx = FindFirstObjectByType<TankGame.Prototype.CombatFx>(); return s_Fx; }
        }

        [ClientRpc(channel = Channels.Reliable)]
        void RpcHitFx(Vector3 pos) { if (Fx != null) Fx.SpawnImpact(pos, Vector3.up, true); }

        [ClientRpc(channel = Channels.Reliable)]
        void RpcExplodeFx(Vector3 pos) { if (Fx != null) Fx.SpawnExplosion(pos); }

        static readonly Color[] ShellColors = { new Color(1f, 0.92f, 0.5f), new Color(1f, 1f, 0.75f), new Color(1f, 0.6f, 0.2f), new Color(1f, 0.35f, 0.15f) };
        static readonly Vector3[] ShellScale = { Vector3.one, new Vector3(0.5f, 0.5f, 0.8f), new Vector3(0.6f, 0.6f, 0.5f), new Vector3(1.9f, 1.9f, 2.4f) };

        void SpawnShell(Vector3 origin, Vector3 dir, float dist, byte weapon)
        {
            if (Application.isBatchMode) return;                 // headless test clients draw nothing
            GameObject go;
            if (shellPrefab != null)
            {
                go = Instantiate(shellPrefab, origin, Quaternion.LookRotation(dir));
                var mr = go.GetComponentInChildren<MeshRenderer>();
                if (mr != null)
                {
                    mr.transform.localScale = new Vector3(0.28f * ShellScale[weapon].x, 0.28f * ShellScale[weapon].y, 1f * ShellScale[weapon].z);
                    var mpb = new MaterialPropertyBlock(); mr.GetPropertyBlock(mpb); mpb.SetColor("_BaseColor", ShellColors[weapon]); mr.SetPropertyBlock(mpb);
                }
                var trail = go.GetComponentInChildren<TrailRenderer>();
                if (trail != null) { trail.Clear(); trail.startColor = ShellColors[weapon]; trail.endColor = new Color(ShellColors[weapon].r, ShellColors[weapon].g * 0.5f, 0.1f, 0f); trail.widthMultiplier = ShellScale[weapon].x; }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(go.GetComponent<Collider>());
                go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(dir));
                go.transform.localScale = new Vector3(0.25f, 0.25f, 0.9f);
                if (shellMaterial != null) go.GetComponent<MeshRenderer>().sharedMaterial = shellMaterial;
            }
            var sh = go.AddComponent<SpikeShell>();
            sh.dir = dir; sh.remaining = dist; sh.speed = SpikeSim.Weapons[weapon].speed; sh.weapon = weapon;
            sh.endsOnWall = dist < SpikeSim.Weapons[weapon].range - 0.01f;
            if (Fx != null) Fx.SpawnMuzzle(origin, dir);
            if (flash != null) { flash.SetActive(true); CancelInvoke(nameof(HideFlash)); Invoke(nameof(HideFlash), 0.05f); }
        }

        void HideFlash() { if (flash != null) flash.SetActive(false); }
    }

    public sealed class SpikeShell : MonoBehaviour
    {
        public Vector3 dir;
        public float remaining, speed = SpikeSim.ShellSpeed;
        public byte weapon; public bool endsOnWall;
        void Update()
        {
            float step = speed * Time.deltaTime;
            transform.position += dir * Mathf.Min(step, remaining);
            remaining -= step;
            if (remaining <= 0f)
            {
                var fx = SpikeTank.Fx;
                if (fx != null && weapon == 3) fx.SpawnExplosion(transform.position);
                else if (fx != null && endsOnWall) fx.SpawnImpact(transform.position, -dir, false);
                Destroy(gameObject);
            }
        }
    }
}
