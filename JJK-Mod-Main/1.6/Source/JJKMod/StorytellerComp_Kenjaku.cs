using System.Collections.Generic;
using RimWorld;
using Verse;

namespace JJKMod
{
    public class StorytellerCompProperties_Kenjaku : StorytellerCompProperties
    {
        public float mtbDays = 6f;
        // minDaysPassed is inherited from StorytellerCompProperties.

        public StorytellerCompProperties_Kenjaku()
        {
            compClass = typeof(StorytellerComp_Kenjaku);
        }
    }

    // Periodically fires the Kenjaku flavor incident (a third-person plotting
    // letter) on a mean-time-between basis. Flavor only, no gameplay effect.
    public class StorytellerComp_Kenjaku : StorytellerComp
    {
        private StorytellerCompProperties_Kenjaku Props => (StorytellerCompProperties_Kenjaku)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (GenDate.DaysPassedFloat < Props.minDaysPassed) yield break;
            if (Rand.MTBEventOccurs(Props.mtbDays, GenDate.TicksPerDay, 1000f))
            {
                IncidentDef def = JJK_IncidentDefOf.JJK_KenjakuFlavor;
                if (def == null) yield break;
                IncidentParms parms = GenerateParms(def.category, target);
                yield return new FiringIncident(def, this, parms);
            }
        }
    }

    [DefOf]
    public static class JJK_IncidentDefOf
    {
        public static IncidentDef JJK_KenjakuFlavor;
        public static IncidentDef JJK_CursedSpiritManifestation;
        public static StorytellerDef JJK_Kenjaku;
        public static FactionDef JJK_CurseUsers;

        static JJK_IncidentDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(JJK_IncidentDefOf));
        }
    }

    public class StorytellerCompProperties_KenjakuScheme : StorytellerCompProperties
    {
        public float mtbDays = 6.5f;
        // Chance a scheme threat is a curse-user raid; otherwise a cursed-spirit
        // manifestation. (minDaysPassed is inherited from StorytellerCompProperties.)
        public float curseUserRaidChance = 0.5f;

        public StorytellerCompProperties_KenjakuScheme()
        {
            compClass = typeof(StorytellerComp_KenjakuScheme);
        }
    }

    // Kenjaku's signature pressure: instead of generic threats, he budgets his
    // scheme toward CURSES and CURSE USERS - periodically sending a cursed-spirit
    // manifestation or a curse-user raid at the colony. Threat size is mood-scaled
    // through the same patch as every other Kenjaku threat (he strikes hardest when
    // the colony is comfortable).
    public class StorytellerComp_KenjakuScheme : StorytellerComp
    {
        private StorytellerCompProperties_KenjakuScheme Props => (StorytellerCompProperties_KenjakuScheme)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            if (!(target is Map map) || !map.IsPlayerHome) yield break;
            if (GenDate.DaysPassedFloat < Props.minDaysPassed) yield break;
            if (!Rand.MTBEventOccurs(Props.mtbDays, GenDate.TicksPerDay, 1000f)) yield break;

            Faction curseUsers = Find.FactionManager.FirstFactionOfDef(JJK_IncidentDefOf.JJK_CurseUsers);
            bool doRaid = curseUsers != null && !curseUsers.defeated
                          && Rand.Value < Props.curseUserRaidChance;

            IncidentDef def = doRaid ? IncidentDefOf.RaidEnemy : JJK_IncidentDefOf.JJK_CursedSpiritManifestation;
            if (def == null) yield break;

            IncidentParms parms = GenerateParms(def.category, target);
            if (doRaid) parms.faction = curseUsers;
            if (!def.Worker.CanFireNow(parms)) yield break;

            yield return new FiringIncident(def, this, parms);
        }
    }

    // Sends a random Kenjaku plotting letter. Always "succeeds" silently.
    public class IncidentWorker_KenjakuFlavor : IncidentWorker
    {
        private const int LetterVariants = 6;

        public override bool TryExecuteWorker(IncidentParms parms)
        {
            int idx = Rand.RangeInclusive(1, LetterVariants);
            TaggedString text = ("JJK_KenjakuLetter" + idx).Translate();
            Find.LetterStack.ReceiveLetter(
                "JJK_KenjakuLetterTitle".Translate(),
                text,
                LetterDefOf.NeutralEvent);
            return true;
        }
    }
}
