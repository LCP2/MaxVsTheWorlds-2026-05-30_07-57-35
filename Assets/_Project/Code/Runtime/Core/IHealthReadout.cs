namespace MaxWorlds.Core
{
    /// <summary>
    /// Something that can show the player how close it is to dying (YT-111).
    ///
    /// Separate from <see cref="IDamageable"/> on purpose. IDamageable is about taking a hit — it is
    /// asked by weapons, and it deliberately says nothing about how much health is left, because
    /// nothing in combat needed to know. This is the readout: it exists so one bar can hang over
    /// Max and over every robot without knowing which it is standing on.
    /// </summary>
    public interface IHealthReadout
    {
        /// <summary>0..1, for the length of the bar.</summary>
        float HealthNormalized { get; }

        /// <summary>Current HP, for the number printed on it.</summary>
        float HealthCurrent { get; }

        /// <summary>What to call this unit above its bar.</summary>
        string ReadoutName { get; }

        /// <summary>False once it is dead, so the bar can take itself off the field.</summary>
        bool IsAlive { get; }
    }
}
