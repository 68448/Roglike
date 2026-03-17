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
            Project.Progression.MetaProgressionService.TryBuy(Project.Progression.MetaUpgradeType.MaxHp);
            RefreshMetaUi();
        }

        public void OnBuyMetaDamageClicked()
        {
            Project.Progression.MetaProgressionService.TryBuy(Project.Progression.MetaUpgradeType.DamagePct);
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

            if (metaCurrencyText != null)
                metaCurrencyText.text = $"Essence: {currency}";

            if (metaHpText != null)
            {
                int hpBonus = Project.Progression.MetaProgressionService.GetMaxHpMetaBonus();
                metaHpText.text = $"Meta HP Lv.{hpLevel}  (+{hpBonus} Max HP)  Cost: {hpCost}";
            }

            if (metaDmgText != null)
            {
                int dmgBonus = Project.Progression.MetaProgressionService.GetDamageMetaBonusPct();
                metaDmgText.text = $"Meta Damage Lv.{dmgLevel}  (+{dmgBonus}% Damage)  Cost: {dmgCost}";
            }
        }
    }
}
