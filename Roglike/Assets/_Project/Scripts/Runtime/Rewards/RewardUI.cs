using Mirror;
using UnityEngine;
using UnityEngine.UI;
using Project.Player;

namespace Project.Rewards
{
    public sealed class RewardUI : MonoBehaviour
    {
        public static RewardUI Instance { get; private set; }

        [Header("Refs")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button option0;
        [SerializeField] private Button option1;
        [SerializeField] private Button option2;
        [SerializeField] private Text label0;
        [SerializeField] private Text label1;
        [SerializeField] private Text label2;
        [SerializeField] private Text statusText;

        private RewardOrb _currentOrb;
        private NetworkIdentity _localPlayerNi;

        private void Awake()
        {
            Instance = this;
            Hide();
        }

        private void Update()
        {
            if (root != null && root.activeSelf && Input.GetKeyDown(KeyCode.Escape))
                Hide();
        }

        public void ShowFor(RewardOrb orb, NetworkIdentity localPlayerNi)
        {
            _currentOrb = orb;
            _localPlayerNi = localPlayerNi;

            if (_currentOrb == null || _localPlayerNi == null)
                return;

            var a = _currentOrb.ClientGetOption(0);
            var b = _currentOrb.ClientGetOption(1);
            var c = _currentOrb.ClientGetOption(2);

            label0.text = ToText(a.rarity, a.kind, a.value);
            label1.text = ToText(b.rarity, b.kind, b.value);
            label2.text = ToText(c.rarity, c.kind, c.value);

            option0.onClick.RemoveAllListeners();
            option1.onClick.RemoveAllListeners();
            option2.onClick.RemoveAllListeners();

            option0.onClick.AddListener(() => Pick(0));
            option1.onClick.AddListener(() => Pick(1));
            option2.onClick.AddListener(() => Pick(2));

            root.SetActive(true);

            if (root != null)
                root.SetActive(true);
            else
                gameObject.SetActive(true);

            Project.UI.UIStateManager.Instance?.Push("RewardUI");
        }

        public void Hide()
        {
            Debug.Log($"[RewardUI] Hide called. root={(root != null ? root.name : "NULL")} thisGO={gameObject.name}");

            // если root не задан — выключаем сам объект RewardUI
            if (root != null)
                root.SetActive(false);
            else
                gameObject.SetActive(false);

            Project.UI.UIStateManager.Instance?.Pop("RewardUI");

            _currentOrb = null;
            _localPlayerNi = null;
        }

        private void Pick(int idx)
        {
            if (_currentOrb == null || _localPlayerNi == null)
                return;
            Debug.Log($"[RewardUI] Pick clicked idx={idx} localNetId={_localPlayerNi.netId}");
            _currentOrb.CmdPickReward(_localPlayerNi, idx);
            Hide();
        }

        // Вызывается TargetRpc-ом после успешного выбора на сервере
        public void ClientOnPickedConfirmed()
        {
            Hide();
        }

        public void ClientOnMetaCurrencyGained(int amount)
        {
            if (statusText != null)
                statusText.text = $"+{amount} Essence (meta)";

            Debug.Log($"[RewardUI] Meta currency gained +{amount}. Total={Project.Progression.MetaProgressionService.GetCurrency()}");
        }

        private string ToText(Project.Player.RewardRarity rarity, RewardKind kind, int value)
        {
            string r = rarity switch
            {
                Project.Player.RewardRarity.Common => "COMMON",
                Project.Player.RewardRarity.Rare => "RARE",
                Project.Player.RewardRarity.Epic => "EPIC",
                _ => "?"
            };

            string body = kind switch
            {
                RewardKind.MaxHpFlat => $"+{value} Max HP",
                RewardKind.DamagePct => $"+{value}% Damage",
                RewardKind.MoveSpeedPct => $"+{value}% Move Speed",
                RewardKind.LifestealPct => $"{value}% Lifesteal",
                RewardKind.ThornsFlat => $"+{value} Thorns",
                RewardKind.RegenPer10s => $"+{value} HP / 10s",
                RewardKind.BossDamagePct => $"+{value}% Boss Damage",
                _ => $"Reward {kind} {value}"
            };

            return $"[{r}] {body}";
        }
    }
}
