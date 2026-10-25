using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace JJKMod
{
    // Per-domain data attached to each Domain Expansion AbilityDef via
    // <modExtensions>. One reusable DE ability class (Ability_DomainExpansion)
    // reads this to know how the technique's domain looks and behaves, so every
    // cursed technique gets a domain purely from XML.
    //
    // The initial cursed-energy cost to open the domain is NOT here - it stays on
    // the AbilityTierDef.energyCost (paid by the standard cast). This carries the
    // ongoing behaviour: upkeep drain, the sure-hit pulse, visuals, and burnout.
    public class DomainExtension : DefModExtension
    {
        public bool gambling;
        // Barrier / interior tint.
        public Color barrierColor = new Color(0.5f, 0.3f, 0.8f);

        // The signature "sure-hit" pulse. Every pulseIntervalTicks, every hostile
        // pawn inside is struck - it cannot miss. A pulse can deal damage
        // (pulseDamage/pulseAmount) and/or stun (pulseStun). Some domains (e.g.
        // Unlimited Void) only stun: they lock everyone inside in place rather than
        // grinding them down.
        public DamageDef pulseDamage;
        public float pulseAmount = 8f;
        public int pulseIntervalTicks = 90;

        // Stun pulse: while inside, hostiles are re-stunned every interval, keeping
        // them frozen for as long as the domain holds them. Set pulseIntervalTicks
        // below pulseStunTicks so the stun never lapses.
        public bool pulseStun = false;
        public int pulseStunTicks = 150;

        // Soul-hit: the pulse strikes the soul, not the body - armor is ignored
        // entirely (Self-Embodiment of Perfection).
        public bool pulseIgnoresArmor = false;

        // The pulse only strikes pawns that are MOVING when it fires (Time Cell
        // Moon Palace: anything that moves inside the domain is cut). Standing
        // still is safe - but standing still means not escaping.
        public bool pulseOnlyMoving = false;

        // When a stun domain collapses, everyone caught inside keeps a residual
        // stun that scales with how long the domain was held (Unlimited Void: the
        // longer you trap them in infinite information, the longer they reel after).
        // The linger equals the domain's age in ticks, clamped to this maximum.
        public int maxLingerStunTicks = 5000;

        // Domain summons: temporary shikigami the domain manifests to hunt down
        // hostiles inside it. They are spawned as the caster's allies and destroyed
        // (returned to the shadows) when the domain collapses.
        //  - summonKinds: explicit pawnkinds to conjure (e.g. fish for Disaster
        //    Tides), summonCountEach of each.
        //  - summonTamedShadows: Ten Shadows only - manifest every shikigami the
        //    caster has already tamed (Chimera Shadow Garden).
        public List<PawnKindDef> summonKinds;
        public int summonCountEach = 1;
        public bool summonTamedShadows = false;

        // 15-tile radius barrier (Section 20.3).
        public float radius = 15f;

        // A closed domain seals itself: a real, impassable barrier (blocking
        // bodies, projectiles and line of sight) is raised around the interior,
        // molding around any structures already present. Open domains (and the
        // small Simple Domain) set this false and stay passable.
        public bool closed = true;

        // Simple Domain: a small, open, purely defensive domain. It deals no
        // damage of its own; instead, the caster and any of their allies standing
        // within its radius are immune to the sure-hit of a hostile Domain
        // Expansion they are caught inside (the canonical neutralizing counter).
        // It also skips the post-domain burnout - it is a basic defensive skill.
        public bool isSimpleDomain = false;

        // Which "in-domain" hediff the caster carries while this domain is open.
        // Null defaults to JJK_DomainActive (the +30% offensive buff). Simple
        // Domain points this at JJK_SimpleDomainActive (a light defensive buff).
        public HediffDef activeHediff = null;

        // Simple Domain durability. A Simple Domain caught inside a hostile Domain
        // Expansion is worn down: every enemy sure-hit interval strips this much
        // integrity, and when it reaches zero the Simple Domain shatters (so a
        // strong domain like Malevolent Shrine eventually breaks through it).
        public float simpleDomainIntegrity = 120f;

        // How hard THIS (offensive) domain's sure-hit grinds against an enemy
        // Simple Domain overlapping it, per pulse interval. 0 = no wear (e.g. a
        // Simple Domain itself). Stronger domains set this higher.
        public float sureHitStrain = 0f;

        // Cursed energy bled per tick while the domain is held open. Before the
        // caster's efficiency (usage) multiplier is applied.
        public float upkeepPerTick = 0.04f;

        // Post-domain burnout (Section 20.5): the lock is longer if the domain ran
        // its course / collapsed than if the caster willingly dismissed it.
        public int burnoutTicksInvoluntary = 60000; // 24 in-game hours
        public int burnoutTicksVoluntary = 15000;   // 6 in-game hours

        // The domain refuses to open below this Max Cursed Energy (Section 7.2 /
        // 20.2 - Deep Reserves at Grade 1 reach ~232, comfortably above 160).
        public float minMaxCE = 160f;

        // Mastery cast-gate: accumulated mastery XP (in this domain's technique)
        // the caster must have before they can open the full Domain Expansion via
        // the ability. Grade unlocks it; practice earns it. 0 = no mastery gate.
        // Enforced only on the player-facing cast; the enemy AI is not gated.
        public float masteryToOpen = 1000f;
    }
}
