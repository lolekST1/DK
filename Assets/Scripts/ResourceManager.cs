using System;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// The economy's front desk. Gold itself lives on treasury and heart tiles in
    /// <see cref="RoomManager"/> — this wraps that in the one interface the rest of the game
    /// wants (spend, bank, subscribe) and keeps the lifetime tally the HUD uses to show how
    /// much mined gold never made it into a vault.
    /// </summary>
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        RoomManager _rooms;
        LooseGold _loose;

        /// <summary>Everything ever banked, including gold later spent. Never goes down.</summary>
        public int TotalBanked { get; private set; }

        /// <summary>
        /// Mined gold that has been dropped on the floor at some point, over the whole run.
        /// Never goes down, even after an imp fetches the pile back — it is a record of how
        /// often the vault ran out, not of what is lying around.
        /// </summary>
        public int TotalSpilled { get; private set; }

        /// <summary>Gold on the dungeon floor right now, waiting to be carried in.</summary>
        public int LooseGold => _loose != null ? _loose.Total : 0;

        public int Gold => _rooms != null ? _rooms.StoredGold : 0;

        public int Capacity => _rooms != null ? _rooms.StorageCapacity : 0;

        /// <summary>Carries (gold, capacity).</summary>
        public event Action<int, int> GoldChanged;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_rooms != null) _rooms.StorageChanged -= OnStorageChanged;
        }

        public void Configure(RoomManager rooms, LooseGold loose = null)
        {
            _rooms = rooms;
            _loose = loose;
            _rooms.StorageChanged += OnStorageChanged;
            OnStorageChanged(_rooms.StoredGold, _rooms.StorageCapacity);
        }

        void OnStorageChanged(int gold, int capacity) => GoldChanged?.Invoke(gold, capacity);

        /// <summary>Banks gold an imp hauled to a specific tile. Returns how much fit.</summary>
        public int Bank(Vector2Int cell, int amount)
        {
            if (_rooms == null || amount <= 0) return 0;

            int banked = _rooms.Deposit(cell, amount);
            TotalBanked += banked;
            return banked;
        }

        /// <summary>Records gold that was mined but had nowhere to go.</summary>
        public void ReportSpill(int amount)
        {
            if (amount > 0) TotalSpilled += amount;
        }

        /// <summary>Pays for something. All or nothing.</summary>
        public bool TrySpend(int amount) => _rooms != null && _rooms.TryWithdraw(amount);
    }
}
