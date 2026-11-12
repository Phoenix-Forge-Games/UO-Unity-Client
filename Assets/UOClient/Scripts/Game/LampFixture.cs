using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// A light fixture, whole (Assets/Synty/UOLit/Fixtures, Light_*): its model, a flame effect on each wick or inside
    /// each lantern, the light in the middle of its flames, and a lantern's glass. Drop the prefab anywhere (a building
    /// prefab, the world through the bake's seeding, a scene) and it shines through <see cref="WorldLights"/>: lit when the
    /// camera can see it, flames and glowing glass while it burns, shadows when near. The bake keeps it whole
    /// (<see cref="UOClient.Bake.PlacedWhole"/>), so editing the prefab changes every copy without baking again.
    /// </summary>
    [RequireComponent(typeof(UOClient.Bake.PlacedWhole))]
    public sealed class LampFixture : MonoBehaviour
    {
        [Tooltip("The light, in the middle of the flames (WorldLights switches it on and off, and sets its shadows).")]
        public Light Light;

        [Tooltip("The flame effects (shown while it burns, near the camera).")]
        public GameObject Flames;

        [Tooltip("A lantern's glass panes: glow with the light's colour while it burns.")]
        public Renderer[] Glass = new Renderer[0];

        [Tooltip("Indoors: burns day and night (dimmer by day). Outdoors: lit at night only.")]
        public bool Indoor;

        [Tooltip("The light's brightness at full night.")]
        public float Intensity = 2f;

        [Tooltip("How much it flickers (0 steady, 1 a torch).")]
        [Range(0f, 1f)] public float Flicker = 0.4f;

        [Tooltip("May glow in the air in fog (volumetric, outdoor fixtures).")]
        public bool Glows = true;

        private void OnEnable() => WorldLights.Register(this);

        private void OnDisable() => WorldLights.Unregister(this);
    }
}
