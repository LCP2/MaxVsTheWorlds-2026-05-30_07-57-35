using System.Collections.Generic;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A concealed knot of robots that wakes together (MV-363, AC5): the moment any one member gets
    /// sight of Max and calls <see cref="RobotEnemy.Activate"/> on itself, every other still-dormant
    /// member wakes with it — a hidden group reads as an ambush, not a trickle of individual spots.
    ///
    /// Fire-and-forget: built once by the spawner right after placing a concealed cluster, wired to
    /// each member's <see cref="RobotEnemy.WokeFromDormant"/>, and never touched again. Nothing keeps
    /// this object alive once every member has either woken or died — each robot's own
    /// <see cref="RobotEnemy.ResetState"/> clears its subscription on pooled reuse, so a spent group
    /// simply falls out of scope rather than leaking.
    /// </summary>
    public sealed class DormantGroup
    {
        private readonly List<RobotEnemy> _members = new List<RobotEnemy>(4);

        public void Add(RobotEnemy robot)
        {
            if (robot == null) return;
            _members.Add(robot);
            robot.WokeFromDormant += OnMemberWoke;
        }

        private void OnMemberWoke(RobotEnemy woken)
        {
            for (int i = 0; i < _members.Count; i++)
            {
                RobotEnemy m = _members[i];
                // Activate() is idempotent (a no-op off Dormant), so this is safe even for the
                // member that just woke itself and re-enters here via its own event.
                if (m != null && m.IsDormant) m.Activate();
            }
        }
    }
}
