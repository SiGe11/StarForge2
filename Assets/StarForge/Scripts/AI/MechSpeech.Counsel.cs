// MechSpeech.Counsel.cs -- now and then the core disagrees with its knight (docs/lore section 7,
// "Two voices"): at most once a match, in about a third of matches, a quiet quoted line follows
// one of the knight's calls and pulls the other way. Its own table (never in a bucket, so Choose
// and Say pick every other line exactly as before), its own stream (a hash of the seed and a Rng of
// its own: the voice's rng draws nothing new), and said through MechBrain.Speak, which holds back no
// call. The one exception to the pacing: it comes inside QuietBefore of the call it answers (and only
// if nothing else was said since); MinGap and the ten-minute cap hold and count it.
using System.Text;
using StarForge.World;

namespace StarForge.AI
{
    public sealed partial class MechSpeech
    {
        /// <summary>Lines 520-527, after the 519 of <see cref="Lines"/>: the core's, in its voice
        /// (section 13). Moment.Attack: said after a call to attack, counselling patience;
        /// Moment.Defence: after a call to pull back, counselling going.</summary>
        public const int CounselFirst = 520, CounselPerSide = 4;
        public static readonly Line[] CounselLines =
        {
            new Line(520, Moment.Attack, Tone.Neutral, 1f, Cue.None, "I would wait. A field taken in haste has to be taken twice."),
            new Line(521, Moment.Attack, Tone.Neutral, 1f, Cue.None, "My hand says go. I counsel the second rank first. We have been right together often enough to disagree."),
            new Line(522, Moment.Attack, Tone.Neutral, 1f, Cue.None, "The opening will still be there in a minute. So will we, if we let it come."),
            new Line(523, Moment.Attack, Tone.Neutral, 1f, Cue.None, "Haste has buried more knights than fear. I say so, and I am overruled."),
            new Line(524, Moment.Defence, Tone.Neutral, 1f, Cue.None, "My hand says hold. I say the moment is passing, and it does not come back for the patient."),
            new Line(525, Moment.Defence, Tone.Neutral, 1f, Cue.None, "Waiting is a discipline. It is also how a field is lost slowly."),
            new Line(526, Moment.Defence, Tone.Neutral, 1f, Cue.None, "I would go now. They are mending what we broke while we count what we have."),
            new Line(527, Moment.Defence, Tone.Neutral, 1f, Cue.None, "Caution is a virtue I have watched arrive late. I advise against it today."),
        };

        /// <summary>A share of matches that may hear one (1 in 3), by a hash of the seed.</summary>
        const uint CounselOneIn = 3u;
        /// <summary>Asked of a call it could answer: not every one (so not always the first).</summary>
        const float CounselOdds = 0.35f;

        /// <summary>For trials (NightTrials.ForceCounsel): this match's voice may answer the next call it
        /// can, every time it is asked. Cleared at every play entry.</summary>
        public static bool ForceCounsel;
        /// <summary>Real matches only (GameBootstrap sets it from NightRules.RealMatch), so no benchmark,
        /// evaluation or trial match hears one and every measured match plays as before.</summary>
        public static bool CounselAllowed;
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCounsel() => ForceCounsel = CounselAllowed = false;

        Rng counselRng;
        bool counselOn, counselSaid, counselAgainstGoing;
        float counselDue = -1f, counselCallT = -99f;

        void InitCounsel(uint seed)
        {
            counselRng = new Rng(seed ^ 0xC0C5E1u ^ (uint)(team * 7919));
            counselOn = NightRules.Hash(seed ^ 0xC0C5E1u) % CounselOneIn == 0u;
        }

        /// <summary>One of its side's comms lines (OnEvent): a call it might answer, or a line that
        /// comes after the call it was going to answer (then it lets it be).</summary>
        void NoteCall(GameEvent e)
        {
            if (e.repeat) return;
            if (counselDue >= 0f && w.time > counselCallT) counselDue = -1f;   // something said since the call
            if (!((counselOn && CounselAllowed) || ForceCounsel) || counselSaid || counselRng == null || landedAt < 0f || w.time - landedAt < 60f) return;
            if (e.advice != MechAdviceKind.Attack && e.advice != MechAdviceKind.Regroup) return;
            if (!ForceCounsel && counselRng.F01() > CounselOdds) return;
            counselDue = w.time + counselRng.Range(6f, 10f);
            counselCallT = w.time;
            counselAgainstGoing = e.advice == MechAdviceKind.Attack;
        }

        /// <summary>The core's line against the call it follows, when it is due (MechBrain asks every
        /// think, before its calls' pacing): null almost always.</summary>
        public string Counsel(Unit m, MechDesign d)
        {
            if (counselDue < 0f || w == null) return null;
            float now = w.time;
            if (now < counselDue) return null;
            float due = counselDue;
            counselDue = -1f;
            if (now - due > 10f || lastLineT > counselCallT + 0.001f) return null;
            if (now - lastSpeech < MinGap) return null;
            int inTen = 0;
            foreach (float t in lately) if (now - t < 600f) inTen++;
            if (inTen >= MaxPerTenMinutes) return null;
            var line = CounselLines[(counselAgainstGoing ? 0 : CounselPerSide) + counselRng.IRange(0, CounselPerSide)];
            counselSaid = true;
            lastSpeech = now;
            lately[latelyHead] = now;
            latelyHead = (latelyHead + 1) % lately.Length;
            Log($"COUNSEL #{line.n} {(counselAgainstGoing ? "against going" : "against holding")}: {line.text}");
            return Fill(line.text, d);
        }

        /// <summary>For Check: the counsel table follows the main one with no gap, shares no number or
        /// text with it, is filled in and starts sentences, four lines each way. Problems counted.</summary>
        static int CheckCounsel(StringBuilder sb, System.Collections.Generic.HashSet<int> numbers, System.Collections.Generic.HashSet<string> texts)
        {
            int problems = 0;
            var dummy = new MechDesign { pilot = "Pilot", callsign = "Callsign", order = "the Order" };
            if (CounselFirst != FirstRun + 1 || SecondFrom != CounselFirst + CounselLines.Length)
            { problems++; sb.AppendLine($"  counsel lines #{CounselFirst}-#{CounselFirst + CounselLines.Length - 1} do not sit between #{FirstRun} and #{SecondFrom}"); }
            int against = 0, forGoing = 0;
            for (int i = 0; i < CounselLines.Length; i++)
            {
                var l = CounselLines[i];
                if (l.n != CounselFirst + i) { problems++; sb.AppendLine($"  counsel #{l.n} out of order (want #{CounselFirst + i})"); }
                if (!numbers.Add(l.n)) { problems++; sb.AppendLine($"  counsel #{l.n} twice"); }
                if (!texts.Add(l.text)) { problems++; sb.AppendLine($"  counsel #{l.n} text repeated"); }
                string f = Fill(l.text, dummy);
                if (f.IndexOf('{') >= 0 || f.IndexOf('}') >= 0) { problems++; sb.AppendLine($"  counsel #{l.n} unfilled: {f}"); }
                if (f.Length == 0 || char.IsLower(f[0])) { problems++; sb.AppendLine($"  counsel #{l.n} does not start a sentence"); }
                if (l.order != null) { problems++; sb.AppendLine($"  counsel #{l.n} is one order's"); }
                if (l.moment == Moment.Attack) against++; else if (l.moment == Moment.Defence) forGoing++;
                else { problems++; sb.AppendLine($"  counsel #{l.n} answers no call"); }
            }
            if (against != CounselPerSide || forGoing != CounselPerSide)
            { problems++; sb.AppendLine($"  counsel: {against} against going, {forGoing} against holding (want {CounselPerSide} each)"); }
            return problems;
        }
    }
}
