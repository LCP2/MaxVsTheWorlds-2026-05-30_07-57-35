using MaxWorlds.Rendering;

namespace MaxWorlds.Arena
{
    /// <summary>World 5's own deck grouping (MV-1249) — level-design knowledge (which area belongs to
    /// which deck), so it lives here in Arena rather than in Rendering, the same split
    /// <see cref="WorldCatalog"/> already draws between "what a world looks like" (Rendering's
    /// <see cref="BackyardLook"/>) and "which world/area resolves to it" (this assembly). Area ranges
    /// match the ticket's own deck identities and <c>mothership_config.json</c>'s area names exactly:
    /// Hangar 1-5, Assembly 6-12, Holds 13-18, Engineering 19-24, Command 25-30. Area 0 (the entry-path
    /// scaffold every world carries) falls into Hangar — it is the Hangar Bay's own doorstep.</summary>
    public enum MothershipDeck
    {
        Hangar,
        Assembly,
        Holds,
        Engineering,
        Command,
    }

    /// <summary>Resolves a Mothership area index to its deck and look (MV-1249). A pure function over
    /// the ticket's own authored deck boundaries — called directly by the EditMode test (AC1) and by
    /// the MV-1249 capture preset's own per-shot Setup (each hero area's shot applies the matching look
    /// itself rather than simulating Max walking there), and also by <see cref="MothershipDeckLighting"/>
    /// for the live in-game switch as Max actually crosses a deck boundary.</summary>
    public static class MothershipDecks
    {
        public static MothershipDeck ForArea(int areaIndex)
        {
            if (areaIndex <= 5) return MothershipDeck.Hangar;
            if (areaIndex <= 12) return MothershipDeck.Assembly;
            if (areaIndex <= 18) return MothershipDeck.Holds;
            if (areaIndex <= 24) return MothershipDeck.Engineering;
            return MothershipDeck.Command;
        }

        public static BackyardLook LookFor(int areaIndex) => ForArea(areaIndex) switch
        {
            MothershipDeck.Hangar => BackyardLook.ShipHangar,
            MothershipDeck.Assembly => BackyardLook.ShipAssembly,
            MothershipDeck.Holds => BackyardLook.ShipHolds,
            MothershipDeck.Engineering => BackyardLook.ShipEngineering,
            _ => BackyardLook.ShipCommand,
        };
    }
}
