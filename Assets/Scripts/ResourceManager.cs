using System;
using UnityEngine;

namespace DK
{
    /// <summary>Gold counter. One int and an event — resource types beyond gold are out of scope.</summary>
    public class ResourceManager : MonoBehaviour
    {
        public static ResourceManager Instance { get; private set; }

        public int Gold { get; private set; }

        public event Action<int> GoldChanged;

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
        }

        public void AddGold(int amount)
        {
            if (amount == 0) return;

            Gold = Mathf.Max(0, Gold + amount);
            GoldChanged?.Invoke(Gold);
        }
    }
}
