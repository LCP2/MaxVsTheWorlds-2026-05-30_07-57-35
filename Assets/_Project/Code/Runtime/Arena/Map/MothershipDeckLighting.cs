using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>The live counterpart of <see cref="MothershipDecks"/> (MV-1249, ticket change item 1):
    /// as Max physically crosses into an area belonging to a different deck, blends
    /// <see cref="BackyardLighting"/> from the deck he left to the deck he entered over
    /// <see cref="BlendSeconds"/>, using the same <see cref="BackyardLook.Lerp"/> primitive
    /// <c>WorldJoinSequence.ApplyCorridorLighting</c> already uses for the World-to-World joining
    /// corridor. Only ever instantiated for the Mothership (see <c>BackyardPath.Awake</c>), so no world
    /// check lives here — a fresh instance assumes every crossing it hears about is a deck crossing.</summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("world")]
    public sealed class MothershipDeckLighting : MonoBehaviour
    {
        private const float BlendSeconds = 0.6f;

        private AreaAccumulationDirector _areaDirector;
        private BackyardLighting _lighting;
        private MothershipDeck _currentDeck;
        private BackyardLook _from;
        private BackyardLook _to;
        private float _t = 1f;   // 1 = no blend in progress

        /// <summary>Called once from <c>BackyardPath.Awake</c>, after the Mothership's own
        /// <see cref="AreaAccumulationDirector"/> exists. The starting deck is applied already (the
        /// catalog row's own <c>Look</c> — see <c>WorldCatalog</c>'s Mothership row), so this only
        /// needs to start listening for the NEXT crossing.</summary>
        public void Configure(AreaAccumulationDirector areaDirector)
        {
            _areaDirector = areaDirector;
            _currentDeck = MothershipDecks.ForArea(areaDirector.CurrentArea);
            // Seeds _to at the starting deck's own look with _t already at 1 (settled, no blend in
            // flight), so the FIRST real crossing's _from below resolves to the starting deck rather
            // than an unset, all-black default(BackyardLook).
            _to = MothershipDecks.LookFor(areaDirector.CurrentArea);
            _t = 1f;
            areaDirector.PlayerCrossedIntoArea += OnPlayerCrossedIntoArea;
        }

        private void OnPlayerCrossedIntoArea(int areaIndex)
        {
            MothershipDeck deck = MothershipDecks.ForArea(areaIndex);
            if (deck == _currentDeck) return;

            // Start the new blend from wherever the light actually is right now — the previous blend's
            // target if it had already settled, or its own in-flight interpolated value if Max crossed
            // two deck boundaries within one blend window.
            _from = _t >= 1f ? _to : BackyardLook.Lerp(_from, _to, _t);
            _to = MothershipDecks.LookFor(areaIndex);
            _currentDeck = deck;
            _t = 0f;
        }

        private void Update()
        {
            if (_t >= 1f) return;
            if (_lighting == null) _lighting = FindFirstObjectByType<BackyardLighting>();
            if (_lighting == null) return;

            _t = Mathf.Min(1f, _t + Time.deltaTime / BlendSeconds);
            _lighting.Apply(BackyardLook.Lerp(_from, _to, _t));
        }

        private void OnDestroy()
        {
            if (_areaDirector != null) _areaDirector.PlayerCrossedIntoArea -= OnPlayerCrossedIntoArea;
        }
    }
}
