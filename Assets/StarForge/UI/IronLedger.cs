// IronLedger.cs — whose names are scratched inside each soldier's iron (lore section 6: iron is
// issued, mended and issued again; a fallen soldier's goes back to the Host with their name in
// it, or is taken by another wing and painted over). Presentation only: it listens to the
// world's events, writes nothing to a Unit, and draws no number from any match stream (names
// come from a hash of the map's seed and the unit's id). One per HUD, so nothing outlives a match.
using System.Collections.Generic;
using System.Text;
using StarForge.World;

namespace StarForge.UI
{
    public sealed class IronLedger
    {
        sealed class Iron
        {
            public readonly List<string> names = new List<string>(8);   // oldest first; the wearer's own last
            public bool painted;       // taken from another wing's dead
            public int freeAfter;      // the newest unit id alive when it came back: only later recruits may wear it
            public string line;        // the card's text, built once per wearer
        }

        // Given names only, as a soldier scratches them: women and men serve alike (lore section 9).
        static readonly string[] Given =
        {
            "Ilse", "Odo", "Wend", "Maren", "Bram", "Tilde", "Osk", "Hanne", "Joss", "Kerra",
            "Milo", "Aldith", "Perrin", "Gerta", "Lenz", "Marit", "Tam", "Edda", "Corin", "Brida",
            "Wulf", "Senna", "Hob", "Ysolt", "Rhea", "Alder", "Mirel", "Garrick", "Thea", "Jorund",
            "Elke", "Pell", "Anka", "Rolf", "Sabin", "Greta", "Oswin", "Nell", "Dunstan", "Vesna",
        };

        /// <summary>Who stands nearest a fallen soldier keeps the iron: the side with more soldiers this close.</summary>
        public const float TakeRadius = 15f;
        const int StoreMax = 48;    // irons waiting for a recruit, a side
        const int NamesShown = 6;   // the newest ones; older scratches are worn smooth

        readonly Dictionary<int, Iron> worn = new Dictionary<int, Iron>();
        readonly List<Iron>[] store = { new List<Iron>(), new List<Iron>() };
        readonly StringBuilder sb = new StringBuilder(96);
        uint seed;

        /// <summary>Counts for the trial (`LoreTrials.Iron`).</summary>
        public int Issued, Reissued, Painted, Returned;

        bool begun;
        float lastTime;

        /// <summary>A new match starts a new ledger: told by the map's seed, or by match time gone
        /// back (a match started again on the same map). Read from the world it is handed, never at
        /// the HUD's OnEnable: the map may not be there yet (lore: every match is its own field).</summary>
        void Sync(GameWorld w)
        {
            if (w == null) return;
            uint s = w.Map != null ? w.Map.seed : 0u;
            if (!begun || s != seed || w.time + 1f < lastTime) Begin(s);
            lastTime = w.time;
        }

        public void Begin(uint mapSeed)
        {
            begun = true;
            lastTime = 0f;
            seed = mapSeed;
            worn.Clear();
            store[0].Clear();
            store[1].Clear();
            Issued = Reissued = Painted = Returned = 0;
        }

        /// <summary>A soldier in the iron: infantry of either side (never a Mech, a Digger or a creature).</summary>
        public static bool Wears(Unit u) =>
            u != null && u.def != null && u.def.bodyRadius > 0f && u.mech == null && u.team >= 0 && u.team < 2;

        public void Note(GameEvent e, GameWorld w)
        {
            if (!Wears(e.unit)) return;
            Sync(w);
            if (e.kind == GameEventKind.UnitReady) Wear(e.unit.id, e.unit.team);
            else if (e.kind == GameEventKind.Death) Fallen(e.unit, w);
        }

        /// <summary>The card's line for one soldier, or null for anything that wears no iron.</summary>
        public string Line(Unit u, GameWorld w)
        {
            if (!Wears(u)) return null;
            Sync(w);
            return Line(u.id, u.team);
        }

        public string Line(int id, int team)
        {
            var iron = Wear(id, team);
            if (iron.line != null) return iron.line;
            sb.Clear();
            int own = iron.names.Count - 1;
            sb.Append(iron.names[own]);
            if (iron.painted)
                // Soldiers do not talk about whose names are inside another wing's iron.
                sb.Append(" · painted-over iron: ").Append(own).Append(own == 1 ? " name inside, another wing's" : " names inside, another wing's");
            else if (own > 0)
            {
                sb.Append(" · in this iron before: ");
                int from = own > NamesShown ? own - NamesShown : 0;
                for (int i = own - 1; i >= from; i--)
                {
                    sb.Append(iron.names[i]);
                    if (i > from) sb.Append(", ");
                }
                if (from > 0) sb.Append(" and ").Append(from).Append(" worn smooth");
            }
            return iron.line = sb.ToString();
        }

        /// <summary>The iron a soldier wears, issued on first asking: one that came back before
        /// they were raised (the oldest such), else one from the Host's stores with its old names.</summary>
        Iron Wear(int id, int team)
        {
            if (worn.TryGetValue(id, out var iron)) return iron;
            var waiting = store[team];
            for (int i = 0; i < waiting.Count; i++)
                if (waiting[i].freeAfter < id)
                {
                    iron = waiting[i];
                    waiting.RemoveAt(i);
                    Reissued++;
                    if (iron.painted) Painted++;
                    break;
                }
            if (iron == null)
            {
                iron = new Iron();
                uint h = Hash(seed, (uint)id);
                int before = 1 + (int)(h % 4u);   // one to four wearers before this match
                for (int k = 0; k < before; k++) iron.names.Add(Given[Hash(h, (uint)k + 11u) % (uint)Given.Length]);
            }
            Issued++;
            iron.line = null;
            iron.names.Add(Given[Hash(seed ^ 0x9E3779B9u, (uint)id) % (uint)Given.Length]);
            worn[id] = iron;
            return iron;
        }

        void Fallen(Unit u, GameWorld w)
        {
            int friends = 0, foes = 0, newest = u.id;
            float r2 = TakeRadius * TakeRadius;
            var units = w.units;
            for (int i = 0; i < units.Count; i++)
            {
                var o = units[i];
                if (o == null) continue;
                if (o.id > newest) newest = o.id;
                if (o == u || o.hp <= 0f || !Wears(o) || o.deserted) continue;
                if ((o.pos - u.pos).sqrMagnitude > r2) continue;
                if (o.team == u.team) friends++; else foes++;
            }
            Fallen(u.id, u.team, foes > friends ? 1 - u.team : u.team, newest);
        }

        /// <summary>A soldier's iron goes to <paramref name="toTeam"/>'s stores (their own side's, or
        /// the other wing's, which paints it over). <paramref name="newestId"/>: the newest unit alive.</summary>
        public void Fallen(int id, int team, int toTeam, int newestId)
        {
            var iron = Wear(id, team);
            worn.Remove(id);
            iron.line = null;
            if (toTeam != team) iron.painted = true;   // once painted over it stays another wing's iron
            iron.freeAfter = newestId;
            var waiting = store[toTeam];
            if (waiting.Count >= StoreMax) waiting.RemoveAt(0);
            waiting.Add(iron);
            Returned++;
        }

        /// <summary>How many irons wait in a side's stores (the trial's).</summary>
        public int Waiting(int team) => store[team].Count;

        static uint Hash(uint a, uint b)
        {
            uint h = a * 0x85EBCA6Bu ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2));
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }
}
