using System;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// The thing there is to lose. The heart sits on the battlefield as a structure: it never
    /// moves and never swings back, it only has health, and when that runs out the run is over.
    ///
    /// Ordinary raiders ignore it — a knight is here for the gold, and hacking at the masonry
    /// while the vault stands open would make no sense. Only what came for the heart looks for
    /// it, which is what makes the Lord of the Land a different kind of problem rather than a
    /// knight with more health.
    /// </summary>
    public class DungeonHeart : MonoBehaviour, ICombatant
    {
        /// <summary>
        /// Enough that a lone Lord takes the best part of a minute to bring it down, so a
        /// player who has creatures anywhere on the map gets a chance to answer.
        /// </summary>
        public const int DefaultHealth = 400;

        public int MaxHealth { get; private set; } = DefaultHealth;

        public int Health { get; private set; } = DefaultHealth;

        public bool IsAlive => Health > 0;

        /// <summary>0 when untouched, 1 when destroyed.</summary>
        public float Damage => MaxHealth > 0 ? 1f - Health / (float)MaxHealth : 1f;

        Side ICombatant.Side => Side.Dungeon;

        /// <summary>The heart's centre tile — what a besieger walks to.</summary>
        public Vector2Int Cell => _cell;

        Vector2Int ICombatant.Cell => _cell;

        Vector3 ICombatant.Position => _core != null ? _core.position : transform.position;

        bool ICombatant.IsStructure => true;

        /// <summary>Raised once, when the last point of health goes.</summary>
        public event Action Destroyed;

        /// <summary>Raised on every hit. Carries (health, maxHealth).</summary>
        public event Action<int, int> Damaged;

        Battlefield _battlefield;
        Transform _core;
        Renderer _coreRenderer;
        MaterialPropertyBlock _propertyBlock;
        Vector2Int _cell;

        static readonly Color Healthy = new Color(0.90f, 0.15f, 0.22f);
        static readonly Color Failing = new Color(0.20f, 0.18f, 0.20f);

        public void Configure(Battlefield battlefield, Vector2Int cell, Transform core, int maxHealth)
        {
            _battlefield = battlefield;
            _cell = cell;
            _core = core;
            _coreRenderer = core != null ? core.GetComponent<Renderer>() : null;
            _propertyBlock = new MaterialPropertyBlock();

            MaxHealth = Mathf.Max(1, maxHealth);
            Health = MaxHealth;

            if (_battlefield != null) _battlefield.Register(this);
            ApplyTint();
        }

        void OnDestroy()
        {
            if (_battlefield != null) _battlefield.Unregister(this);
        }

        public void TakeDamage(int amount, ICombatant from)
        {
            if (amount <= 0 || !IsAlive) return;

            Health = Mathf.Max(0, Health - amount);
            ApplyTint();

            Damaged?.Invoke(Health, MaxHealth);
            if (Health == 0) Destroyed?.Invoke();
        }

        /// <summary>The core dims towards dead stone as it is broken, so damage reads on the map.</summary>
        void ApplyTint()
        {
            if (_coreRenderer == null) return;

            _coreRenderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, Color.Lerp(Healthy, Failing, Damage));
            _coreRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
