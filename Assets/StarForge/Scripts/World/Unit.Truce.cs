// Unit.Truce.cs -- the Night of the Quenching's truce, for one unit (GameWorld.Night). From the
// darkening to lauds no gun fires: UpdateCombat stops here (a soldier after his own thinking, so he
// still takes orders and cover and his suppression ebbs), and TryThrow and the camp's alarm ask
// GameWorld.Truce too.
using StarForge.Sim;

namespace StarForge.World
{
    public sealed partial class Unit
    {
        /// <summary>Hold fire: a target it picked up itself is dropped (an ordered attack's is kept:
        /// it walks to it and stands), and so is an order to fire at ground.</summary>
        void HoldFireForTruce()
        {
            if (!(order == Order.Attack && targetOrdered)) target = null;
            fireAtOrdered = false;
            blindFireUntil = -99f;
            fireBlocked = false;
            throwing = Thrown.None;   // a frag in its wind-up is not thrown (one already in the air lands)
        }
    }
}
