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

            "The quarrel. Asked what should be done, the Muster divided four ways, and the division fell along the wings. The Host dates things from it: " +
            "before the Division, since the Division. Blue, the " +
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

            "The enemy who knows you. Before the Division, the Host's Annalists kept a file on every officer. Each wing took its " +
            "share of the archive and all of the habit. The officer across the field is served by a staff that studied you in " +
            "peacetime, sees only what its own units see, and amends your file after every engagement.\n\n" +

            "Oaths. Every soldier swears the Host-oath, and fear breaks some of them: the broken go to ground behind their own " +
            "base. A soldier who proves themselves takes a second, stricter oath before a knight and is given the cape in the " +
            "wing's colour, trimmed in bronze and later in gold; nobody wearing one is known to have run. Knights are the keepers " +
            "of oaths: whether your Mech's knight hunts deserters or spares them is his own conscience.\n\n" +

            "Two tongues. Everyone speaks the Vulgar, on the field net as at home. The Old Language, which the clergy call the " +
            "High Speech, is the tongue of the Bright Age, kept for the liturgy, titles, mottos and oaths. Every soldier swears " +
            "in it: <i>Brighteste Sterrethe under, handthewith which holdeth and handthewith which blesseth</i> — under the Brightest " +
            "Star, by the hand that holds and the hand that blesses.\n\n" +

            "The twin houses. The monastic life of the Rite is kept in twin houses: a house of vigil-sisters and a house of " +
            "watchmen on one ground, a wall between them and one hall shared, with a screen down its middle and the two choirs " +
            "answering across it. Between them they keep the hours unbroken: somewhere in the Empire a choir is always singing " +
            "one. The sisters are clergy; the watchmen are laymen, who keep the hours and hold no office of the Rite. Vesper had " +
            "several among its farms, and all but one are roofless now, emptied with the farms by the Clearance. The last, in " +
            "the hills, keeps the hours between the lines, and nobody fires on it.\n\n" +

            "The hours. The day is kept in offices: lauds at sunrise, vespers at the first star, compline before sleep, and " +
            "vigils through the night before a feast. These four are the people's; the twin houses keep the full round, with " +
            "matins in the night and terce, sext and nones through the day. People greet each other by the hours: good " +
            "lauds, good vespers, and after " +
            "compline, keep the night. At vespers the celebrant sings <i>Firste sterrethe waketh</i> — the first star wakes — " +
            "and the people answer that they receive the light. Every ninth night the Great Liturgy runs from the first star " +
            "to lauds, read and sung by heart in the dark, until at its height the flame is passed out from the hall-lamp, " +
            "lamp to lamp: the Taking of the Light.\n\n" +

            // docs/lore/the-three-books.md: the Readings' three books, the Compact as the Annals record it.
            "The three books. At the Great Liturgy the lectors recite, by heart and in the dark, from three books. The Book " +
            "of Makings holds sayings of the Bright Age that nobody signed. The Annals of the Long Night are the record since " +
            "the Quenching: their first leaf is older than the Annalists, and the night it tells of has no year. The Annals' " +
            "first dated leaf is the Compact, when Aveline, the first Holy Mother, named Temrik the first Khan on the condition " +
            "that he hold the sword and never the light. The Book of Mothers keeps the Matriarchs' rulings and sermons, and " +
            "its first gave the halls their saying: <i>Light bearethsk, ne makethsk</i> — light is born, not made.\n\n" +

            // Soldiers' lore (lore sections 6 and 9), in the game's own voice: soldiers' reports never carry it.
            "Soldiers' lore. Soldiers of every wing share all of it: they were one Host until the Division. A soldier's fault " +
            "is washed in star-water at lauds before the whole company, and the shame is the punishment. The fallen are buried " +
            "where they fall. Their iron goes back to the Host with one more name scratched inside, and the next to wear it " +
            "reads them all; iron taken from another wing's dead is painted over and worn, and nobody talks about whose names " +
            "are in it. Letters come by slow ship, a season late, and a company reads its letters at vespers. Kin serving in " +
            "another wing are cousins across.\n\n" +

            "Birth-stars. Everyone is named under the star risen at their birth, and squads on the field net go by those " +
            "names: Wren, Lark, Heron and the rest. A soldier on the feast of their star is spared the night watch by custom. " +
            "To fall on one's own star's rising is to be taken at the rising, and is held a good death.\n\n" +

            "The Ninth Watch. It is the slow march every wing sings in the same words; on quiet nights each camp hears " +
            "another's verse a beat behind its own. Its refrain: nine watches kept and nobody slept; we walk the ninth till " +
            "morning. To keep the ninth is to stay awake to the end of anything.\n\n" +

            "Twins. It is the camp game, played with teal chips on a board scratched into a plank. The chips are laid in " +
            "facing pairs, as the seams lie, and each player takes from their own side; whoever leaves a chip without its " +
            "twin loses it. Officers forbid it, because the chips are Foundry feed.\n\n" +

            "The Night of the Quenching. Once a year every camp on Vesper puts out its lamps one by one, within sight of " +
            "each other, and no shot is fired until lauds. No order is given and no officer admits to it. Soldiers sometimes meet " +
            "between the lines in the dark, trade letters for kin across and share food.\n\n" +

            "The soldiers' words. In iron: serving. The grey: the old Host colour under every wing's paint. Gone quiet: " +
            "deserted, the masts' leads cut. A season: a posting on Vesper. Teal: quenchstone. The lance: the Mech-Hunter. " +
            "Kettle: an Ironclad, the knights' word. Lamplighter: a flame-trooper. Frogs, wasps, spiders, the cattle: the " +
            "Biolab's creatures.\n\n" +

            "The war. There is no front line, only field after field along the seams, each wing following the ore toward an " +
            "origin none has reached. The seams are the only map to the Starforge, and every wing digs them up to print " +
            "tanks. The soldiers have one name for all of it. They say: I did two seasons in the Starforge.";

        /// <summary>The USS class of the picture beside a Chronicle entry (by its heading, upper case), or
        /// null. The six plates are `UI/Art/plate_*.jpg` (docs/lore/prompts.md, prompts 2-5, 4 v2, 12-14).</summary>
        public static string PlateFor(string heading)
        {
            switch (heading)
            {
                case "THE QUENCHING": return "m-plate--quenching";
                case "THE TWO HANDS": return "m-plate--two-hands";
                case "THE HOST": return "m-plate--vespers";
                case "THE ORDERS": return "m-plate--gantry";
                case "THE IRON": return "m-plate--iron";
                case "TWO TONGUES": return "m-plate--taking";
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
                case "the Ashen Covenant": return "<i>Firethe ded. Mourn and ward.</i> — The fire is dead. Mourn and guard.";
                case "the Order of the Iron Lantern": return "<i>Light bearethsk, ne bidethsk.</i> — Light is carried, not awaited.";
                case "the Ninth Vigil": return "<i>Ninthe wecche. Ne loosede.</i> — The ninth watch. Not released.";
                case "the Sable Oath": return "<i>Oath eikonam ne needeth.</i> — An oath needs no picture.";
                case "the Order of the Broken Crown": return "<i>Crowned hevedtö ne serveth.</i> — It serves no crowned head.";
                case "the Emberguard": return "<i>Kernentha laste embren. Tend hem.</i> — The cores are the last embers. Tend them.";
                case "the Wardens of the Long Night": return "<i>Nightthe settede eld. Stand wel.</i> — The Night is the appointed age. Stand well.";
                case "the Brazen Host": return "<i>Workthe offrung.</i> — The work is the offering.";
                default: return null;
            }
        }

        /// <summary>One of the eight knightly orders, as the Field Manual's Orders page tells it
        /// (lore section 7's table). <c>name</c> is <c>MechParts</c>' Orders, letter for letter.</summary>
        public sealed class OrderEntry
        {
            public readonly string name, standard, teaching, repute;
            public OrderEntry(string name, string standard, string teaching, string repute)
            { this.name = name; this.standard = standard; this.teaching = teaching; this.repute = repute; }
        }

        public const string OrdersHead = "THE KNIGHTLY ORDERS";
        public const string OrdersIntro =
            "Eight orders, older than the Empire and older than the Luminary, descended from the guards of the Makers. They " +
            "swear to neither Khan nor Mother nor wing. By the Gantry Right they answer any lawful host that raises a gantry: " +
            "one knight and one core, once, for that field. Your Mech flies its order's standard and carries its motto.";
        /// <summary>Repute is hearsay, and the page says so (lore section 7, under the table).</summary>
        public const string OrdersFoot =
            "Reputation leans a knight but does not bind him: in the field his own temper and pride decide the rest. Titles are offices, " +
            "not ranks. A Castellan keeps an order's hold, a Seneschal its household, a Marshal its knights in the field and " +
            "a Knight-Warden a single core; a Paladin is a sworn champion, a Knight-Errant answers the gantries nobody else " +
            "will, and a Vigilant has served on the High Watch.";

        public static readonly OrderEntry[] Orders =
        {
            new OrderEntry("the Ashen Covenant", "a white ring on grey",
                "The fire is dead. Mourn it and guard what it made.", "the oldest, cold, never hurried."),
            new OrderEntry("the Order of the Iron Lantern", "an unlit lantern, black on pale",
                "Light is carried, not awaited.", "wayfinders."),
            new OrderEntry("the Ninth Vigil", "eight hollow stars and one filled",
                "It kept the ninth watch at the Quenching and was never relieved.", "it says least and is assumed to know most."),
            new OrderEntry("the Sable Oath", "plain black, no device",
                "An oath needs no picture.", "the hardest on the oathbroken."),
            new OrderEntry("the Order of the Broken Crown", "a crown in two halves",
                "Its founders broke their king's crown rather than see a conqueror wear it. It serves no crowned head.",
                "proud. The Mothers are crowned, and the order has never said its motto does not mean them."),
            new OrderEntry("the Emberguard", "one orange ember on black",
                "The cores are the last embers. Tend them.", "warm, and the closest to their machines."),
            new OrderEntry("the Wardens of the Long Night", "a pale horizon line on slate",
                "The Night is the age appointed. Stand it well.", "stoic."),
            new OrderEntry("the Brazen Host", "a brass hammer on charcoal",
                "The Makers were smiths first. The work is the offering.",
                "the largest and the loudest; it claims descent from the Makers' forge-hands, which the others dispute."),
        };

        /// <summary>Joins the main menu's FIELD INTEL rotation.</summary>
        public static readonly string[] Tips =
        {
            "A cape marks a second oath, bronze trim and then gold: soldiers of the second and third rank never desert.",
            "The Biolab's creatures swore nothing, so they never desert. Neither do Diggers or aircrews.",
            "Field walls and ruined farmhouses are what the Clearance left on Vesper. Soldiers fight from behind them.",
            "An order answers a gantry once. Lose the Mech Bay before the drop and no knight comes; lose the Mech and none follows.",
            "The staff across the field kept your file from the last engagement. Open differently.",
            "The lance was licensed against knights and their cheap copies: a Mech-Hunter fells an Ironclad in two shots.",
            "Squads on the field net go by birth-star names. Point at a report and its soldiers are ringed on the field.",
            "A knight takes no orders. The Gantry Right owes you his guns, not his obedience.",
            "One map in three is fought at night. The camps light their lamps at vespers, and soldiers call the celebrant who keeps them a lampwarden.",
            "Deserters cut the leads of their iron's masts first. The field net loses them, and by night their mast lights are dark.",
            "Inside every breastplate are the names of those who wore the iron before. Select one soldier to read them.",
            "A fallen soldier's iron goes back to the Host and the next recruit wears it. Iron taken by another wing is painted over.",
            "When knights of one order answer opposing gantries they fight in earnest, and no grudge is permitted.",
            "The Field Manual's Orders page shows the eight orders' standards and mottos. Your Mech flies one of them.",
            "On the Night of the Quenching the halls read one saying only: bank the hearth, do not quench it. No gun fires from the darkening to lauds.",
            "On a night of the Great Liturgy the lamps go dark and are lit again from the Foundries outward, as the flame is passed from the hall-lamp.",
        };

        /// <summary>The Readings on a night map (docs/lore/the-three-books.md), as the HUD's dispatches
        /// quote them in the Vulgar: a heading naming the book, then the line.</summary>
        public readonly struct Reading
        {
            public readonly string book, text;
            public Reading(string book, string text) { this.book = book; this.text = text; }
        }

        /// <summary>Read only on the Night of the Quenching, the last words before the hall-lamp is put out.</summary>
        public static readonly Reading QuenchingReading = new Reading("BOOK OF MAKINGS",
            "At evening bank the hearth. Do not quench it, and do not feed it in the dark.");

        /// <summary>A Great Liturgy night quotes one of these, chosen from the map's seed.</summary>
        public static readonly Reading[] LiturgyReadings =
        {
            new Reading("BOOK OF MAKINGS", "The sun chooses no field. The maker chooses his iron."),
            new Reading("BOOK OF MAKINGS", "Mark the work, not the maker. The work lasts; the maker goes."),
            new Reading("BOOK OF MAKINGS", "Give the thing its purpose, and let it go."),
            new Reading("ANNALS OF THE LONG NIGHT", "In the ninth watch the fire failed. Who kept the ninth watch is not written here."),
            new Reading("ANNALS OF THE LONG NIGHT", "That morning the sun rose on every world as it had always risen."),
            new Reading("ANNALS OF THE LONG NIGHT", "She named one captain over all the others, on the condition that he hold the sword and never the light."),
            new Reading("BOOK OF MOTHERS", "Whoever bears a lamp bears the light to another. Light is born, not made."),
            new Reading("BOOK OF MOTHERS", "A closed hand holds nothing of the light. It holds the dark that was in it already."),
        };
    }
}
