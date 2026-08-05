using System;
using UnityEngine;

namespace DK
{
    public enum Outcome
    {
        Playing,
        Won,
        Lost,
    }

    /// <summary>
    /// Watches the two things that end a run: the dungeon heart falling, and the Lord of the
    /// Land dying in it. Everything else in the game is indifferent to who is winning, which is
    /// why this is a separate component and not a flag threaded through the managers.
    ///
    /// Stopping the world is done with the timescale rather than by disabling every AI in the
    /// scene: it is one line, it cannot miss anything that gets added later, and a frozen
    /// dungeon behind the result is the correct picture of what just happened.
    /// </summary>
    public class GameDirector : MonoBehaviour
    {
        public Outcome Result { get; private set; } = Outcome.Playing;

        public bool Finished => Result != Outcome.Playing;

        /// <summary>Raised once when the run ends.</summary>
        public event Action<Outcome> Ended;

        DungeonHeart _heart;
        HeroManager _heroes;

        public void Configure(DungeonHeart heart, HeroManager heroes)
        {
            _heart = heart;
            _heroes = heroes;

            if (_heart != null) _heart.Destroyed += OnHeartDestroyed;
        }

        void OnDestroy()
        {
            if (_heart != null) _heart.Destroyed -= OnHeartDestroyed;

            // Never leave the next run frozen because this one ended.
            if (Finished) Time.timeScale = 1f;
        }

        void Update()
        {
            if (Finished) return;

            // Losing is event-driven; winning is a state nobody raises an event for, because
            // the Lord is just another hero as far as the roster is concerned.
            if (_heroes != null && _heroes.LordDefeated) Finish(Outcome.Won);
        }

        void OnHeartDestroyed() => Finish(Outcome.Lost);

        /// <summary>Ends the run. Public so tests do not have to stage a whole siege.</summary>
        public void Finish(Outcome outcome)
        {
            if (Finished || outcome == Outcome.Playing) return;

            Result = outcome;
            Time.timeScale = 0f;
            Ended?.Invoke(outcome);
        }
    }
}
