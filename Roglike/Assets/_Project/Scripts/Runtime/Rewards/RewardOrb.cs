using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Project.Player;

namespace Project.Rewards
{
    public sealed class RewardOrb : NetworkBehaviour
    {
        [Header("Sync")]
        [SyncVar] public int SegmentIndex;
        [SyncVar] public int Enc0;
        [SyncVar] public int Enc1;
        [SyncVar] public int Enc2;

        private readonly HashSet<uint> _picked = new HashSet<uint>();

        [Server]
        public void ServerInit(int segmentIndex, int seed)
        {
            SegmentIndex = segmentIndex;

            var rng = new System.Random(seed ^ 0x51EDC0DE);
            var a = NextReward(rng, segmentIndex);
            var b = NextReward(rng, segmentIndex);
            var c = NextReward(rng, segmentIndex);

            Enc0 = Encode(a.rarity, a.kind, a.value);
            Enc1 = Encode(b.rarity, b.kind, b.value);
            Enc2 = Encode(c.rarity, c.kind, c.value);

            Debug.Log($"[RewardOrb] Init seg={segmentIndex} enc=[{Enc0},{Enc1},{Enc2}]");
        }

        [Command(requiresAuthority = false)]
        public void CmdPickReward(NetworkIdentity playerNi, int optionIndex, NetworkConnectionToClient sender = null)
        {
            if (!NetworkServer.active)
                return;

            if (playerNi == null || !playerNi.CompareTag("Player"))
                return;

            if (_picked.Contains(playerNi.netId))
                return;

            float dist = Vector3.Distance(playerNi.transform.position, transform.position);
            if (dist > 4.0f)
                return;

            int enc = optionIndex switch
            {
                0 => Enc0,
                1 => Enc1,
                2 => Enc2,
                _ => Enc0
            };

            var (rarity, kind, value) = Decode(enc);

            var stats = playerNi.GetComponent<PlayerStats>();
            if (stats == null)
                return;

            stats.ServerApplyReward(kind, value);
            _picked.Add(playerNi.netId);

            int metaCurrency = GetMetaCurrencyReward(rarity);
            stats.ServerAddRunEssence(metaCurrency);
            var conn = sender ?? playerNi.connectionToClient;
            if (conn != null)
            {
                TargetConfirmPicked(conn);
            }

            TryDestroyIfAllPicked();

            Debug.Log(
                $"[RewardOrb] Pick ok playerNetId={playerNi.netId} option={optionIndex} " +
                $"kind={kind} value={value} meta+={metaCurrency}");
        }

        [TargetRpc]
        private void TargetConfirmPicked(NetworkConnectionToClient target)
        {
            RewardUI.Instance?.ClientOnPickedConfirmed();
        }

        private static int Encode(Project.Player.RewardRarity r, RewardKind k, int value)
        {
            int rr = ((int)r & 0xF) << 28;
            int kk = ((int)k & 0xFF) << 20;
            int vv = Mathf.Clamp(value, 0, 0x000FFFFF);
            return rr | kk | vv;
        }

        private static (Project.Player.RewardRarity rarity, RewardKind kind, int value) Decode(int enc)
        {
            int rr = (enc >> 28) & 0xF;
            int kk = (enc >> 20) & 0xFF;
            int vv = enc & 0x000FFFFF;
            return ((Project.Player.RewardRarity)rr, (RewardKind)kk, vv);
        }

        private static (Project.Player.RewardRarity rarity, RewardKind kind, int value) NextReward(System.Random rng, int segmentIndex)
        {
            double rareChance = 0.15 + (segmentIndex * 0.005);
            double epicChance = 0.05 + (segmentIndex * 0.002);

            rareChance = System.Math.Min(0.45, rareChance);
            epicChance = System.Math.Min(0.20, epicChance);

            double roll = rng.NextDouble();
            var rarity = Project.Player.RewardRarity.Common;
            if (roll < epicChance) rarity = Project.Player.RewardRarity.Epic;
            else if (roll < epicChance + rareChance) rarity = Project.Player.RewardRarity.Rare;

            int hpBase = 10 + (segmentIndex / 5) * 5;
            int pctBase = 10 + (segmentIndex / 10) * 5;

            float mult = rarity switch
            {
                Project.Player.RewardRarity.Common => 1.0f,
                Project.Player.RewardRarity.Rare => 1.6f,
                Project.Player.RewardRarity.Epic => 2.3f,
                _ => 1.0f
            };

            int pick = rng.Next(0, 100);

            if (rarity == Project.Player.RewardRarity.Common)
            {
                if (pick < 34) return (rarity, RewardKind.MaxHpFlat, Mathf.RoundToInt(hpBase * mult));
                if (pick < 67) return (rarity, RewardKind.DamagePct, Mathf.RoundToInt(pctBase * mult));
                return (rarity, RewardKind.MoveSpeedPct, Mathf.RoundToInt(pctBase * mult));
            }

            if (rarity == Project.Player.RewardRarity.Rare)
            {
                if (pick < 25) return (rarity, RewardKind.MaxHpFlat, Mathf.RoundToInt(hpBase * mult));
                if (pick < 50) return (rarity, RewardKind.DamagePct, Mathf.RoundToInt(pctBase * mult));
                if (pick < 70) return (rarity, RewardKind.BossDamagePct, Mathf.RoundToInt((pctBase + 10) * mult));
                return (rarity, RewardKind.RegenPer10s, Mathf.RoundToInt((6 + segmentIndex / 5) * mult));
            }

            if (pick < 25) return (rarity, RewardKind.LifestealPct, Mathf.Clamp(Mathf.RoundToInt(5 * mult), 3, 20));
            if (pick < 50) return (rarity, RewardKind.ThornsFlat, Mathf.RoundToInt((8 + segmentIndex / 3) * mult));
            if (pick < 75) return (rarity, RewardKind.DamagePct, Mathf.RoundToInt((pctBase + 10) * mult));
            return (rarity, RewardKind.MaxHpFlat, Mathf.RoundToInt((hpBase + 10) * mult));
        }

        private static int GetMetaCurrencyReward(Project.Player.RewardRarity rarity)
        {
            return rarity switch
            {
                Project.Player.RewardRarity.Common => 1,
                Project.Player.RewardRarity.Rare => 2,
                Project.Player.RewardRarity.Epic => 4,
                _ => 1
            };
        }

        public (Project.Player.RewardRarity rarity, RewardKind kind, int value) ClientGetOption(int idx)
        {
            int enc = idx switch { 0 => Enc0, 1 => Enc1, 2 => Enc2, _ => Enc0 };
            return Decode(enc);
        }

        [Server]
        private void TryDestroyIfAllPicked()
        {
            int totalPlayers = 0;

            foreach (var kv in NetworkServer.connections)
            {
                var conn = kv.Value;
                if (conn == null || conn.identity == null) continue;
                if (!conn.identity.CompareTag("Player")) continue;
                totalPlayers++;
            }

            if (totalPlayers > 0 && _picked.Count >= totalPlayers)
            {
                Debug.Log($"[RewardOrb] All picked ({_picked.Count}/{totalPlayers}). Destroy orb.");
                NetworkServer.Destroy(gameObject);
            }
        }
    }
}
