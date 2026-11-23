// LoreText.cs — the world as the player is told it: the Field Manual's Chronicle page and
// the main menu's lore tips. Canon and voice: docs/lore/starforge-lore.md (section 13).
// Nothing here may answer what its section 11 leaves open, or take a wing's side; a tip
// states a game rule, so check it against the rule it names when that rule changes.
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
            "and the treasury, and the Holy Mother, who blesses, and who alone elects each Khan. Their bargain is the Compact. " +
            "What is a matter of both the sword and the light must carry both seals.\n\n" +

            "The Host. By old custom the Empire's army is kept in two equal wings, Blue and Red, so that no captain ever " +
            "commands all of it. The same equipment, the same ranks, the same oath: by the hand that holds and the hand that " +
            "blesses.\n\n" +

            "Vesper. A quiet farm-world of birch and spruce, wolf, fox and eagle, named for the hour: from here the Brightest " +
            "Star is the evening star. A hundred years ago its smallholders were cleared off the land for ore concessions that " +
            "failed. The field walls and house corners are what they left, and many soldiers of both wings are their " +
            "great-grandchildren.\n\n" +

            "Teal. Its proper name is quenchstone. It is found only on worlds where starfire was once worked, it holds no magic, " +
            "and it is the finest feedstock known for the printworks that raise a Foundry in minutes. Its seams lie in facing " +
            "pairs, as if cast from one pour, and on Vesper every seam runs back toward one origin. The surveyors' conclusion: " +
            "the Starforge is here.\n\n" +

            "The quarrel. Asked what should be done, the Muster split evenly, and the split fell along the wings. Blue, the " +
            "Kindlers, would find the Starforge and relight it. Red, the Keepers, would find it and seal it cold. Both landed to " +
            "secure the site. Either the Khan or the Holy Mother could end it with a sentence, and neither has spoken. So no " +
            "wing is a rebel, and in law there is no war: only a disputed custody.\n\n" +

            "The orders. The eight knightly orders are older than the Empire and swear to neither wing. At the heart of each of " +
            "their war machines sits a core, a purposed mind forged in the Bright Age, which can think, remember and advise but " +
            "cannot move a limb without a sworn human hand. Nobody can make another. By the Gantry Right, any lawful host that " +
            "raises a gantry is owed one knight and one core, once, for that field: your Mech Bay is that gantry. A fallen Mech " +
            "is not replaced, and a gantry that falls before the drop cancels the summons.\n\n" +

            "Two voices. By the orders' teaching, your Mech's calls and reports are the knight's, and its quiet quoted sayings " +
            "are the core's. Sceptics hold that there is one voice and a book of sayings. Knights who are asked say \"we\" and " +
            "change the subject.\n\n" +

            "The enemy who knows you. Before the split, the Host's Annalists kept a file on every officer. Each wing took half " +
            "the archive and all of the habit. The officer across the field is served by a staff that studied you in peacetime, " +
            "sees only what its own units see, and amends your file after every engagement.\n\n" +

            "Oaths. Every soldier swears the Host-oath, and fear breaks some of them: the broken go to ground behind their own " +
            "base. A soldier who proves themselves takes a second, stricter oath before a knight and is given the cape in the " +
            "wing's colour, trimmed in bronze and later in gold; nobody wearing one is known to have run. Knights are the keepers of oaths: whether your Mech " +
            "hunts deserters or spares them is its own conscience.\n\n" +

            "The war. There is no front line, only field after field along the seams, each wing following the ore toward an " +
            "origin neither has reached. The seams are the only map to the Starforge, and both wings dig them up to print " +
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

        /// <summary>Joins the main menu's FIELD INTEL rotation.</summary>
        public static readonly string[] Tips =
        {
            "A cape marks a second oath, bronze trim and then gold: soldiers of the second and third rank never desert.",
            "The Biolab's creatures swore nothing, so they never desert. Neither do Diggers or aircrews.",
            "Field walls and ruined farmhouses are what the Clearance left on Vesper. Soldiers fight from behind them.",
            "An order answers a gantry once. Lose the Mech Bay before the drop and no knight comes; lose the Mech and none follows.",
            "The staff across the field kept your file from the last engagement. Open differently.",
        };
    }
}
