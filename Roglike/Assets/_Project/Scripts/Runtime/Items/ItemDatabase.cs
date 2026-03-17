using System.Collections.Generic;
using UnityEngine;

namespace Project.Items
{
    public sealed class ItemDatabase : MonoBehaviour
    {
        public static ItemDatabase Instance { get; private set; }

        [SerializeField] private List<ItemDefinition> items = new List<ItemDefinition>();
        private Dictionary<int, ItemDefinition> _byId;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _byId = new Dictionary<int, ItemDefinition>();
            foreach (var it in items)
            {
                if (it == null) continue;
                _byId[it.ItemId] = it;
            }
        }

        public ItemDefinition Get(int id)
        {
            if (_byId != null && _byId.TryGetValue(id, out var it))
                return it;
            return null;
        }
    }
}