// MechSpeech.cs — what the Mech says to keep its side's spirits up, and when.
//
// MechBrain gives its side the calls that matter (the enemy coming, the moment to
// strike, pull back); this is the rest: the voice of an old machine that has seen more
// wars than its side has, speaking with certainty and restraint, never ranting or
// begging. It reads how the battle stands about once a second and says a line when
// the moment asks for one:
//   - a fight ends (nothing has died for 15 s): won or lost, at home or on their side,
//     decisively or not, and at what cost;
//   - the balance swings back after they had the better of it (a turning point);
//   - it is in extremis (hull low under fire, surrounded, the base being taken);
//   - the side is close to losing everything (near defeat);
//   - otherwise now and then (every 45-75 s in a fight, 70-110 s in a quiet spell),
//     by what the fighting is: attack or defence, winning, even or losing, going well
//     or badly -- or calm, with nothing happening.
// Each of those has buckets of lines by situation and tone; a line is drawn from them
// by weight, never twice in a match while a bucket has lines left, and lines that name
// something (their retreat, their artillery, being surrounded, the cost) weigh three
// times as much when it is true and a third as much when it is not. Now and then a
// line from the "iconic" set comes instead (solemn when things are grim, superior when
// they are not). Each pilot has a temper (warm or cold: good-mood lines or bad ones)
// and a pride (how often it talks down to the enemy), drawn from the match seed.
//
// Its words never change what anyone does: it has its own random stream (so what it
// says does not shift the brain's draws), it speaks only when the brain has nothing to
// say, and it never holds the brain's calls back (the other side's Commander acts on
// those; nothing acts on these).
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using StarForge.Sim;
using StarForge.World;
using static StarForge.SFMath;

namespace StarForge.AI
{
    public sealed partial class MechSpeech
    {
        // ------------------------------------------------------------ the lines
        /// <summary>The situation a line is written for.</summary>
        public enum Moment { Attack, Defence, LosingAttack, LosingDefence, WinningAttack, WinningDefence, Critical, TurningPoint, Calm, Victory, Defeat, Any }
        public enum Tone { Good, Bad, Neutral, Solemn, Superior }

        /// <summary>Something a line says is so: it weighs more when it is, less when it is not.</summary>
        [System.Flags]
        public enum Cue
        {
            None = 0,
            /// <summary>The enemy near the fight is falling back toward its base.</summary>
            Retreating = 1,
            /// <summary>Their army is on our half of the map.</summary>
            Extended = 2,
            /// <summary>Maulers (their shells) in the fight.</summary>
            Artillery = 4,
            /// <summary>Enemies on three sides of the Mech.</summary>
            Surrounded = 8,
            /// <summary>Our side has lost a good deal lately.</summary>
            Costly = 16,
            /// <summary>Things are hard: behind, in a bad way, or in extremis.</summary>
            Hard = 32
        }

        public readonly struct Line
        {
            public readonly int n;
            public readonly Moment moment;
            public readonly Tone tone;
            public readonly float weight;
            public readonly Cue cue;
            public readonly string text;
            /// <summary>Null: any Mech's. Else the order (as <see cref="MechDesign.order"/>
            /// spells it) whose Mech alone says it.</summary>
            public readonly string order;

            public Line(int n, Moment moment, Tone tone, float weight, Cue cue, string text, string order = null)
            {
                this.n = n; this.moment = moment; this.tone = tone; this.weight = weight; this.cue = cue; this.text = text;
                this.order = order;
            }

            /// <summary>Whether the Mech of this design may say it.</summary>
            public bool For(MechDesign d) => order == null || (d != null && order == d.order);

            public int Bucket => BucketOf(moment, tone);
        }

        const int Tones = 5;
        static int BucketOf(Moment m, Tone t) => (int)m * Tones + (int)t;
        static readonly int BucketCount = ((int)Moment.Any + 1) * Tones;

        /// <summary>Every line it can say. 1-400 as written for it (British spelling; #286,
        /// #329 and #400 reworded to stand on their own); 401-406 the lines it had before;
        /// 407-506 written against the lore (docs/lore/starforge-lore.md, section 13 has the
        /// voice and what no line may answer): 407-490 any Mech's, 491-506 two for each
        /// order, said only by that order's Mech (<see cref="Line.order"/>).
        /// {pilot}, {callsign} and {order} are filled in from the Mech's design.</summary>
        public const int LineCount = 522;
        /// <summary>The table's numbers run 1-519, then 528 on: 520-527 are the counsel lines (MechSpeech.Counsel).
        /// <see cref="LineCount"/> is the table's length.</summary>
        public const int FirstRun = 519, SecondFrom = 528;
        static System.Collections.Generic.IEnumerable<int> ExpectedNumbers()
        {
            for (int n = 1; n <= FirstRun; n++) yield return n;
            for (int n = SecondFrom; n < SecondFrom + LineCount - FirstRun; n++) yield return n;
        }
        public static readonly Line[] Lines = BuildLines();
        static readonly int[][] ByBucket = IndexBuckets();

        static Line[] BuildLines()
        {
            var L = new List<Line>(LineCount + 4);
            Moment m = Moment.Attack;
            Tone t = Tone.Good;
            void S(Moment mo, Tone to) { m = mo; t = to; }
            void A(int n, string text, float w = 1f, Cue cue = Cue.None) => L.Add(new Line(n, m, t, w, cue, text));
            // One order's own: weighted up, since only one Mech in eight can say it.
            void O(string order, int n, string text, float w = 2f) => L.Add(new Line(n, m, t, w, Cue.None, text, order));

            S(Moment.Attack, Tone.Good);
            A(1, "The enemy has exposed itself. Such gifts should not be left unanswered.");
            A(2, "Their line is open. Let the weight of our guns pass through it.");
            A(3, "The moment has come. The battlefield itself bears witness.");
            A(4, "They have offered us the initiative. We shall spend it well.");
            A(5, "Their formation bends. Pressure will turn the bend into a break.");
            A(6, "There is strength in our position. There is greater strength in what follows from it.", 0.8f);
            A(7, "Their defence has become a question. Our weapons possess the answer.");
            A(8, "The enemy has mistaken our patience for hesitation. They will not make that mistake twice.", 1.3f);
            A(9, "Our weapons are ready. Their armour has already been judged.", 1.3f);
            A(10, "A narrow opening is sufficient. Great victories often begin with less.");
            A(11, "Their flank is bare. Even fortune occasionally favours the prepared.");
            A(12, "Their strongest formation is already beginning to lose its shape.");
            A(13, "Momentum is with us. Such things are fragile. We will make ours endure.");
            A(14, "The first breach is always the most difficult. The rest follows more willingly.");
            A(15, "They are falling back. Their retreat has become the shortest road to their defeat.", 1f, Cue.Retreating);
            A(16, "The enemy is yielding. We have merely to ensure that yielding becomes collapse.", 1f, Cue.Retreating);
            A(17, "Their resistance is weakening. Ours has yet to be tested.");
            A(18, "The field favours us. Let us prove worthy of the advantage.");
            A(19, "Their line is fractured. Fractures become ruins beneath sufficient weight.");
            A(20, "We have found the weakness. Now comes the discipline required to exploit it.");
            A(21, "Their army still stands. Their ability to withstand us does not.");
            A(22, "Today, the battlefield remembers whose weapons speak with authority.", 0.8f);
            A(23, "A decisive hour has arrived. History favours those who recognise it.", 0.8f);
            A(24, "They came prepared to resist. They did not come prepared for this.", 1.4f);
            A(25, "The path is open. There is no honour in leaving an enemy unbroken when victory is within reach.");
            A(26, "Our strength is proven not by what we possess, but by what we accomplish with it.", 0.7f);
            A(27, "Let the enemy learn the old lesson: pressure reveals what courage conceals.");
            A(28, "Their position is temporary. Ours is becoming inevitable.");
            A(29, "The battle has begun to obey reason. I find that reassuring.", 1.3f);
            A(30, "Forward progress is no longer uncertain. Only the cost remains to be determined.");

            S(Moment.Attack, Tone.Bad);
            A(31, "This engagement has lasted longer than its merits justify.");
            A(32, "Their survival is becoming an unacceptable inefficiency.", 1.2f);
            A(33, "We possess the greater strength. Our execution has yet to demonstrate it.");
            A(34, "The enemy is not formidable. We have merely been insufficiently decisive.", 1.2f);
            A(35, "Their resistance has outlived its usefulness.");
            A(36, "I have measured their defences. They should not still be standing.");
            A(37, "Every wasted second is an advantage we voluntarily surrender.");
            A(38, "They are buying time. We are allowing the transaction.", 1.4f);
            A(39, "Their centre is weakening. This opportunity will not wait for indecision.");
            A(40, "The battlefield is simple. Our response should be equally so.", 0.8f);
            A(41, "There is no mystery here. Their position must break.");
            A(42, "We have endured enough resistance from forces beneath our strength.");
            A(43, "Their formation is wounded. It is unbecoming of us to leave it alive.");
            A(44, "Our advantage is being squandered. I find waste difficult to tolerate.");
            A(45, "The enemy has been given too many opportunities to recover.");
            A(46, "The longer they remain intact, the more embarrassing this becomes.", 1.2f);
            A(47, "Superior firepower is meaningless when discipline fails to direct it.");
            A(48, "Their courage is admirable. Their continued existence is less so.", 1.4f);
            A(49, "We are not losing this battle. We are delaying its proper conclusion.", 1.2f);
            A(50, "I have seen weaker forces accomplish more with less hesitation.");
            A(51, "The enemy believes this resistance has meaning. Correct the misunderstanding.");
            A(52, "The breach is there. The unwillingness to exploit it is ours.");
            A(53, "I will not mistake persistence for progress.");
            A(54, "Our weapons are not the problem. Their employment is.");
            A(55, "This is not the hour for caution. It is the hour for precision.", 0.9f);
            A(56, "Let the enemy discover how quickly patience can become wrath.", 1.2f);
            A(57, "The battle is not difficult. We are making it difficult.");
            A(58, "They have been granted enough time. The account is now due.");

            S(Moment.Defence, Tone.Good);
            A(59, "They come to us. That is often the beginning of their mistake.", 1.2f);
            A(60, "Our position is strong. Their determination cannot alter that fact.");
            A(61, "Let them cross the distance. Every metre strengthens our judgement of them.");
            A(62, "A fortified position rewards patience. We have patience in abundance.");
            A(63, "Their numbers are considerable. Their avenues of approach are not.");
            A(64, "The walls will hold because the will behind them holds first.", 0.9f);
            A(65, "They have chosen to attack prepared ground. I welcome their confidence.", 1.2f);
            A(66, "Their advance narrows with every step. Ours does not.");
            A(67, "There is comfort in a battlefield with fewer directions.", 0.8f);
            A(68, "Their assault is gathering strength. So is our firing solution.");
            A(69, "They are entering the distance where mistakes become casualties.", 1.3f);
            A(70, "A disciplined defence does not fear the enemy's arrival.");
            A(71, "We need not pursue what has chosen to approach us.");
            A(72, "Every attack tells us something. This one is becoming particularly informative.");
            A(73, "Their confidence will carry them farther than their wisdom should permit.", 1f, Cue.Extended);
            A(74, "The enemy has brought the battle to favourable ground. Their courtesy is noted.", 1.4f);
            A(75, "A strong position turns patience into weapons.", 0.8f);
            A(76, "Their assault is spending itself against us.");
            A(77, "We stand where they must come. They bleed where we choose.", 1.3f);
            A(78, "The perimeter remains unbroken. That is the only report that matters.");
            A(79, "Let them discover that numbers are not the same as strength.");
            A(80, "We were built to endure precisely this.");
            A(81, "The enemy advances beneath our guns. I have seen less favourable arrangements.");
            A(82, "They have brought an army. We have brought a position.", 1.4f);
            A(83, "Their attack is becoming slower. Ours is becoming more certain.");
            A(84, "The line stands. So long as it stands, the battle remains ours to shape.");
            A(401, "Steady, Commander. Every metre they take, they pay for twice.", 1.2f);
            A(405, "Hold the line. When the moment comes, I will tell you -- and I will be at the front of it.");

            S(Moment.Defence, Tone.Bad);
            A(85, "The perimeter is weakening. Pretending otherwise would be beneath us.");
            A(86, "We are reacting too slowly. The enemy is beginning to notice.");
            A(87, "Ground is being surrendered without sufficient return.");
            A(88, "A defensive line that moves without purpose is merely a retreat awaiting explanation.");
            A(89, "Their pressure is concentrated. Ours must become equally deliberate.");
            A(90, "The threatened sector is obvious. So is the consequence of ignoring it.");
            A(91, "We cannot defend every place equally. Reality has never permitted such luxuries.", 0.9f);
            A(92, "Their artillery is dictating the rhythm. That rhythm must end.", 1f, Cue.Artillery);
            A(93, "The outer line may be lost. The heart of the base must not follow it.");
            A(94, "Their advance is becoming an established fact. Facts can still be destroyed.");
            A(95, "The enemy has found a weakness. They must not be allowed to enjoy it.");
            A(96, "I dislike this position. The battlefield agrees with me.", 1.4f);
            A(97, "There is too much ground between our strength and our objective.");
            A(98, "A breach unattended becomes a road. Roads become graves.", 1.2f);
            A(99, "The line is bending. Discipline must become its reinforcement.");
            A(100, "They are probing for collapse. We will give them resistance instead.");
            A(101, "This sector has ceased to be defensible without reinforcement.");
            A(102, "The enemy is gaining confidence. Confidence is a problem best corrected early.");
            A(103, "A position is not sacred. Survival is.");
            A(104, "The battlefield offers no sympathy. Neither should we expect it.", 0.8f);
            A(105, "We have endured worse. I would prefer not to test the statement today.", 1.3f);
            A(106, "The line is still ours. But it is no longer comfortable.");
            A(107, "Too many threats have been answered too late.");
            A(108, "The enemy is pressing because we permit them to believe they can.");
            A(109, "This position will not hold forever. It need only hold long enough.");
            A(110, "We are approaching the point where courage alone becomes insufficient.");

            S(Moment.LosingAttack, Tone.Good);
            A(111, "We are behind. That makes their confidence useful to us.");
            A(112, "They have advanced too far. Distance has begun to separate them from wisdom.", 1f, Cue.Extended);
            A(113, "Their victory formation is becoming their weakness.");
            A(114, "We have lost ground. We have not lost our understanding of the battlefield.");
            A(115, "An enemy certain of victory rarely sees the blade approaching.");
            A(116, "Their forces are stretched. Even superior numbers become fragile when extended.", 1f, Cue.Extended);
            A(117, "They believe they are pursuing us. They may yet discover otherwise.", 1.3f);
            A(118, "Our disadvantage has forced clarity upon us.");
            A(119, "We do not require superiority everywhere. Only where the decision is made.");
            A(120, "The battle is not measured by what has already been lost.");
            A(121, "Their spearhead has outrun its shield. Such things end badly.", 1.2f, Cue.Extended);
            A(122, "The enemy has gathered strength in one place. I have always appreciated convenient concentrations.", 1.3f);
            A(123, "We have been forced to yield initiative. We can take it back.");
            A(124, "This is not yet the story they believe it to be.");
            A(125, "One decisive blow can outweigh a dozen lesser disappointments.");
            A(126, "Their confidence has become visible. Visible things can be targeted.");
            A(127, "There is still a path through this. I can see it.", 1.1f);
            A(128, "A wounded force can still possess a lethal hand.");
            A(129, "They have begun to believe the battlefield belongs to them.");
            A(130, "Belief has destroyed armies before. Let theirs serve us now.");

            S(Moment.LosingAttack, Tone.Bad);
            A(131, "We are losing. That fact is no longer useful. The enemy's exposed flank is.");
            A(132, "Their advance has outrun their protection. It must become their punishment.", 1f, Cue.Extended);
            A(133, "We cannot afford another wasteful engagement.");
            A(134, "They possess the numbers. We must possess the judgement.");
            A(135, "Their victory is becoming dangerously plausible. I dislike that.", 1.2f);
            A(136, "They have overextended. I would prefer them to learn why that was unwise.", 1f, Cue.Extended);
            A(137, "We have paid heavily. There must be something worth taking in return.", 1f, Cue.Costly);
            A(138, "Do not feed strength into their strength.");
            A(139, "Their formation is stretched beyond comfort. Good.", 1.2f, Cue.Extended);
            A(140, "We require a reversal, not another exchange of losses.");
            A(141, "Their rear is unguarded. Even victories have blind spots.", 1f, Cue.Extended);
            A(142, "We are cornered only if we accept the enemy's definition of the battlefield.");
            A(143, "A weaker force survives through discipline. A stronger one survives through fewer mistakes.");
            A(144, "We have already spent enough lives on their strongest position.", 1f, Cue.Costly);
            A(145, "Their momentum must break somewhere.");
            A(146, "We are wounded, not harmless.", 1.3f);
            A(147, "There is still one decisive target.");
            A(148, "They have mistaken our losses for their invincibility.", 1.2f);
            A(149, "Their strength is considerable. Their discipline is not.");
            A(150, "If they insist upon approaching us, they may yet discover the cost of victory.");
            A(151, "We are not required to win beautifully. We are required to remain standing.");
            A(152, "The battlefield has turned against us. We need only turn one part of it back.");
            A(153, "Their triumph is unfinished. So is our resistance.");
            A(154, "This is the hour in which lesser forces surrender. We are not a lesser force.");

            S(Moment.LosingDefence, Tone.Good);
            A(155, "We yield ground, not purpose.", 1.3f);
            A(156, "A smaller perimeter gives greater weight to every weapon within it.");
            A(157, "They have taken territory. They have not taken our strength.");
            A(158, "Their advance narrows our space and sharpens our purpose.");
            A(159, "We are retreating with intelligence. There is no shame in that.");
            A(160, "Every position we abandon teaches us where the next stand must be made.");
            A(161, "Their army grows closer. So does the range at which our response becomes decisive.");
            A(162, "They are consuming territory faster than they can secure it.", 1f, Cue.Extended);
            A(163, "The field may be theirs for a time. Time itself remains ours to spend.");
            A(164, "We have fewer places to defend. That simplifies the problem.", 1.2f);
            A(165, "They have crossed another line. The lines ahead will be harder.");
            A(166, "The fortress is not the stone. It is the will behind the stone.", 1.2f);
            A(167, "Our options are narrowing. Our resolve need not.");
            A(168, "A retreat conducted with purpose is a movement toward opportunity.");
            A(169, "The enemy believes distance favours them. It may instead favour our guns.");
            A(170, "We have survived the first wave. The next will meet a wiser defence.");
            A(171, "The battle is shrinking toward its decisive ground.");
            A(172, "We have not been broken. That remains the only fact that matters.");
            A(402, "{Order} has not lost a field it chose to hold. We will not start today.", 1.2f);

            S(Moment.LosingDefence, Tone.Bad);
            A(173, "The perimeter is collapsing. Survival now depends upon concentration.");
            A(174, "We have surrendered enough ground.");
            A(175, "A position that cannot be held must not consume those who can fight elsewhere.");
            A(176, "Their breakthrough is imminent. The next stand must not share its weakness.");
            A(177, "The enemy has entered our defensive depth. This is no longer a distant threat.");
            A(178, "We cannot preserve every structure. We can preserve what matters.");
            A(179, "Our line is becoming too wide for our strength.");
            A(180, "The dead cannot hold a position. Preserve the living.", 1.2f, Cue.Costly);
            A(181, "Their pressure is relentless. Our response must become deliberate.");
            A(182, "There is little room remaining for mistakes.");
            A(183, "We are approaching the edge of failure. The edge is not the same as the fall.", 1.2f);
            A(184, "This line is temporary. The objective is not.");
            A(185, "Do not waste strength defending what has already become indefensible.");
            A(186, "The enemy has momentum. Momentum ends where resistance becomes absolute.");
            A(187, "Our losses are becoming difficult to justify.", 1f, Cue.Costly);
            A(188, "We are buying time at too high a price.", 1f, Cue.Costly);
            A(189, "The next position must be stronger. There will not be another after it.");
            A(190, "We have been pushed back. We have not been erased.", 1.3f);
            A(191, "Hold whatever cannot be replaced. Everything else is merely ground.");
            A(192, "There are moments when retreat is wisdom. This is one of them.");
            A(193, "Survival is now the first victory.");

            S(Moment.WinningAttack, Tone.Good);
            A(194, "The enemy is breaking. Let the break become irreversible.");
            A(195, "Their line has lost cohesion. Victory is beginning to take physical form.");
            A(196, "The hard part is behind us. Discipline must carry us through the easy part.");
            A(197, "They retreat because they understand what their commanders will not admit.", 1.2f, Cue.Retreating);
            A(198, "The battlefield has begun to recognise its rightful shape.");
            A(199, "Their reserves are exposed.");
            A(200, "One victory has become the foundation for another.");
            A(201, "They are no longer fighting for triumph. They are fighting for time.");
            A(202, "We have earned momentum. We shall not squander it.");
            A(203, "Their strongest units retreat first. That is useful knowledge.", 1f, Cue.Retreating);
            A(204, "A broken enemy still possesses weapons. Respect that fact.");
            A(205, "The end is approaching. Precision becomes more important, not less.");
            A(206, "The enemy's courage has not vanished. Their confidence has.");
            A(207, "There is dignity in decisive victory. Let ours be remembered for it.");
            A(208, "Their command structure frays beneath the weight of its failures.");
            A(209, "The enemy is becoming a collection of isolated battles.");
            A(210, "We have broken the centre. The rest is only distance.", 1.3f);
            A(211, "Victory belongs to those disciplined enough to finish what they began.");
            A(212, "Let them flee. Their retreat carries the memory of our guns with it.", 1.2f, Cue.Retreating);

            S(Moment.WinningAttack, Tone.Bad);
            A(213, "We are winning. Do not disgrace that fact with carelessness.");
            A(214, "Their defeat is certain. Their destruction is not yet complete.");
            A(215, "The enemy is broken. We remain responsible for what comes next.");
            A(216, "Victory has made them vulnerable. It has also made us complacent.");
            A(217, "Their retreat must not become an opportunity for them to recover.", 1f, Cue.Retreating);
            A(218, "We have superiority. Waste none of it.");
            A(219, "Their reserves remain. I would prefer fewer uncertainties.");
            A(220, "A victory thrown away through arrogance is simply defeat wearing ceremonial armour.", 1.4f);
            A(221, "The enemy is weakened, not harmless.");
            A(222, "Do not chase glory. Secure the result.");
            A(223, "Their formation is collapsing. There is no reason for ours to do the same.");
            A(224, "The battle is nearly ours. Nearly is a dangerous word.", 1.4f);
            A(225, "The enemy has suffered enough to retreat. Make certain they cannot return in strength.", 1f, Cue.Retreating);
            A(226, "Discipline now matters more than courage.");
            A(227, "Their mistakes created our advantage. Do not create theirs in return.");
            A(228, "The battlefield rewards those who remain sober after victory.");
            A(229, "Do not allow success to make us ordinary.", 1.3f);
            A(230, "We did not survive this long to lose ourselves to triumph.");

            S(Moment.WinningDefence, Tone.Good);
            A(231, "Their assault has spent itself against an immovable will.");
            A(232, "The walls remain. The objective remains. So do we.");
            A(233, "Their offensive has become a monument to wasted strength.", 1.2f);
            A(234, "We endured. That was enough.", 1.3f);
            A(235, "Their army has paid dearly for every metre it failed to take.");
            A(236, "The perimeter stands more confidently now than it did before the assault.");
            A(237, "Their retreat is orderly. Their defeat is not.", 1f, Cue.Retreating);
            A(238, "The guns are quieting. For now, they have nothing more to say.");
            A(239, "A fortress proves its worth not when it is built, but when it is tested.");
            A(240, "They came with strength. We answered with endurance.");
            A(241, "Our position remains beyond their reach.");
            A(242, "The enemy has learned what the walls could not explain.");
            A(243, "There is a particular silence that follows a failed assault. Listen to it.", 1.4f);
            A(244, "We remain. They withdraw. The battlefield has spoken.", 1f, Cue.Retreating);
            A(245, "This ground has been defended. Let it remain ours.");
            A(246, "Their courage brought them here. Our discipline sent them back.");

            S(Moment.WinningDefence, Tone.Bad);
            A(247, "We held. The cost was higher than it should have been.", 1f, Cue.Costly);
            A(248, "The enemy failed. Our performance was hardly flawless.");
            A(249, "Victory does not erase unnecessary casualties.", 1f, Cue.Costly);
            A(250, "Repair the wounded strength. The next assault will study our mistakes.");
            A(251, "We survived because we adapted. Remember that.");
            A(252, "Their assault failed. It nearly succeeded. That distinction matters.", 1.3f);
            A(253, "The objective is secure. Our margin is not.");
            A(254, "We have earned this victory. We have not earned complacency.");
            A(255, "Their next attack will not resemble this one. Assume as much.");
            A(256, "Count what remains. Numbers are more honest than celebration.", 1.2f, Cue.Costly);
            A(257, "Their retreat is not the end. It is preparation for another attempt.", 1f, Cue.Retreating);
            A(258, "A narrow victory is still a warning.", 1.2f);
            A(259, "The walls held. The weaknesses behind them must now be corrected.");
            A(260, "We have been given another day. We should make proper use of it.");

            S(Moment.Critical, Tone.Good);
            A(261, "The odds are unfavourable. Fortunately, odds do not pull triggers.", 1.5f);
            A(262, "They have surrounded us. Every direction now contains a target.", 1f, Cue.Surrounded);
            A(263, "Pressure is extreme. So is the value of remaining calm.");
            A(264, "This battlefield has become unforgiving. I have always performed well under such conditions.", 1.3f);
            A(265, "Our margins are thin. Thin margins still contain victories.");
            A(266, "The enemy has committed heavily. Such commitment creates weaknesses elsewhere.");
            A(267, "They believe we are trapped. Their belief may become our weapon.");
            A(268, "Difficult circumstances reveal the quality of a fighting force.");
            A(269, "We have fewer options now. The important ones remain.");
            A(270, "There is still a path. I have found it.", 1.2f);
            A(271, "The battlefield is hostile. Our resolve need not be.");
            A(272, "They have committed everything. That is rarely wise.");
            A(273, "Desperation is not defeat. It is merely pressure.");
            A(274, "The moment is severe. Good. Severe moments clarify the truth.");
            A(404, "They are counting on us to blink first. We do not blink.", 1.2f);

            S(Moment.Critical, Tone.Bad);
            A(275, "Our position is deteriorating. There is no value in pretending otherwise.");
            A(276, "Too many threats. Too little time. Concentration is no longer optional.");
            A(277, "We are approaching the edge. The edge is where discipline matters most.");
            A(278, "The enemy believes this battle is decided. That belief remains unverified.");
            A(279, "Our losses are severe. The remaining strength must become decisive.", 1f, Cue.Costly);
            A(280, "There is no room for pride now. Only what survives matters.");
            A(281, "The battlefield has become hostile in every direction.", 1f, Cue.Surrounded);
            A(282, "We cannot sustain this position indefinitely. Something must change.");
            A(283, "Their pressure has exceeded our current response.");
            A(284, "This is no longer a battle of comfort. It is a battle of endurance.");
            A(285, "Many will fail here. We do not have that luxury.", 1.2f);
            A(286, "The situation is grave. Grave does not mean hopeless.", 1.2f);
            A(287, "Survival has become the battlefield.");
            A(288, "Stand firm where retreat would become collapse.");
            A(289, "We are being tested beyond doctrine. Good. Doctrine ends where experience begins.", 1.3f);

            S(Moment.TurningPoint, Tone.Good);
            A(290, "There. The balance has shifted.", 1.4f);
            A(291, "Their mistake has become our opportunity.");
            A(292, "Momentum is changing hands. Let it remain with us.");
            A(293, "One opening was enough.", 1.3f);
            A(294, "Their advantage has begun to fracture.");
            A(295, "At last. The battlefield is becoming intelligible again.");
            A(296, "They have exposed the weakness beneath their strength.");
            A(297, "The enemy is hesitating. That hesitation is ours to exploit.");
            A(298, "The tide does not turn by itself. Someone gives it direction.", 1.2f);
            A(299, "A single decision has altered the field.");
            A(300, "The enemy believed our strength exhausted. They were mistaken.");
            A(301, "Now the battlefield belongs to whoever understands the moment.");
            A(302, "Their confidence has cracked.");
            A(303, "The opportunity is real. Such things do not remain real forever.");
            A(304, "This may be remembered as the hour the battle changed.", 1.1f);

            S(Moment.TurningPoint, Tone.Bad);
            A(305, "About time.", 1.5f);
            A(306, "The opening is here. It must not be wasted.");
            A(307, "They have given us what we needed.");
            A(308, "The enemy is vulnerable. Exploitation must follow recognition.");
            A(309, "Their mistake has purchased us a chance. We should spend it without mercy.");
            A(310, "The battle has finally offered something other than resistance.");
            A(311, "Their formation is compromised.");
            A(312, "We have leverage now. Do not return it.");
            A(313, "The enemy has blinked. Once is enough.", 1.4f);
            A(314, "We have suffered for this opening. It had better be worth it.", 1f, Cue.Costly);
            A(315, "Their momentum is broken. Break something more permanent.");
            A(316, "This is the first sensible thing the battlefield has offered us.", 1.3f);
            A(317, "They are exposed. I have no patience for missed opportunities.");
            A(318, "The balance has shifted. Keep it shifted.");
            A(319, "We were given one chance. Fortunately, one is all that is required.", 1.3f);

            // Calm: nothing has died for a while and nothing is shooting at it.
            S(Moment.Calm, Tone.Neutral);
            A(320, "The battlefield is quiet. That is not the same as safe.", 1.3f);
            A(323, "Silence often conceals movement. Movement reveals intention.");
            A(324, "Every battle is decided twice: once before it begins, and once after contact.");
            A(328, "War rewards those who arrive mentally before they arrive physically.");
            S(Moment.Calm, Tone.Good);
            A(321, "Preparation is the oldest form of victory.");
            A(322, "Let the enemy spend their strength deciding what we have already understood.");
            A(325, "There is no need to fear what has not yet happened. There is value in preparing for it.");
            A(329, "The next shot has not been fired. The battle has already begun.");
            A(403, "Keep the ore flowing and the guns fed. I will keep the rest of them honest.", 1.2f);
            A(406, "{callsign} standing ready. Your people fight well, Commander.", 1.2f);
            S(Moment.Calm, Tone.Superior);
            A(326, "The enemy is still choosing. I have already considered the consequences.", 1.3f);
            A(327, "They are preparing for battle. I am preparing for their preparation.", 1.4f);

            // After a battle won decisively, and at the end of a match won.
            S(Moment.Victory, Tone.Solemn);
            A(330, "The field is ours. Remember the cost of making it so.", 1f, Cue.Costly);
            A(331, "The enemy is defeated. The fallen remain.", 1f, Cue.Costly);
            A(333, "Another battle is finished. Another debt has been paid.");
            A(336, "Do not let triumph make you forget how close the grave can be.");
            A(338, "There will be another enemy. There will always be another enemy.");
            A(340, "Rest while you can. War rarely grants a second invitation.", 1.3f);
            S(Moment.Victory, Tone.Good);
            A(332, "Victory has been secured. Let discipline preserve what courage achieved.");
            A(334, "Today, we proved that strength guided by reason can endure.");
            A(337, "The objective stands because we stood with it.");
            S(Moment.Victory, Tone.Superior);
            A(335, "The outcome was never mysterious. Only the path was.", 1.3f);
            A(339, "We have done what lesser forces could not.");

            // After a battle lost decisively, near the end, and at the end of a match lost.
            S(Moment.Defeat, Tone.Solemn);
            A(341, "This battle is lost. Our purpose is not.");
            A(343, "Defeat is information purchased at terrible cost. Learn from it.");
            A(346, "Remember this feeling. It is an efficient teacher.");
            A(348, "The fallen cannot return. Their sacrifice must therefore produce wisdom.", 1f, Cue.Costly);
            A(350, "A battle may be lost in an hour. An army is not rebuilt in one.");
            A(353, "Failure is survivable. Repeating it is not.");
            S(Moment.Defeat, Tone.Good);
            A(342, "The field has been taken from us. It can be taken back.");
            A(347, "We have survived the defeat. That is the beginning of recovery.");
            A(351, "We remain operational. Therefore, the story is unfinished.", 1.2f);
            S(Moment.Defeat, Tone.Superior);
            A(344, "The enemy has won this engagement. They have not surpassed us in every measure.");
            A(349, "They have taken the field. I have already begun calculating how it is reclaimed.");
            S(Moment.Defeat, Tone.Bad);
            A(345, "We failed. Do not soften the word.", 1.2f);
            A(352, "This was beneath our standard. It must never become our pattern.");

            // The iconic lines: now and then, in place of the moment's own.
            S(Moment.Any, Tone.Solemn);
            A(354, "Steel does not possess courage. It possesses purpose.", 1.2f);
            A(355, "Remember what stands behind you. Remember what lies ahead.");
            A(358, "Wars are not won by those without fear. They are won by those who refuse to obey it.");
            A(360, "The battlefield does not remember intentions. It remembers what remained standing.");
            A(362, "There are moments when armies become history. This is not yet ours.");
            A(364, "When fear speaks, let discipline answer.");
            A(366, "Steel breaks. Resolve breaks later.");
            A(368, "Stand firm. The universe has never rewarded those who surrendered before necessity demanded it.", 1f, Cue.Hard);
            A(370, "Let every surviving soldier remember: survival itself is an act of defiance.", 1f, Cue.Hard);
            A(372, "There is always a point beyond which retreat becomes surrender. We have not reached it.", 1f, Cue.Hard);
            A(374, "Hold fast. The night is darkest where the battle is greatest.", 1f, Cue.Hard);
            A(376, "The dead have entrusted the living with the future. Spend that trust carefully.", 1f, Cue.Costly);
            A(378, "Our weapons are finite. Our resolve must not be.");
            A(380, "Where others see a battlefield, I see a sequence of decisions.");
            A(382, "The hour is difficult. Difficult hours forge the stories armies are remembered by.", 1f, Cue.Hard);
            A(384, "Stand. Endure. The enemy must cross you before they reach what lies behind you.", 1f, Cue.Hard);
            A(386, "When this war is forgotten, what remains will be what we refused to yield.");
            A(388, "The machine continues. Therefore, hope continues.", 1f, Cue.Hard);
            A(390, "Our fate is not decided by the first wound.", 1f, Cue.Hard);
            A(392, "There is still ground beneath our feet. That is enough.", 1f, Cue.Hard);
            A(394, "Remain steady. Panic has never improved a firing solution.", 1.3f);
            A(396, "The battle asks for endurance. Give it endurance.", 1f, Cue.Hard);
            A(398, "The horizon does not belong to those who reach it first. It belongs to those who remain.");
            A(400, "Let preparation become victory.");
            S(Moment.Any, Tone.Superior);
            A(356, "I have watched civilisations rise behind walls that were believed impregnable. None were.");
            A(357, "I have seen stronger armies than theirs. I have seen weaker armies than ours.");
            A(359, "I do not require certainty of victory. Only sufficient certainty of purpose.");
            A(361, "You need not know everything I know. You need only refuse to waste what I know.");
            A(363, "I was built for wars such as this. I have no intention of disappointing my makers.");
            A(365, "I have measured the enemy. They are dangerous. They are not enough.", 1.3f);
            A(367, "There is knowledge earned only through survival. I have earned much of it.");
            A(369, "The enemy possesses weapons. We possess understanding.");
            A(371, "I do not fear the enemy. I respect their capacity to make mistakes.");
            A(373, "You may doubt the outcome. I do not.", 1.2f);
            A(375, "I have been wrong before. I have also survived being wrong.", 1.3f);
            A(377, "They believe strength is measured in numbers. History repeatedly proves otherwise.");
            A(379, "I do not promise victory. I state that victory remains possible.", 1f, Cue.Hard);
            A(381, "The enemy has experience. I have memory.", 1.4f);
            A(383, "I know what this battle can become. Do not let it become worse.", 1f, Cue.Hard);
            A(385, "They may possess greater numbers. They do not possess greater certainty.", 1f, Cue.Hard);
            A(387, "I have no instinct for surrender. It was never installed.", 1.5f);
            A(389, "Let the enemy exhaust themselves against a force that understands endurance.");
            A(391, "I have seen impossible positions become victories. This is merely a difficult one.", 1f, Cue.Hard);
            A(393, "The enemy may be stronger than expected. They are not beyond understanding.", 1f, Cue.Hard);
            A(395, "I know precisely how dangerous this moment is. That is why I remain calm.", 1f, Cue.Hard);
            A(397, "You need not be stronger than the enemy everywhere. Only stronger where it matters.");
            A(399, "I have no faith in miracles. I have faith in preparation.");

            // ---- 407-506: the lore's lines (docs/lore/starforge-lore.md). The core's voice:
            // old, certain, dry. None answers what section 11 leaves open or takes a wing's side.
            S(Moment.Calm, Tone.Neutral);
            A(407, "Both camps sang vespers to the same star last night. It has not answered either.", 1.3f);
            A(408, "Your surveyors follow the ore. So do theirs. Maps are generous that way.", 1.3f);
            A(409, "Both hosts swore the same oath. One of them will be wrong by evening.");
            A(410, "The seams ran this way before your Empire had a name. They are shorter today.");
            A(411, "There were farms here once. The walls remember where. Use them.");
            A(412, "It is quiet. Across the valley, someone is amending your file. Give them less to write.", 1.2f);
            S(Moment.Calm, Tone.Good);
            A(413, "Wolves took this valley back when the farms were cleared. They will take it back from us. I find that steadying.");
            A(414, "A knight asks for no gifts and refuses none. I count a quiet hour among them.");
            A(415, "Your soldiers stand well. Some of their great-grandparents ploughed this ground. It shows.", 1.2f);
            A(416, "The summons is answered. That is all the law asks of us. The rest we do for reasons of our own.");
            A(417, "The Diggers swore to the Foundry, not to this war. Guard them accordingly.");
            S(Moment.Calm, Tone.Superior);
            A(418, "By my reckoning this is a short watch. Use it well.");
            A(419, "They study your file. I was old when their archive was founded. Let them study.", 1.3f);
            A(420, "The Vigilants fitted me in the last seconds before the drop. I have had a thousand years to prepare for improvisation.", 1.2f);

            S(Moment.Any, Tone.Solemn);
            A(421, "It is called the Long Night. Nights are for keeping watch.", 1.3f);
            A(422, "Once a wall was made that meant to stand. Now walls need soldiers. Stand.", 1.2f, Cue.Hard);
            A(423, "The fire went out and the watch went on. That is the whole of what I know about endurance.", 1f, Cue.Hard);
            A(424, "The dead have gone into the light. That is all the Luminary will say, and more than I can add.", 1f, Cue.Costly);
            A(425, "An oath is the last thing your age knows how to forge. Do not break one lightly.");
            A(426, "By the hand that holds and the hand that blesses. They swore it too. Fight them anyway, and do not hate them.", 1.2f);
            A(427, "The star is still where it was. It has watched worse hours than this one and said nothing.", 1f, Cue.Hard);
            A(428, "I kept my purpose when I lost the power to act on it. Your soldiers are being asked for less.", 1f, Cue.Hard);
            S(Moment.Any, Tone.Superior);
            A(429, "I was lit at a fire you have only read about. Do not ask whether I miss it.", 1.4f);
            A(430, "One wing would relight it, one would bury it, and two would carry it elsewhere. I guarded it, and I have learned to distrust all four ambitions.", 1.2f);
            A(431, "No one can make another of me. I mention it only to explain my caution.");
            A(432, "The Empire is seven centuries old. I try to be patient with the young.", 1.2f);
            A(433, "My makers left no names. Only work that outlived them. I am content to be evidence.");
            A(434, "Your Empire is ruled from a star nobody has visited twice. I have served stranger arrangements.");
            A(435, "The old houses built their walker in my likeness, and the Host kept the pattern. It has no core and nothing to say. I am told it is reliable.");
            A(436, "I remember the night the fire failed. I am not going to tell you about it.", 1.3f);

            S(Moment.Victory, Tone.Solemn);
            A(437, "It went our way. Both hosts will sing vespers tonight. One will have fewer voices.");
            A(438, "They swore the oath you swore. Remember that when you count them.", 1.2f);
            A(439, "It is won. Two seals could have spared every one of these. They were not used.", 1f, Cue.Costly);
            A(440, "Another stretch of the seam is ours. We have not found what the seam leads to. We have found the cost.", 1f, Cue.Costly);
            A(441, "No grudge is permitted. That rule is older than your Empire, and it holds today.");
            S(Moment.Victory, Tone.Good);
            A(442, "Well fought. The summons was for a decided field, and you are deciding it.");
            A(443, "Well done. Your soldiers have earned their capes today.");
            A(444, "That was soundly done. I shall say so when the order asks what manner of host raised my gantry.");
            S(Moment.Victory, Tone.Superior);
            A(445, "They had a dossier on you. It appears to be out of date.", 1.4f);
            A(446, "I have seen this done worse by people the chronicles call great.");

            S(Moment.Defeat, Tone.Solemn);
            A(447, "The fire went out once and the world continued. So shall we.", 1.3f);
            A(448, "We lost ground. Nobody here lost an oath. Remember which of the two the Host is built on.");
            A(449, "Not every core that falls wakes again. I say it so that you know what was staked beside you.", 1f, Cue.Costly);
            A(450, "Lauds is sung at sunrise whatever the night held. Be there to hear it.");
            S(Moment.Defeat, Tone.Good);
            A(451, "They hold that ground. They do not hold the seam. There is more of this war further along it.");
            A(452, "A thing may keep its purpose and lose a battle. I have done both, more than once.", 1.2f);
            A(453, "We are beaten on this ground. The oath did not mention this ground.");
            S(Moment.Defeat, Tone.Superior);
            A(454, "Every wing will cite today as proof of its case. They always do. I stopped keeping count long ago.");
            S(Moment.Defeat, Tone.Bad);
            A(455, "Badly done. I have watched hosts lose with more care than that.");
            A(456, "They knew what you would do because you had done it before. Their file on you is thicker now.", 1.2f);

            S(Moment.Critical, Tone.Good);
            A(457, "I have stood through a longer night than this one. Stand with me.", 1.3f);
            A(458, "A machine that meant to guard. That was the whole of my making. It is sufficient for today.");
            A(459, "They are all round us. So is the dark, and I am well practised with that.", 1f, Cue.Surrounded);
            A(460, "The orders stake one core on a field. This is the hour it was staked for.");
            S(Moment.Critical, Tone.Bad);
            A(461, "If I fall here I am not replaced. Neither are you. Act accordingly.", 1.3f);
            A(462, "A knight is owed to this field once. The debt is being collected now. Hold.");
            A(463, "This is how a watch fails: not at once, but one post at a time. Hold yours.");
            A(464, "We are losing soldiers faster than the celebrants can reach the dying.", 1f, Cue.Costly);

            S(Moment.TurningPoint, Tone.Good);
            A(465, "There. Even a long night turns.", 1.3f);
            A(466, "The Mother is silent and the Khan is silent. I advise you to be louder than both.", 1.2f);
            A(467, "That was not in their file on you. Do it again before they amend it.");
            S(Moment.TurningPoint, Tone.Bad);
            A(468, "At last. I have waited a thousand years for less.", 1.2f);
            A(469, "They have slipped. Do not wait for both seals to act on it.");
            A(470, "An opening. The last one this size closed while a council debated it.");

            S(Moment.Attack, Tone.Good);
            A(471, "They dig the same seam from the other end. Let us meet them in the middle.", 1.2f);
            A(472, "They came to secure the site. So did we. Let us be the more secure.");
            S(Moment.Attack, Tone.Bad);
            A(473, "Two hosts, one oath, and we are the ones dithering. Finish it.");
            S(Moment.Defence, Tone.Good);
            A(474, "Let them come. I was made to guard, and they have kindly supplied the occasion.", 1.2f);
            A(475, "Farmers held this ground for centuries with ploughs. We have guns. Hold it.");
            S(Moment.Defence, Tone.Bad);
            A(476, "The wall will not stand by itself. That stopped a thousand years ago. Reinforce it.");
            A(477, "They are through the outer line. This is the part the oath was written for.");
            S(Moment.LosingAttack, Tone.Good);
            A(478, "We are behind. The Night has been ahead of us since before your Empire, and we are still here.");
            A(479, "Their dossier says you will fall back now. Disappoint it.", 1.3f);
            S(Moment.LosingAttack, Tone.Bad);
            A(480, "We are spending soldiers to learn what a survey would have told us.", 1f, Cue.Costly);
            S(Moment.LosingDefence, Tone.Good);
            A(481, "Give them the ground. This valley has been lost before, and here its children stand.");
            A(482, "Fall back and keep the watch. Walls can be printed again.", 1.2f);
            S(Moment.LosingDefence, Tone.Bad);
            A(483, "They are taking this base apart faster than a printworks can raise it.");
            A(484, "I have outlasted every wall I was set to guard. I would rather not add this one. Tighten the line.");
            S(Moment.WinningAttack, Tone.Good);
            A(485, "They are falling back along the seam they came by. Follow the ore. Everyone else has.", 1f, Cue.Retreating);
            A(486, "Their line is going out lamp by lamp. I have seen how that ends.");
            S(Moment.WinningAttack, Tone.Bad);
            A(487, "Ahead is not decided, and the orders count only decided fields. Stop giving this one back.");
            S(Moment.WinningDefence, Tone.Good);
            A(488, "They broke on us. These hills have seen ploughs, wolves and now this. They remain unimpressed.");
            A(489, "Thrown back. Let them sing about it at vespers.", 1.2f);
            S(Moment.WinningDefence, Tone.Bad);
            A(490, "We held. The price was paid in soldiers who swore the same words as the ones they stopped.", 1f, Cue.Costly);

            // Each order's own two (its teaching, lore section 7): one for a quiet spell, one iconic.
            // The names are MechParts.Orders', letter for letter: Check() finds one no design draws.
            const string Ashen = "the Ashen Covenant", Lantern = "the Order of the Iron Lantern", Ninth = "the Ninth Vigil",
                         Sable = "the Sable Oath", Crown = "the Order of the Broken Crown", Ember = "the Emberguard",
                         Wardens = "the Wardens of the Long Night", Brazen = "the Brazen Host";
            S(Moment.Calm, Tone.Neutral);
            O(Ashen, 491, "The fire is dead. What it made is not. That is the whole of our rule, and it has been enough.");
            O(Ninth, 495, "The ninth watch was never relieved. This one will not be either.");
            O(Sable, 497, "An oath needs no picture. It needs keeping. See that your soldiers know which they swore.");
            O(Wardens, 503, "The Night is the age appointed. We were not asked to like it. We were asked to stand it well.");
            S(Moment.Calm, Tone.Good);
            O(Lantern, 493, "Light is carried, not awaited. Find me a road and I will carry it down it.");
            O(Ember, 501, "An ember is tended, not hurried. So is a battle. So, if you will permit it, are you.");
            O(Brazen, 505, "The Makers were smiths first. The work is the offering. Find me some work.");
            S(Moment.Calm, Tone.Superior);
            O(Crown, 499, "We serve no crowned head. A lit gantry is another matter. You have my guns.");
            S(Moment.Any, Tone.Solemn);
            O(Ashen, 492, "The Covenant does not hurry. Nothing we guard has anywhere else to be.");
            O(Lantern, 494, "The lantern on our standard is unlit. It is the bearer that matters.");
            O(Sable, 498, "Our standard is plain black. There was nothing to add.");
            O(Ember, 502, "The cores are the last embers. I would know. Bank your strength and it will last the night.");
            O(Wardens, 504, "Others wait for morning. The Wardens keep the watch as though none were promised.");
            S(Moment.Any, Tone.Superior);
            O(Ninth, 496, "The Vigil was there. That is all it says, and I say less.");
            O(Crown, 500, "We broke a crown once rather than hand it over. A line is less to ask of us.");
            O(Brazen, 506, "The other orders dispute our descent from the forge-hands. They are welcome to dispute it with the hammer.");

            // ---- 507-519: the expanded lore (2026-11-23): the iron, the houses' walkers, the lance,
            // four wings, the hours. 514 is the one Old Language line (old-language.md section 8:
            // one phrase, the line reads without it; said at most once a match like every line).
            S(Moment.Calm, Tone.Neutral);
            A(507, "Every breastplate in your line has other names scratched inside it. See that today adds as few as it must.", 1.2f);
            A(508, "My frame was forged in my order's hold. The gifts on it are your Host's. The core is older than both.");
            S(Moment.Calm, Tone.Good);
            A(509, "Keep the night, your people say at compline. It is sound tactical advice.");
            S(Moment.Calm, Tone.Superior);
            A(510, "The houses built walkers when no order would answer them. They still walk. They still have nothing to say.");
            A(511, "Four wings, four plans for one hearth, and not one of them has seen it. I find the agreement touching.", 1.2f);
            S(Moment.Any, Tone.Solemn);
            A(512, "They fight faceless, as the Makers worked. Nobody asks the iron who is inside, and the iron does not say.");
            A(513, "Every ninth night the halls go dark and are lit again from one flame. I have watched it a long time. It has not yet failed.", 1.2f);
            A(514, "Lighttheinto gangen. The rest of us still have work.", 0.6f, Cue.Costly);
            S(Moment.Any, Tone.Superior);
            A(515, "Knights call the houses' walker a kettle. It is unkind, and it is accurate.", 1.2f);
            A(516, "The lance was licensed against knights who forget the oath, and against the houses' copies. I have forgotten nothing.");
            S(Moment.Victory, Tone.Solemn);
            A(517, "Their celebrants and ours will both be busy tonight. Nobody aims at a white alb. It is the last courtesy left.", 1f, Cue.Costly);
            S(Moment.Critical, Tone.Good);
            A(518, "Your soldiers swore to stand in their wing until released. Nobody has released them. Nor me.", 1.2f);
            S(Moment.Defence, Tone.Good);
            A(519, "Field walls, house corners and iron. The farmers would have recognised two of them.");

            // ---- 528-530 (2026-11-24, lore sections 4 and 6): the twin houses, the watchmen, a knight's fire.
            // After the counsel lines (520-527), so the table runs 1-519 and 528 on (ExpectedNumbers).
            S(Moment.Calm, Tone.Neutral);
            A(528, "Somewhere a twin house is singing one of the hours. In all my memory they have not let one lapse.");
            A(529, "The watchmen keep the night and hold no office for it. We keep it by an older use, and hold none either.");
            S(Moment.Any, Tone.Superior);
            A(530, "Fire in a knight's frame is judged, not handed out. Your lamplighters are only handed it.");
            return L.ToArray();
        }

        static int[][] IndexBuckets()
        {
            var lists = new List<int>[BucketCount];
            for (int b = 0; b < BucketCount; b++) lists[b] = new List<int>();
            for (int i = 0; i < Lines.Length; i++) lists[Lines[i].Bucket].Add(i);
            var r = new int[BucketCount][];
            for (int b = 0; b < BucketCount; b++) r[b] = lists[b].ToArray();
            return r;
        }

        // ------------------------------------------------------------ choosing
        /// <summary>What prompts a line.</summary>
        public enum Occasion { None, Ambient, FightWon, FightLost, TurningPoint, Critical, NearDefeat, MatchWon, MatchLost }

        /// <summary>How the battle stands when it speaks: all a line is chosen by.</summary>
        public struct Reading
        {
            public Occasion occasion;
            /// <summary>-1 losing, 0 even, 1 winning (over the whole map).</summary>
            public int standing;
            public bool calm, attack, good, home, decisive, costly, longLosing;
            public float temper, pride;
            public Cue cues;
        }

        /// <summary>How well each bucket (situation and tone) fits a reading. Pure, so the
        /// static check can walk every reading and prove every line can be said.</summary>
        public static void Fits(Reading r, List<(int bucket, float fit)> into)
        {
            into.Clear();
            Tone mood = r.good ? Tone.Good : Tone.Bad, other = r.good ? Tone.Bad : Tone.Good;
            bool grim = r.standing < 0 || !r.good;
            void Add(Moment m, Tone t, float f) { if (f > 0f) into.Add((BucketOf(m, t), f)); }
            void Iconic(float k)
            {
                Add(Moment.Any, Tone.Solemn, k * (grim ? 0.6f : 0.4f));
                Add(Moment.Any, Tone.Superior, k * (grim ? 0.4f : 0.6f) * (0.6f + 0.6f * r.pride));
            }
            void Won(float k)
            {
                Add(Moment.Victory, Tone.Solemn, k * (0.5f + (r.costly ? 1f : 0f)));
                Add(Moment.Victory, Tone.Good, k * 0.8f);
                Add(Moment.Victory, Tone.Superior, k * (r.costly ? 0.15f : 0.3f + 0.7f * r.pride));
            }
            void Lost(float k)
            {
                Add(Moment.Defeat, Tone.Solemn, k * (0.6f + (r.costly ? 0.6f : 0f)));
                Add(Moment.Defeat, Tone.Bad, k * (0.3f + Mathf.Max(0f, -r.temper)));
                Add(Moment.Defeat, Tone.Good, k * (r.good ? 0.8f : 0.3f));
                Add(Moment.Defeat, Tone.Superior, k * (0.2f + 0.6f * r.pride));
            }
            Moment Posture(bool attack, int standing) => attack
                ? (standing > 0 ? Moment.WinningAttack : standing < 0 ? Moment.LosingAttack : Moment.Attack)
                : (standing > 0 ? Moment.WinningDefence : standing < 0 ? Moment.LosingDefence : Moment.Defence);

            switch (r.occasion)
            {
                case Occasion.Ambient:
                    if (r.calm)
                    {
                        Add(Moment.Calm, Tone.Neutral, 1f);
                        Add(Moment.Calm, Tone.Good, 1f + 0.3f * r.temper);
                        Add(Moment.Calm, Tone.Superior, 0.3f + 0.7f * r.pride);
                        Iconic(0.5f);
                    }
                    else
                    {
                        Moment p = Posture(r.attack, r.standing);
                        Add(p, mood, 1f);
                        Add(p, other, 0.15f);
                        if (r.standing != 0) Add(r.attack ? Moment.Attack : Moment.Defence, mood, 0.3f);
                        Iconic(0.15f);
                    }
                    break;
                case Occasion.FightWon:
                    if (r.home)
                    {
                        // An assault on the base thrown back.
                        Tone held = r.costly ? Tone.Bad : Tone.Good;
                        Add(Moment.WinningDefence, held, r.standing < 0 ? 0.5f : 1f);
                        Add(Moment.WinningDefence, held == Tone.Good ? Tone.Bad : Tone.Good, 0.2f);
                        if (r.standing < 0) Add(Moment.LosingDefence, Tone.Good, 0.8f);
                        if (r.decisive) Won(0.4f);
                    }
                    else if (r.decisive) { Won(1f); Add(Moment.WinningAttack, Tone.Good, 0.4f); }
                    else if (r.standing < 0) { Add(Moment.LosingAttack, Tone.Good, 1f); Add(Moment.Attack, Tone.Good, 0.3f); }
                    else
                    {
                        Add(r.standing > 0 ? Moment.WinningAttack : Moment.Attack, Tone.Good, 1f);
                        Add(r.standing > 0 ? Moment.Attack : Moment.WinningAttack, Tone.Good, 0.3f);
                    }
                    break;
                case Occasion.FightLost:
                    if (r.decisive) Lost(r.home ? 0.6f : 1f);
                    if (r.home)
                    {
                        // Never a confident mid-assault line after losing one ("a disciplined
                        // defence does not fear..."); the good-tempered answer is the
                        // unbroken one (we yield ground, not purpose).
                        Add(r.standing < 0 ? Moment.LosingDefence : Moment.Defence, Tone.Bad, 1f);
                        Add(Moment.LosingDefence, Tone.Good, r.good ? 0.5f : 0.2f);
                    }
                    else
                    {
                        // Winning overall and still losing a fight: carelessness.
                        Moment a = r.standing > 0 ? Moment.WinningAttack : r.standing < 0 ? Moment.LosingAttack : Moment.Attack;
                        Add(a, Tone.Bad, 1f);
                        if (a == Moment.LosingAttack) Add(Moment.LosingAttack, Tone.Good, r.good ? 0.6f : 0.2f);
                    }
                    break;
                case Occasion.TurningPoint:
                    Add(Moment.TurningPoint, Tone.Good, 1f);
                    // Irritable after a long spell on the back foot, or by nature.
                    Add(Moment.TurningPoint, Tone.Bad, 0.25f + (r.longLosing ? 1f : 0f) + 0.8f * Mathf.Max(0f, -r.temper));
                    break;
                case Occasion.Critical:
                    Add(Moment.Critical, mood, 1f);
                    Add(Moment.Critical, other, 0.3f);
                    Iconic(0.15f);
                    break;
                case Occasion.NearDefeat:
                    Lost(1f);
                    Add(Moment.LosingDefence, Tone.Bad, 0.5f);
                    Add(Moment.LosingDefence, Tone.Good, 0.3f);
                    Add(Moment.Critical, mood, 0.3f);
                    Iconic(0.2f);
                    break;
                case Occasion.MatchWon: Won(1f); break;
                case Occasion.MatchLost: Lost(1f); break;
            }
        }

        static float CueFit(Cue lineCue, Cue now) =>
            lineCue == Cue.None ? 1f : (lineCue & now) != 0 ? 3f : 0.3f;

        // ------------------------------------------------------------ one Mech's voice
        /// <summary>How long nothing must die for a fight to be over.</summary>
        const float FightGap = 15f;
        /// <summary>Never two of its lines closer than this.</summary>
        const float MinGap = 90f;
        /// <summary>Flavour yields to the calls: never this soon after any comms line.</summary>
        const float QuietBefore = 30f;
        /// <summary>At most this many of its lines in any ten minutes.</summary>
        const int MaxPerTenMinutes = 4;
        /// <summary>A fight worth a word afterwards: this much lost between both sides.</summary>
        const float FightWorthSaying = 600f;
        /// <summary>A moment's line is dropped if it has not been said by then.</summary>
        const float PendingLife = 35f;

        readonly GameWorld w;
        readonly int team;
        readonly Rng rng;
        readonly float temper, pride;
        readonly bool[] said = new bool[Lines.Length];
        readonly float[] saidAt = new float[Lines.Length];
        readonly List<(int bucket, float fit)> fits = new List<(int, float)>(16);
        readonly List<Unit> structs = new List<Unit>(32);
        MechIntent mode;

        // The balance of the whole battle, smoothed: mostly the armies and Mechs, partly the
        // bases (their structures and workers stood still while the armies traded, and held
        // it at "even" through a match one side was losing), and its last two minutes every
        // 5 s for the turning point.
        float standing, military, bases, nextSample, nextHistory, nextStandingNote;
        readonly float[] history = new float[24];
        int historyCount, historyHead;
        float losingSince = -1f, losingSpell;
        Cue cues;
        float assault;
        int ourStructures;
        bool foundry;

        // Losses: decaying (for mood) and per fight -- deaths within FightReach of where it
        // started, with no gap of FightGap (one feed for the whole map never went quiet, so
        // no fight ever ended). Deaths near the Mech, for a local calm.
        float lostUs, lostThem, structLoss, decayT;
        bool fightOpen;
        float lastDeathT = -99f, lastFightDeathT = -99f, lastNearDeathT = -99f, lastFightEnd = -99f;
        float fightStart, fightUs, fightThem, fightHomeW, fightW;
        Vector2 fightSum;
        const float FightReach = 60f;

        // What it will say next.
        Occasion pending;
        int pendingPrio;
        float pendingUntil;
        bool pendingHome, pendingDecisive, pendingCostly, pendingLong;
        float landedAt = -1f, nextAmbient, lastSpeech = -99f, lastTurn = -999f, lastCritical = -999f, lastNearDefeat = -999f;
        bool finalSaid;
        // When anything was last said on its side's comms (its own lines too; not repeats),
        // and when its own last few lines were, for the ten-minute cap.
        float lastLineT = -99f;
        readonly float[] lately = { -999f, -999f, -999f, -999f };
        int latelyHead;

        Vector2 Home => w.Map.StartPos(team);
        Vector2 Foe => w.Map.StartPos(1 - team);

        public MechSpeech(GameWorld world, int team, uint seed)
        {
            w = world;
            this.team = team;
            rng = new Rng(seed ^ 0x5BEE7u ^ (uint)(team * 104729));
            temper = rng.Range(-1f, 1f);
            pride = rng.F01();
            InitCounsel(seed);   // its own stream (MechSpeech.Counsel): rng draws nothing new
            if (team == 0) Journal.Clear();
            w.Event += OnEvent;
        }

        /// <summary>A voice with no world, for <see cref="Check"/>: all it can do is <see cref="Say"/>.</summary>
        MechSpeech(uint seed, float temper, float pride)
        {
            team = 1;   // nothing is journalled (Log returns before it reads the world)
            rng = new Rng(seed);
            this.temper = temper;
            this.pride = pride;
        }

        float checkClock;
        /// <summary>The match clock, or the check's own where there is no world.</summary>
        float Clock => w != null ? w.time : checkClock;

        public void Dispose() => w.Event -= OnEvent;

        /// <summary>The pilot's temper (-1 cold .. 1 warm) and pride (0..1), drawn from the seed.</summary>
        public float Temper => temper;
        public float Pride => pride;

        /// <summary>How the pilot feels right now, above 0 good and below bad: the reading
        /// <see cref="Read"/> takes, without its random nudge, so asking it does not shift
        /// what the Mech says next.</summary>
        public float Mood(Unit m)
        {
            DecayTo(w.time);
            float hull = m.hp / Mathf.Max(1f, m.MaxHp);
            float exchange = (lostThem - lostUs) / (lostThem + lostUs + 200f);
            return 0.8f * exchange + 0.6f * (hull - 0.55f) + 0.5f * standing + 0.25f * temper;
        }

        /// <summary>Read the battle (about once a second) and note any moment worth a line.</summary>
        public void Observe(Unit m, MechIntent intent)
        {
            float now = w.time;
            mode = intent;
            if (landedAt < 0f)
            {
                if (!m.mech.Landed) return;
                landedAt = now;
                nextAmbient = now + rng.Range(90f, 130f);   // it has just said hello
            }
            if (fightOpen && now - lastFightDeathT >= FightGap) CloseFight();
            if (pending != Occasion.None && now >= pendingUntil)
            {
                Note($"{pending} expired unsaid");
                pending = Occasion.None;
                pendingPrio = 0;
            }
            if (now < nextSample) return;
            nextSample = now + 1f;
            Sample(m, now);
            if (now >= nextStandingNote)
            {
                nextStandingNote = now + 30f;
                Note($"standing {standing:+0.00;-0.00} (armies {military:+0.00;-0.00}, bases {bases:+0.00;-0.00}) cues {cues}");
            }

            // A turning point: the balance back from well down to about even or better.
            if (now >= nextHistory)
            {
                nextHistory = now + 5f;
                history[historyHead] = standing;
                historyHead = (historyHead + 1) % history.Length;
                historyCount = Mathf.Min(historyCount + 1, history.Length);
            }
            if (standing < -0.1f) { if (losingSince < 0f) losingSince = now; }
            else if (losingSince >= 0f) { losingSpell = now - losingSince; losingSince = -1f; }
            if (historyCount >= 6 && now - lastTurn >= 150f)
            {
                float lo = 1f;
                for (int i = 0; i < historyCount; i++) lo = Mathf.Min(lo, history[i]);
                if (lo <= -0.1f && standing - lo >= 0.2f && standing >= -0.05f)
                {
                    lastTurn = now;
                    float spell = losingSince >= 0f ? now - losingSince : losingSpell;
                    Mark(Occasion.TurningPoint, 2, false, false, false, spell >= 120f);
                }
            }

            // In extremis. (The hull test is under fire and near where the brain turns for the
            // bay, 38%: under 30% it never came, because the Mech had gone for repairs by then.)
            DecayTo(now);
            float hull = m.hp / Mathf.Max(1f, m.MaxHp);
            bool inCombat = now - m.lastDamagedT < 5f || now - lastDeathT < 10f;
            if (inCombat && now - lastCritical >= 100f &&
                ((hull < 0.42f && now - m.lastDamagedT < 4f) || ((cues & Cue.Surrounded) != 0 && hull < 0.55f) ||
                 (assault >= 500f && standing < -0.15f) || structLoss >= 1.8f || standing < -0.45f))
            {
                lastCritical = now;
                Mark(Occasion.Critical, 3, false, false, (cues & Cue.Costly) != 0, false);
            }

            // Close to losing everything.
            if (now - lastNearDefeat >= 150f &&
                ((ourStructures <= 2 && standing < -0.25f) || (!foundry && ourStructures <= 4 && standing < -0.35f)))
            {
                lastNearDefeat = now;
                Mark(Occasion.NearDefeat, 4, false, false, (cues & Cue.Costly) != 0, false);
            }
        }

        /// <summary>A line, if one is due: a moment's, else now and then one for how things
        /// stand. Called only when the brain has nothing to say itself.</summary>
        public string Next(Unit m, MechDesign d)
        {
            float now = w.time;
            if (landedAt < 0f || now - lastSpeech < MinGap || now - lastLineT < QuietBefore) return null;
            int inTen = 0;
            foreach (float t in lately) if (now - t < 600f) inTen++;
            if (inTen >= MaxPerTenMinutes) return null;
            Occasion o = pending != Occasion.None && now < pendingUntil ? pending
                       : now >= nextAmbient ? Occasion.Ambient : Occasion.None;
            if (o == Occasion.None) return null;
            var r = Read(m, o);
            pending = Occasion.None;
            pendingPrio = 0;
            string text = Say(r, d);
            // Nothing fitted: try again shortly, without spending a slot of the ten-minute cap.
            if (text == null) { nextAmbient = Mathf.Max(nextAmbient, now + 10f); return null; }
            lastSpeech = now;
            nextAmbient = now + (r.calm ? rng.Range(180f, 270f) : rng.Range(150f, 240f));
            lately[latelyHead] = now;
            latelyHead = (latelyHead + 1) % lately.Length;
            return text;
        }

        /// <summary>One last word as the match ends (once).</summary>
        public string Final(Unit m, MechDesign d, bool won)
        {
            if (finalSaid || landedAt < 0f) return null;
            finalSaid = true;
            var r = Read(m, won ? Occasion.MatchWon : Occasion.MatchLost);
            return Say(r, d);
        }

        void Mark(Occasion o, int prio, bool home, bool decisive, bool costly, bool longLosing)
        {
            float now = w.time;
            if (pending != Occasion.None && now < pendingUntil && prio < pendingPrio)
            {
                Note($"{o} dropped: {pending} is waiting");
                return;
            }
            Note($"{o} noted" + (pending != Occasion.None && now < pendingUntil ? $" (replacing {pending})" : "") +
                 (o == Occasion.FightWon || o == Occasion.FightLost ? $" {(home ? "at home" : "on their side")}{(decisive ? ", decisive" : "")}{(costly ? ", costly" : "")}" : ""));
            pending = o;
            pendingPrio = prio;
            pendingUntil = now + PendingLife;
            pendingHome = home;
            pendingDecisive = decisive;
            pendingCostly = costly;
            pendingLong = longLosing;
        }

        Reading Read(Unit m, Occasion o)
        {
            float now = w.time;
            DecayTo(now);
            // The moment's own facts, if this is the moment that was noted.
            bool noted = o == pending && o != Occasion.Ambient;
            var r = new Reading
            {
                occasion = o,
                standing = standing > 0.12f ? 1 : standing < -0.12f ? -1 : 0,
                temper = temper,
                pride = pride,
                home = noted && pendingHome,
                decisive = noted && pendingDecisive,
                longLosing = noted && pendingLong,
            };
            bool recent = fightOpen || now - lastFightEnd < 30f;
            // Calm: nothing dying anywhere; or, standing guard, nothing near it (in an AI-vs-AI
            // match something died somewhere every few seconds, and it never was).
            bool guarding = mode == MechIntent.Guarding || mode == MechIntent.Holding || mode == MechIntent.Repairing;
            r.calm = o == Occasion.Ambient &&
                     ((!fightOpen && now - lastDeathT > 25f && now - m.lastDamagedT > 15f) ||
                      (guarding && now - lastNearDeathT > 30f && now - m.lastDamagedT > 20f));
            // Attack or defence: where the fighting is, else what it is doing.
            r.attack = recent && fightW > 0f ? fightHomeW < fightW * 0.5f
                     : mode == MechIntent.Supporting || mode == MechIntent.Hunting ||
                       (mode == MechIntent.Duelling && (m.pos - Foe).sqrMagnitude < (m.pos - Home).sqrMagnitude);
            float hull = m.hp / Mathf.Max(1f, m.MaxHp);
            float exchange = (lostThem - lostUs) / (lostThem + lostUs + 200f);
            float mood = 0.8f * exchange + 0.6f * (hull - 0.55f) + 0.5f * standing + 0.25f * temper + rng.Range(-0.15f, 0.15f);
            r.good = mood >= 0f;
            r.costly = (noted && pendingCostly) || (cues & Cue.Costly) != 0;
            bool hard = r.standing < 0 || !r.good || o == Occasion.Critical || o == Occasion.NearDefeat ||
                        o == Occasion.FightLost || o == Occasion.MatchLost;
            r.cues = cues | (hard ? Cue.Hard : Cue.None) | (r.costly ? Cue.Costly : Cue.None);
            return r;
        }

        string Say(in Reading r, MechDesign d)
        {
            Fits(r, fits);
            float total = 0f;
            for (int i = 0; i < fits.Count; i++)
            {
                var (b, f) = fits[i];
                // Another order's lines are not this Mech's: they neither fill a bucket nor keep it fresh.
                bool fresh = Fresh(ByBucket[b], d, said, out bool any);
                if (!any) f = 0f;
                else if (!fresh) f *= 0.1f;
                fits[i] = (b, f);
                total += f;
            }
            if (total <= 0f) return null;
            float pick = rng.F01() * total;
            int bucket = fits[fits.Count - 1].bucket;
            foreach (var (b, f) in fits)
            {
                if (f <= 0f) continue;
                bucket = b;
                pick -= f;
                if (pick <= 0f) break;
            }

            int chosen = Choose(ByBucket[bucket], d, r.cues, said, saidAt, rng);
            if (chosen < 0) return null;
            said[chosen] = true;
            saidAt[chosen] = Clock;
            var line = Lines[chosen];
            string text = Fill(line.text, d);
            Note(r, line, text);
            return text;
        }

        /// <summary>Whether the bucket holds a line this Mech has not said yet; <paramref name="any"/>
        /// whether it holds one it may say at all. Static, for <see cref="Check"/>.</summary>
        static bool Fresh(int[] idx, MechDesign d, bool[] said, out bool any)
        {
            any = false;
            foreach (int k in idx)
            {
                if (!Lines[k].For(d)) continue;
                any = true;
                if (!said[k]) return true;
            }
            return false;
        }

        /// <summary>A line of the bucket for this Mech: by weight and by what it says, never one
        /// already said while the bucket has others it may say; once all are said, the one said
        /// longest ago. -1 if the bucket holds none for it. Static, so <see cref="Check"/> runs
        /// the very choice a match makes.</summary>
        static int Choose(int[] idx, MechDesign d, Cue cues, bool[] said, float[] saidAt, Rng rng)
        {
            float sum = 0f;
            foreach (int k in idx) if (!said[k] && Lines[k].For(d)) sum += Lines[k].weight * CueFit(Lines[k].cue, cues);
            int chosen = -1;
            if (sum > 0f)
            {
                float p = rng.F01() * sum;
                foreach (int k in idx)
                {
                    if (said[k] || !Lines[k].For(d)) continue;
                    chosen = k;
                    p -= Lines[k].weight * CueFit(Lines[k].cue, cues);
                    if (p <= 0f) break;
                }
            }
            else
            {
                float oldest = float.MaxValue;
                foreach (int k in idx) if (Lines[k].For(d) && saidAt[k] < oldest) { oldest = saidAt[k]; chosen = k; }
            }
            return chosen;
        }

        static string Fill(string text, MechDesign d)
        {
            if (text.IndexOf('{') < 0) return text;
            return text.Replace("{pilot}", d != null ? d.pilot : "The pilot")
                       .Replace("{callsign}", d != null ? d.callsign : "The Mech")
                       .Replace("{order}", d != null ? d.order : "my order")
                       .Replace("{Order}", Capital(d != null ? d.order : "My order"));   // at a sentence's start: "The Emberguard ..."
        }

        /// <summary>An order's name as a sentence begins with it ("the Ninth Vigil" is how a design spells it).</summary>
        public static string Capital(string s) =>
            string.IsNullOrEmpty(s) || char.IsUpper(s[0]) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ------------------------------------------------------------ reading the battle
        /// <summary>What a unit is worth to its side, for the balance and the losses.</summary>
        static float Worth(UnitType t, UnitDef d)
        {
            if (t == UnitType.Mech) return 0.5f * Defs.ArmyValue(UnitType.Mech);   // 1,500 when that was 3,200
            if (d.building) return Mathf.Max(100f, d.cost);
            if (t == UnitType.Worker || d.harvester) return d.harvester && t != UnitType.Worker ? d.cost : 50f;
            return Defs.ArmyValue(t);
        }

        void Sample(Unit m, float now)
        {
            // [0] ours, [1] theirs: armies and Mechs, and bases (structures and workers).
            float om = 0f, tm = 0f, ob = 0f, tb = 0f, eArmyV = 0f;
            Vector2 eArmy = Vector2.zero;
            ourStructures = 0;
            foundry = false;
            structs.Clear();
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team > 1) continue;
                bool mine = u.team == team;
                if (u.mech != null)
                {
                    // A Mech by its hull, but never under half: walking back to its bay to be
                    // mended should not read as its side losing.
                    if (!u.mech.Landed) continue;
                    float v = 0.5f * Defs.ArmyValue(UnitType.Mech) * (0.5f + 0.5f * Saturate(u.hp / Mathf.Max(1f, u.MaxHp)));
                    if (mine) om += v; else tm += v;
                }
                else if (u.def.building)
                {
                    if (!u.Complete) continue;
                    float v = Mathf.Max(100f, u.def.cost) * (0.5f + 0.5f * u.hp / Mathf.Max(1f, u.MaxHp));
                    if (mine) { ob += v; ourStructures++; structs.Add(u); if (u.Type == UnitType.Foundry) foundry = true; }
                    else tb += v;
                }
                else if (u.def.harvester) { float hv = u.Type == UnitType.Worker ? 50f : u.def.cost; if (mine) ob += hv; else tb += hv; }
                else if (u.def.IsArmy && !u.deserted)   // deserters have left the war
                {
                    float v = Defs.ArmyValue(u.Type);
                    if (mine) om += v;
                    else { tm += v; eArmy += u.pos * v; eArmyV += v; }
                }
            }
            military = (om - tm) / Mathf.Max(400f, om + tm);
            bases = (ob - tb) / Mathf.Max(1f, ob + tb);
            standing = Mathf.Lerp(standing, 0.7f * military + 0.3f * bases, 1f - Mathf.Exp(-1f / 12f));

            // What is true round the fight (or the Mech, between fights).
            Vector2 focus = (fightOpen || now - lastFightEnd < 30f) && fightW > 0f ? fightSum / fightW : m.pos;
            int maulers = 0, near = 0, fleeing = 0, around = 0, quads = 0;
            assault = 0f;
            foreach (var u in w.units)
            {
                if (u == null || u.dying || u.team == team || u.team > 1 || u.Untargetable) continue;
                if ((!u.def.IsArmy && u.mech == null) || u.deserted) continue;
                Vector2 p = u.pos;
                if ((p - focus).sqrMagnitude < 50f * 50f)
                {
                    near++;
                    if (u.Type == UnitType.Mauler) maulers++;
                    if (u.agent != null && u.agent.enabled)
                    {
                        Vector3 v3 = u.agent.velocity;
                        var v = new Vector2(v3.x, v3.z);
                        if (v.sqrMagnitude > 1f && Vector2.Dot(v.normalized, Norm(Foe - p)) > 0.5f) fleeing++;
                    }
                }
                Vector2 dm = p - m.pos;
                if (dm.sqrMagnitude < 30f * 30f)
                {
                    around++;
                    quads |= 1 << ((dm.x >= 0f ? 1 : 0) + (dm.y >= 0f ? 2 : 0));
                }
                foreach (var st in structs)
                {
                    float r = 35f + st.def.radius;
                    if ((st.pos - p).sqrMagnitude < r * r) { assault += u.mech != null ? 0.5f * Defs.ArmyValue(UnitType.Mech) : Defs.ArmyValue(u.Type); break; }
                }
            }
            cues = Cue.None;
            if (near >= 3 && fleeing * 2 >= near) cues |= Cue.Retreating;
            if (eArmyV >= 300f)
            {
                Vector2 c = eArmy / eArmyV;
                if ((c - Home).sqrMagnitude < (c - Foe).sqrMagnitude) cues |= Cue.Extended;
            }
            if (maulers >= 2) cues |= Cue.Artillery;
            int sides = 0;
            for (int q = 0; q < 4; q++) if ((quads & (1 << q)) != 0) sides++;
            if (sides >= 3 && around >= 5) cues |= Cue.Surrounded;
            DecayTo(now);
            // Costly: we have lost a good deal, and not much less than they have (in a grinding
            // match a fixed 300 was always met, and every line read as costly).
            bool recentFight = fightOpen || now - lastFightEnd < 30f;
            if ((lostUs >= 500f && lostUs >= 0.8f * lostThem) || (recentFight && fightUs >= 400f && fightUs >= 0.5f * fightThem))
                cues |= Cue.Costly;
        }

        /// <summary>Losses fade over about 45 s (for mood), structures lost over a minute.</summary>
        void DecayTo(float now)
        {
            float dt = now - decayT;
            if (dt <= 0f) return;
            decayT = now;
            float k = Mathf.Exp(-dt / 45f);
            lostUs *= k;
            lostThem *= k;
            structLoss *= Mathf.Exp(-dt / 60f);
        }

        void OnEvent(GameEvent e)
        {
            if (e.kind == GameEventKind.MechAdvice)
            {
                if (e.team != team) return;
                if (!e.repeat) lastLineT = w.time;
                // The brain's calls, for the journal (its flavour is noted as it is said).
                if (e.advice != MechAdviceKind.Morale) Log($"CALL {e.advice}{(e.repeat ? " repeat" : "")}: {e.text}");
                NoteCall(e);
                return;
            }
            if (e.kind != GameEventKind.Death || e.team > 1) return;
            // A deserter's death is no loss in the war, nor a fight: it had left it (and one
            // its own Mech put down would read as a fight lost at home).
            if (e.unit != null && e.unit.deserted) return;
            var def = e.unit != null ? e.unit.def : Defs.Get(e.type);
            if (def == null) return;
            float v = Worth(e.type, def);
            if (v <= 0f) return;
            float now = w.time;
            DecayTo(now);
            bool ours = e.team == team;
            if (ours) { lostUs += v; if (def.building) structLoss += 1f; }
            else lostThem += v;
            var p = new Vector2(e.pos.x, e.pos.z);
            lastDeathT = now;
            var mech = w.MechOf(team);
            if (mech != null && (p - mech.pos).sqrMagnitude < 70f * 70f) lastNearDeathT = now;
            // One fight at a time: deaths elsewhere do not keep it going.
            if (fightOpen && (p - fightSum / Mathf.Max(1f, fightW)).sqrMagnitude > FightReach * FightReach) return;
            if (!fightOpen)
            {
                fightOpen = true;
                fightStart = now;
                fightUs = fightThem = fightHomeW = fightW = 0f;
                fightSum = Vector2.zero;
            }
            if (ours) fightUs += v; else fightThem += v;
            fightSum += p * v;
            fightW += v;
            if ((p - Home).sqrMagnitude < (p - Foe).sqrMagnitude) fightHomeW += v;
            lastFightDeathT = now;
        }

        /// <summary>Nothing has died for a while: how did that fight go?</summary>
        void CloseFight()
        {
            fightOpen = false;
            lastFightEnd = w.time;
            if (landedAt < 0f || fightUs + fightThem < 250f) return;
            bool home = fightHomeW >= fightW * 0.5f;
            bool costly = fightUs >= 400f && fightUs >= 0.5f * fightThem;
            Note($"fight over after {w.time - fightStart - FightGap:0} s {(home ? "at home" : "on their side")}: we lost {fightUs:0}, they lost {fightThem:0}");
            if (fightUs + fightThem < FightWorthSaying) return;
            if (fightThem >= 1.5f * fightUs && fightThem >= 200f)
                Mark(Occasion.FightWon, 1, home, fightThem >= 600f && fightThem >= 2.5f * fightUs, costly, false);
            else if (fightUs >= 1.5f * fightThem && fightUs >= 200f)
                Mark(Occasion.FightLost, 1, home, fightUs >= 600f && fightUs >= 2.5f * fightThem, costly, false);
        }

        // ------------------------------------------------------------ checking
        /// <summary>What both Mechs said this match (newest last), for the checks below.</summary>
        static readonly List<string> Journal = new List<string>(256);

        void Note(in Reading r, in Line line, string text) =>
            Log($"SAY {r.occasion} {line.moment}/{line.tone} #{line.n} " +
                $"[{(r.standing > 0 ? "winning" : r.standing < 0 ? "losing" : "even")} {(r.calm ? "calm" : r.attack ? "attack" : "defence")} " +
                $"{(r.good ? "good" : "bad")} {standing:+0.00;-0.00} cues {r.cues}] {text}");

        /// <summary>What it noticed: moments noted, dropped and expired, fights, the standing.</summary>
        void Note(string what) => Log("    " + what);

        void Log(string entry)
        {
            if (team != 0) return;   // the journal is the player's Mech's: the opponent's never speaks
            if (Journal.Count >= 800) Journal.RemoveAt(0);
            int t = Mathf.FloorToInt(w.time);
            Journal.Add($"{t / 60}:{t % 60:00} T{team} {entry}");
        }

        /// <summary>The journal of the match so far: how many lines by occasion and bucket,
        /// then every line. <c>Tools/editor.sh call StarForge.AI.MechSpeech.JournalReport</c>.</summary>
        public static string JournalReport()
        {
            var sb = new StringBuilder();
            var byOcc = new SortedDictionary<string, int>();
            var byBucket = new SortedDictionary<string, int>();
            int spoken = 0, calls = 0, repeats = 0;
            float first = -1f, last = 0f;
            foreach (var j in Journal)
            {
                var parts = j.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                var mmss = parts[0].Split(':');
                float t = int.Parse(mmss[0]) * 60 + int.Parse(mmss[1]);
                if (first < 0f) first = t;
                last = t;
                if (parts[2] == "CALL") { if (parts[3].EndsWith(":")) calls++; else repeats++; }
                if (parts.Length < 5 || parts[2] != "SAY") continue;
                spoken++;
                string occ = parts[3], bucket = parts[4];
                byOcc[occ] = byOcc.TryGetValue(occ, out int a) ? a + 1 : 1;
                byBucket[bucket] = byBucket.TryGetValue(bucket, out int b) ? b + 1 : 1;
            }
            sb.AppendLine($"MechSpeech: {spoken} lines said this match");
            float minutes = Mathf.Max(1f, last - Mathf.Max(0f, first)) / 60f;
            sb.AppendLine($"  {calls} calls shown, {repeats} repeats kept off the comms, {spoken} flavour: " +
                          $"{(calls + spoken) / minutes:0.0} lines a minute, flavour every {(spoken > 0 ? minutes / spoken : 0f):0.0} min, over {minutes:0.0} min");
            foreach (var kv in byOcc) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            foreach (var kv in byBucket) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            foreach (var j in Journal) sb.AppendLine(j);
            return sb.ToString();
        }

        /// <summary>The table's own check: every line there once, filled in, and sayable in
        /// some reading. <c>Tools/editor.sh call StarForge.AI.MechSpeech.Check</c>.</summary>
        public static string Check()
        {
            var sb = new StringBuilder();
            int problems = 0;
            var numbers = new HashSet<int>();
            var texts = new HashSet<string>();
            var dummy = new MechDesign { pilot = "Pilot", callsign = "Callsign", order = "the Order" };   // as designs spell it
            foreach (var l in Lines)
            {
                if (!numbers.Add(l.n)) { problems++; sb.AppendLine($"  #{l.n} twice"); }
                if (!texts.Add(l.text)) { problems++; sb.AppendLine($"  #{l.n} text repeated"); }
                string f = Fill(l.text, dummy);
                if (f.IndexOf('{') >= 0 || f.IndexOf('}') >= 0) { problems++; sb.AppendLine($"  #{l.n} unfilled: {f}"); }
                if (f.StartsWith("the Order") || f.Contains(". the Order") || f.Contains("! the Order") || f.Contains("? the Order"))
                { problems++; sb.AppendLine($"  #{l.n} starts a sentence with the order's name in lower case (use {{Order}}): {f}"); }
                if (l.weight <= 0f) { problems++; sb.AppendLine($"  #{l.n} has no weight"); }
            }
            foreach (int n in ExpectedNumbers())
                if (!numbers.Contains(n)) { problems++; sb.AppendLine($"  #{n} missing"); }
            if (Lines.Length != LineCount) { problems++; sb.AppendLine($"  {Lines.Length} lines, {LineCount} expected"); }
            problems += CheckCounsel(sb, numbers, texts);

            // An order's line is said only by a Mech of that order: its name must be one the
            // designs draw (a misspelt one would never be said, with no error), and a bucket
            // must hold a line for every Mech.
            var drawn = new SortedDictionary<string, int>();
            for (uint s = 1; s <= 400; s++)
            {
                string o = MechParts.Generate(s * 2654435761u).order;
                drawn[o] = drawn.TryGetValue(o, out int c0) ? c0 + 1 : 1;
            }
            var own = new SortedDictionary<string, int>();
            foreach (var l in Lines)
            {
                if (l.order == null) continue;
                own[l.order] = own.TryGetValue(l.order, out int c1) ? c1 + 1 : 1;
                if (!drawn.ContainsKey(l.order)) { problems++; sb.AppendLine($"  #{l.n} is for '{l.order}', which no design draws"); }
            }
            foreach (var o in drawn.Keys)
                if (!own.ContainsKey(o)) { problems++; sb.AppendLine($"  '{o}' has no line of its own"); }
            for (int b = 0; b < BucketCount; b++)
            {
                if (ByBucket[b].Length == 0) continue;
                bool general = false;
                foreach (int k in ByBucket[b]) if (Lines[k].order == null) { general = true; break; }
                if (!general) { problems++; sb.AppendLine($"  bucket {b} holds only one order's lines: other Mechs have none there"); }
            }
            // The choice a match makes, run for a Mech of each order (and one with no design)
            // through every bucket and past its end: never another order's line, each of its
            // own once, and never empty-handed where the bucket has lines.
            var orders = new List<string>(drawn.Keys) { null };
            var pick = new Rng(0x10AEu);
            foreach (string o in orders)
            {
                var d = o == null ? null : new MechDesign { pilot = "Pilot", callsign = "Callsign", order = o };
                var saidC = new bool[Lines.Length];
                var atC = new float[Lines.Length];
                int foreign = 0, ownSaid = 0, none = 0, twice = 0, stale = 0;
                float clock = 0f;
                for (int b = 0; b < BucketCount; b++)
                {
                    int n = ByBucket[b].Length, mine = 0;
                    foreach (int k in ByBucket[b]) if (Lines[k].For(d)) mine++;
                    for (int i = 0; i < n + 3; i++)
                    {
                        // Say()'s freshness test: fresh exactly while one of its own is unsaid.
                        bool fresh = Fresh(ByBucket[b], d, saidC, out bool anyC);
                        if (n > 0 && (fresh != (i < mine) || anyC != (mine > 0))) stale++;
                        int k = Choose(ByBucket[b], d, Cue.None, saidC, atC, pick);
                        if (k < 0) { if (n > 0) none++; continue; }
                        if (Lines[k].order != null && Lines[k].order != o) foreign++;   // not by For(): it is what is being checked
                        if (saidC[k] && i < mine) twice++;          // repeated while it still had fresh ones
                        if (Lines[k].order != null && !saidC[k]) ownSaid++;
                        saidC[k] = true;
                        atC[k] = clock += 1f;
                    }
                }
                int owned = o != null && own.TryGetValue(o, out int c2) ? c2 : 0;
                if (foreign > 0 || none > 0 || twice > 0 || stale > 0 || ownSaid != owned)
                {
                    problems++;
                    sb.AppendLine($"  choice for '{o ?? "no design"}': {foreign} of another order, {none} empty-handed, {twice} repeated early, {stale} wrong freshness, {ownSaid} of its own {owned}");
                }
            }

            // Walk every reading and collect the buckets that can be drawn from.
            var reach = new HashSet<int>();
            var into = new List<(int, float)>();
            var occasions = (Occasion[])System.Enum.GetValues(typeof(Occasion));
            float[] tempers = { -1f, 0f, 1f }, prides = { 0f, 1f };
            foreach (var o in occasions)
                for (int st = -1; st <= 1; st++)
                    for (int bits = 0; bits < 64; bits++)
                        foreach (float te in tempers)
                            foreach (float pr in prides)
                            {
                                var r = new Reading
                                {
                                    occasion = o, standing = st, temper = te, pride = pr,
                                    calm = (bits & 1) != 0, attack = (bits & 2) != 0, good = (bits & 4) != 0,
                                    home = (bits & 8) != 0, decisive = (bits & 16) != 0, costly = (bits & 32) != 0,
                                    longLosing = (bits & 1) != 0
                                };
                                Fits(r, into);
                                foreach (var (b, f) in into) if (f > 0f) reach.Add(b);
                            }
            // Say() itself, for a Mech of each order (and one with no design), over every reading
            // twice: never a line of another order, never empty-handed, every text one of the table's,
            // filled in and beginning a sentence, and every line that Mech may say said at least once.
            var sayLines = new StringBuilder();
            foreach (string o in orders)
            {
                var d = o == null ? null : new MechDesign { pilot = "Pilot", callsign = "Callsign", order = o };
                var byText = new Dictionary<string, int>(Lines.Length);
                for (int i = 0; i < Lines.Length; i++) byText[Fill(Lines[i].text, d)] = i;
                var voice = new MechSpeech(0x5A1Du, 0f, 0.5f);
                var heard = new bool[Lines.Length];
                int foreign = 0, unknown = 0, mute = 0, lower = 0, calls = 0;
                for (int round = 0; round < 2; round++)
                    foreach (var oc in occasions)
                    {
                        if (oc == Occasion.None) continue;
                        for (int st = -1; st <= 1; st++)
                            for (int bits = 0; bits < 64; bits++)
                                foreach (float te in tempers)
                                    foreach (float pr in prides)
                                    {
                                        var r = new Reading
                                        {
                                            occasion = oc, standing = st, temper = te, pride = pr,
                                            calm = (bits & 1) != 0, attack = (bits & 2) != 0, good = (bits & 4) != 0,
                                            home = (bits & 8) != 0, decisive = (bits & 16) != 0, costly = (bits & 32) != 0,
                                            longLosing = (bits & 1) != 0, cues = (Cue)bits
                                        };
                                        string text = voice.Say(r, d);
                                        voice.checkClock += 1f;
                                        calls++;
                                        if (text == null) { mute++; continue; }
                                        if (text.Length == 0 || char.IsLower(text[0])) lower++;
                                        if (!byText.TryGetValue(text, out int k)) { unknown++; continue; }
                                        if (Lines[k].order != null && Lines[k].order != o) foreign++;
                                        heard[k] = true;
                                    }
                    }
                int its = 0, itsSaid = 0;
                for (int i = 0; i < Lines.Length; i++)
                    if (Lines[i].order == null || Lines[i].order == o) { its++; if (heard[i]) itsSaid++; }
                sayLines.AppendLine($"  Say() as {o ?? "no design"}: {itsSaid} of its {its} lines in {calls} calls");
                if (foreign > 0 || unknown > 0 || mute > 0 || lower > 0 || itsSaid != its)
                {
                    problems++;
                    sb.AppendLine($"  Say() as '{o ?? "no design"}': {foreign} of another order, {unknown} not in the table, {mute} empty-handed, {lower} not starting a sentence, {itsSaid} of its {its} said");
                }
            }

            var counts = new SortedDictionary<string, int>();
            foreach (var l in Lines)
            {
                string key = $"{l.moment}/{l.tone}";
                counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                if (!reach.Contains(l.Bucket)) { problems++; sb.AppendLine($"  #{l.n} ({key}) can never be said"); }
            }
            var head = new StringBuilder();
            head.AppendLine($"MechSpeech: {Lines.Length} lines and {CounselLines.Length} counsel lines (#{CounselFirst}-#{CounselFirst + CounselLines.Length - 1}), {counts.Count} buckets, {problems} problem(s)");
            foreach (var kv in counts) head.AppendLine($"  {kv.Key}: {kv.Value}");
            foreach (var kv in own) head.AppendLine($"  {kv.Key}: {kv.Value} of its own");
            head.Append(sayLines);
            return head.ToString() + sb;
        }
    }
}
