using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// "Gameplay owns this renderer's colour — do not skin it."
    ///
    /// The skin director claims every renderer underneath a damageable body, which is right for the
    /// bodies themselves and wrong for the handful of child renderers that gameplay drives frame by
    /// frame through a MaterialPropertyBlock of their own. The Mower Hutch's VulnerableCore is the
    /// one that matters: it is the pulsing "shoot here" tell, and two LateUpdates writing the same
    /// block — the skin's and the factory's — is a fight decided by script order, which means the
    /// core glows in the editor and renders flat grey in a build. That is exactly what shipped.
    ///
    /// A marker rather than a name check or a type check, because the rule isn't "the core is
    /// special", it's "whoever drives a block owns it". Anything else that starts driving its own
    /// tint gets this and is safe by construction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SelfDrivenTint : MonoBehaviour
    {
    }
}
