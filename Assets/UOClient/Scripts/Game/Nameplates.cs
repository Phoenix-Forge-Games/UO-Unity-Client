using System.Collections.Generic;
using UnityEngine;

namespace UOGame
{
    /// <summary>Something with a name over its head (players and NPCs), as a client sees it.</summary>
    public interface INamed
    {
        string DisplayName { get; }
        /// <summary>What the name floats over (the smoothed model).</summary>
        Transform Body { get; }
        int ObjectId { get; }
        /// <summary>How high the name sits above the body's feet (metres).</summary>
        float NameHeight { get; }
        Color NameColour { get; }
    }

    /// <summary>Everything this client shows a name for.</summary>
    public static class Nameplates
    {
        public static readonly List<INamed> Seen = new List<INamed>();
    }
}
