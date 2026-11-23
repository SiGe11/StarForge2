// LoreText.cs — the world as the player is told it: the Field Manual's Chronicle page, the
// main menu's lore tips and the orders' mottos. Canon and voice: docs/lore/starforge-lore.md
// (section 13); Old Language text: docs/lore/old-language.md. Nothing here may answer what the
// lore's section 11 leaves open, or take a wing's side; a tip states a game rule, so check it
// against the rule it names when that rule changes.
namespace StarForge.UI
{
    public static class LoreText
    {
        /// <summary>Paragraphs split by a blank line; each one's first sentence is its
        /// heading (the form <c>HUDController.OpponentHelp</c> has).</summary>
        public const string Chronicle =
            "The Bright Age. A thousand years ago the Makers worked starfire. It was not sorcery but a craft, and its people were " +
            "smiths: they held that it was sunlight brought down to the anvil. A thing forged in it kept a purpose of its own -- " +
            "a wall that meant to stand, a plough that meant to cut straight, a machine that meant to guard. No Maker's name " +
            "survives, only their work, and every fire they lit was kindled from one hearth: the Starforge.\n\n" +

            "The Quenching. In one night every starfire went out. Nine watches were kept over the Starforge that night, and the " +
            "fire failed during the ninth. The purposed things kept their purpose and lost the power to act on it alone. The " +
            "clergy say the fire was lent by the stars and has gone home; the Khan's chroniclers, that it was spent in a war " +
            "nobody recorded; the knightly orders, that the Makers put it out themselves. Nobody has proven any of it. The age " +
            "since is called the Long Night.\n\n" +

            "The two hands. The Empire of the Brightest Star is seven centuries old and is ruled, in law, from the star itself, " +
            "where nobody lives. Two hands govern in its name and may never become one: the Khan, who holds the Host, the courts " +
            "and the treasury, and the Holy Mother, who blesses, and who alone elects each Khan. Only a man may be Khan, and only " +
            "a woman may be Mother; the whole clergy are women. Their bargain is the Compact. What is a matter of both the sword " +
            "and the light must carry both seals.\n\n" +

            "The Host. By old custom the Empire's army is kept in equal wings, so that no captain ever commands all of it. There " +
            "were two at the founding, Blue and Red; a later Khan, wary of his own Host, split them into four, adding Green and " +
            "Yellow. The same equipment, the same ranks, the same oath: by the hand that holds and the hand that blesses.\n\n" +

            "The iron. Every soldier fights in the iron: a full suit of plate with no helm, the eyes behind a slit in the " +
            "breastplate, the shoulders in the wing's colour and two masts on the back to carry the field net. In the field " +
            "nobody's face is seen, as the Makers left no faces. Iron is handed down, and inside each breastplate are the names " +
            "of those who wore it before. Under every wing's paint is the same grey.\n\n" +

            "Vesper. A quiet farm-world of birch and spruce, wolf, fox and eagle, named for the hour: from here the Brightest " +
            "Star is the evening star. A hundred years ago its smallholders were cleared off the land for ore concessions that " +
            "failed. The field walls and house corners are what they left, and many soldiers of every wing are their " +
            "great-grandchildren.\n\n" +

            "Teal. Its proper name is quenchstone. It is found only on worlds where starfire was once worked, it holds no magic, " +
            "and it is the finest feedstock known for the printworks that raise a Foundry in minutes. Its seams lie in facing " +
            "pairs, as if cast from one pour, and on Vesper every seam runs back toward one origin. The surveyors' conclusion: " +
            "the Starforge is here.\n\n" +

            "The quarrel. Asked what should be done, the Muster split four ways, and the split fell along the wings. Blue, the " +
            "Kindlers, would find the Starforge and relight it. Red, the Keepers, would seal it cold. Green, the Bearers, would " +
            "carry it to Matins for the two hands to decide. Yellow, the Returners, would give it back to the orders. Blue and " +
            "Red landed first, and Green and Yellow a season later. Either the Khan or the Holy Mother could end it with a " +
            "sentence, and neither has spoken. So no wing is a rebel, and in law there is no war: only a disputed custody.\n\n" +

            "The orders. The eight knightly orders are older than the Empire and swear to no wing. At the heart of each of " +
            "their war machines sits a core, a purposed mind forged in the Bright Age, which can think, remember and advise but " +
            "cannot move a limb without a sworn human hand. Nobody can make another. Only men are sworn as knights, and a knight " +
            "gives up his house and is known by his birth-star. By the Gantry Right, any lawful host that raises a gantry is owed " +
            "one knight and one core, once, for that field: your Mech Bay is that gantry, a cradle and a beacon. A fallen Mech " +
            "is not replaced, and a gantry that falls before the drop cancels the summons.\n\n" +

            "Two voices. By the orders' teaching, your Mech's calls and reports are the knight's, and its quiet quoted sayings " +
            "are the core's, which only the knight hears and passes on. Sceptics hold that there is one voice and a book of " +
            "sayings. Knights who are asked say \"we\" and change the subject.\n\n" +

            "The licensed lance. Only an order may sanction a weapon built to kill a knight. The Sable Oath made the lance, and " +
            "it has two purposes: the knight who forgets the oath, and the cheap copies of knights the old houses built. So a " +
            "Mech-Hunter flies an order's flag, is crewed by its serjeants, and fells an Ironclad as readily as a Mech. Each " +
            "lance is granted once; when it falls, the host waits on a new grant.\n\n" +

            "The enemy who knows you. Before the split, the Host's Annalists kept a file on every officer. Each wing took its " +
            "share of the archive and all of the habit. The officer across the field is served by a staff that studied you in " +
            "peacetime, sees only what its own units see, and amends your file after every engagement.\n\n" +

            "Oaths. Every soldier swears the Host-oath, and fear breaks some of them: the broken go to ground behind their own " +
            "base. A soldier who proves themselves takes a second, stricter oath before a knight and is given the cape in the " +
            "wing's colour, trimmed in bronze and later in gold; nobody wearing one is known to have run. Knights are the keepers " +
            "of oaths: whether your Mech's knight hunts deserters or spares them is his own conscience.\n\n" +

            "Two tongues. Everyone speaks the Vulgar, on the field net as at home. The Old Language, which the clergy call the " +
            "High Speech, is the tongue of the Bright Age, kept for the liturgy, titles, mottos and oaths. Every soldier swears " +
            "in it: \"Brihteste Sterreþe under, handþewiþ hwich haldeþ and handþewiþ hwich blesseþ\" -- under the Brightest " +
            "Star, by the hand that holds and the hand that blesses.\n\n" +

            "The war. There is no front line, only field after field along the seams, each wing following the ore toward an " +
            "origin none has reached. The seams are the only map to the Starforge, and every wing digs them up to print " +
            "tanks. The soldiers have one name for all of it. They say: I did two seasons in the Starforge.";

        /// <summary>The USS class of the picture beside a Chronicle entry (by its heading, upper case), or
        /// null. The four plates are `UI/Art/plate_*.jpg` (docs/lore/prompts.md, prompts 2-5).</summary>
        public static string PlateFor(string heading)
        {
            switch (heading)
            {
                case "THE QUENCHING": return "m-plate--quenching";
                case "THE TWO HANDS": return "m-plate--two-hands";
                case "THE HOST": return "m-plate--vespers";
                case "THE ORDERS": return "m-plate--gantry";
                default: return null;
            }
        }

        /// <summary>An order's motto in the Old Language, then its gloss (docs/lore/old-language.md,
        /// section 9), as rich text for the Mech's card; null for a name no design draws. The names
        /// are <c>MechParts</c>' Orders, letter for letter.</summary>
        public static string Motto(string order)
        {
            switch (order)
            {
                case "the Ashen Covenant": return "<i>Fyrþe ded. Murn and weard.</i> — The fire is dead. Mourn and guard.";
                case "the Order of the Iron Lantern": return "<i>Liht bereþsk, ne bideþsk.</i> — Light is carried, not awaited.";
                case "the Ninth Vigil": return "<i>Nigoþa wæcce. Ne losed.</i> — The ninth watch. Not released.";
                case "the Sable Oath": return "<i>Oþ eikonam ne neodeþ.</i> — An oath needs no picture.";
                case "the Order of the Broken Crown": return "<i>Kruned hevedtö ne serveþ.</i> — It serves no crowned head.";
                case "the Emberguard": return "<i>Kernenþa laste embren. Tend hem.</i> — The cores are the last embers. Tend them.";
                case "the Wardens of the Long Night": return "<i>Nihtþe settede eld. Stand wel.</i> — The Night is the appointed age. Stand well.";
                case "the Brazen Host": return "<i>Werkþe offrung.</i> — The work is the offering.";
                default: return null;
            }
        }

        /// <summary>Joins the main menu's FIELD INTEL rotation.</summary>
        public static readonly string[] Tips =
        {
            "A cape marks a second oath, bronze trim and then gold: soldiers of the second and third rank never desert.",
            "The Biolab's creatures swore nothing, so they never desert. Neither do Diggers or aircrews.",
            "Field walls and ruined farmhouses are what the Clearance left on Vesper. Soldiers fight from behind them.",
            "An order answers a gantry once. Lose the Mech Bay before the drop and no knight comes; lose the Mech and none follows.",
            "The staff across the field kept your file from the last engagement. Open differently.",
            "The lance was licensed against knights and their cheap copies: a Mech-Hunter fells an Ironclad in two shots.",
            "Squads on the field net go by birth-star names. Point at a report and its men are ringed on the field.",
            "A knight takes no orders. The Gantry Right owes you his guns, not his obedience.",
        };
    }
}
