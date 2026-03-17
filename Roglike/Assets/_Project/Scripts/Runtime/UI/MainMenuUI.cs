using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class MainMenuUI : MonoBehaviour
    {
        [Header("Scenes")]
        [SerializeField] private string lobbySceneName = "Lobby";

        [Header("Meta Progression (Optional UI)")]
        [SerializeField] private Text metaCurrencyText;
        [SerializeField] private Text metaHpText;
        [SerializeField] private Text metaDmgText;
        [SerializeField] private Text metaHpNextText;
        [SerializeField] private Text metaDmgNextText;
        [SerializeField] private Button buyMetaHpButton;
        [SerializeField] private Button buyMetaDamageButton;

        private void Start()
        {
            RefreshMetaUi();
        }

        public void OnHostClicked()
        {
            Project.Core.LaunchParams.IsHost = true;
            SceneManager.LoadScene(lobbySceneName);
        }

        public void OnJoinClicked()
        {
            Project.Core.LaunchParams.IsHost = false;
            Project.Core.LaunchParams.Address = "localhost"; // позже сделаем поле ввода
            SceneManager.LoadScene(lobbySceneName);
        }

        public void OnQuitClicked()
        {
            Application.Quit();
        }

        public void OnBuyMetaHpClicked()
        {
            bool bought = Project.Progression.MetaProgressionService.TryBuy(Project.Progression.MetaUpgradeType.MaxHp);
            if (!bought)
                Debug.Log("[MainMenuUI] Buy Meta HP failed (not enough Essence or max level).");

            RefreshMetaUi();
        }

        public void OnBuyMetaDamageClicked()
        {
            bool bought = Project.Progression.MetaProgressionService.TryBuy(Project.Progression.MetaUpgradeType.DamagePct);
            if (!bought)
                Debug.Log("[MainMenuUI] Buy Meta Damage failed (not enough Essence or max level).");

            RefreshMetaUi();
        }

        public void OnResetMetaProgressionClicked()
        {
            Project.Progression.MetaProgressionService.ResetAll();
            RefreshMetaUi();
        }

        private void RefreshMetaUi()
        {
            int currency = Project.Progression.MetaProgressionService.GetCurrency();

            int hpLevel = Project.Progression.MetaProgressionService.GetLevel(Project.Progression.MetaUpgradeType.MaxHp);
            int dmgLevel = Project.Progression.MetaProgressionService.GetLevel(Project.Progression.MetaUpgradeType.DamagePct);

            int hpCost = Project.Progression.MetaProgressionService.GetUpgradeCost(Project.Progression.MetaUpgradeType.MaxHp, hpLevel);
            int dmgCost = Project.Progression.MetaProgressionService.GetUpgradeCost(Project.Progression.MetaUpgradeType.DamagePct, dmgLevel);

            bool hpMaxed = hpLevel >= Project.Progression.MetaProgressionService.MaxLevelPerUpgrade;
            bool dmgMaxed = dmgLevel >= Project.Progression.MetaProgressionService.MaxLevelPerUpgrade;

            if (metaCurrencyText != null)
                metaCurrencyText.text = $"Essence: {currency}";

            if (metaHpText != null)
            {
                int hpBonus = Project.Progression.MetaProgressionService.GetMaxHpMetaBonus();
                metaHpText.text = hpMaxed
                    ? $"Meta HP Lv.{hpLevel}  (+{hpBonus} Max HP)  [MAX]"
                    : $"Meta HP Lv.{hpLevel}  (+{hpBonus} Max HP)  Cost: {hpCost}";
            }

            if (metaDmgText != null)
            {
                int dmgBonus = Project.Progression.MetaProgressionService.GetDamageMetaBonusPct();
                metaDmgText.text = dmgMaxed
                    ? $"Meta Damage Lv.{dmgLevel}  (+{dmgBonus}% Damage)  [MAX]"
                    : $"Meta Damage Lv.{dmgLevel}  (+{dmgBonus}% Damage)  Cost: {dmgCost}";
            }

            if (metaHpNextText != null)
            {
                metaHpNextText.text = hpMaxed
                    ? "Next: MAX"
                    : $"Next: +{Project.Progression.MetaProgressionService.HpPerLevel} Max HP";
            }

            if (metaDmgNextText != null)
            {
                metaDmgNextText.text = dmgMaxed
                    ? "Next: MAX"
                    : $"Next: +{Project.Progression.MetaProgressionService.DamagePctPerLevel}% Damage";
            }

            if (buyMetaHpButton != null)
                buyMetaHpButton.interactable = !hpMaxed && currency >= hpCost;

            if (buyMetaDamageButton != null)
                buyMetaDamageButton.interactable = !dmgMaxed && currency >= dmgCost;
        }
    }
}
