using static LanguageLab.Domain.Grammar.GrammarLevel;

namespace LanguageLab.Domain.Grammar;

/// <summary>
/// One topic of the syllabus, written or not. <see cref="Key"/> is the topic's stable key — a
/// written topic has a <see cref="GrammarTopic"/> with the same key in <see cref="GrammarCatalog"/>.
/// </summary>
public sealed record GrammarSyllabusEntry(string Key, string Section, GrammarLevel Level, string Title);

/// <summary>
/// Every grammar topic from A1 to B2, written or planned, in learning order and grouped by section
/// — the source of truth for each topic's order, section, level and title. Sections follow grammar
/// areas, not levels, so one section can run from A1 to B2. Content lives in
/// <see cref="GrammarCatalog"/>; a topic it lacks is shown as planned.
/// </summary>
public static class GrammarSyllabus
{
    public const string SentenceBasics = "Sentence basics";
    public const string PresentTenses = "Present tenses";
    public const string PastTenses = "Past tenses";
    public const string PresentPerfect = "Present perfect";
    public const string Future = "Future";
    public const string ModalVerbs = "Modal verbs";
    public const string Passive = "Passive";
    public const string Conditionals = "Conditionals";
    public const string ReportedSpeech = "Reported speech";
    public const string VerbPatterns = "Verb patterns";
    public const string NounsArticlesDeterminers = "Nouns, articles, determiners";
    public const string Pronouns = "Pronouns";
    public const string AdjectivesAdverbs = "Adjectives and adverbs";
    public const string Prepositions = "Prepositions";
    public const string ClausesLinking = "Clauses and linking";

    public static IReadOnlyList<GrammarSyllabusEntry> Entries { get; } =
    [
        new("be", SentenceBasics, A1, "be: am, is, are"),
        new("there-is-are", SentenceBasics, A1, "there is, there are"),
        new("have-have-got", SentenceBasics, A1, "have, have got"),
        new("word-order", SentenceBasics, A1, "word order in statements"),
        new("questions-short-answers", SentenceBasics, A1, "questions and short answers"),

        new("present-simple", PresentTenses, A1, "present simple"),
        new("present-continuous", PresentTenses, A1, "present continuous"),
        new("present-simple-vs-continuous", PresentTenses, A2, "present simple or continuous"),
        new("state-verbs", PresentTenses, A2, "state verbs"),

        new("past-simple", PastTenses, A1, "past simple"),
        new("past-continuous", PastTenses, A2, "past continuous"),
        new("past-simple-vs-continuous", PastTenses, A2, "past simple or continuous"),
        new("used-to", PastTenses, A2, "used to"),
        new("past-perfect", PastTenses, B1, "past perfect"),
        new("past-perfect-continuous", PastTenses, B2, "past perfect continuous"),
        new("would-past-habits", PastTenses, B2, "would for past habits"),

        new("present-perfect", PresentPerfect, A2, "present perfect: experience and result"),
        new("for-since-already-yet-just", PresentPerfect, A2, "for, since, already, yet, just"),
        new("present-perfect-vs-past-simple", PresentPerfect, B1, "present perfect or past simple"),
        new("present-perfect-continuous", PresentPerfect, B1, "present perfect continuous"),

        new("will", Future, A2, "will"),
        new("going-to", Future, A2, "going to"),
        new("present-continuous-future", Future, A2, "present continuous for arrangements"),
        new("will-vs-going-to", Future, B1, "will or going to"),
        new("time-clauses", Future, B1, "when, as soon as + present"),
        new("future-continuous", Future, B2, "future continuous"),
        new("future-perfect", Future, B2, "future perfect"),

        new("can-could", ModalVerbs, A1, "can, could: ability"),
        new("permission-requests", ModalVerbs, A2, "asking for permission, making requests"),
        new("must-have-to", ModalVerbs, A2, "must, have to"),
        new("mustnt-dont-have-to", ModalVerbs, A2, "mustn't or don't have to"),
        new("should", ModalVerbs, A2, "should"),
        new("may-might", ModalVerbs, B1, "may, might: possibility"),
        new("deduction", ModalVerbs, B2, "must, can't, might: deduction"),
        new("modal-perfects", ModalVerbs, B2, "should have, might have"),
        new("need-neednt", ModalVerbs, B2, "need, needn't"),

        new("passive-present-past", Passive, B1, "present and past simple passive"),
        new("passive-other-tenses", Passive, B1, "passive in other tenses"),
        new("passive-modals", Passive, B2, "passive with modals"),
        new("have-something-done", Passive, B2, "have something done"),
        new("it-is-said", Passive, B2, "it is said that, be said to"),

        new("zero-first-conditional", Conditionals, A2, "zero and first conditional"),
        new("second-conditional", Conditionals, B1, "second conditional"),
        new("third-conditional", Conditionals, B1, "third conditional"),
        new("unless", Conditionals, B1, "unless"),
        new("mixed-conditionals", Conditionals, B2, "mixed conditionals"),
        new("wish-if-only", Conditionals, B2, "wish, if only"),
        new("in-case", Conditionals, B2, "in case"),

        new("reported-statements", ReportedSpeech, B1, "reported statements"),
        new("reported-questions", ReportedSpeech, B1, "reported questions"),
        new("reported-orders-requests", ReportedSpeech, B2, "reported orders and requests"),
        new("reporting-verbs", ReportedSpeech, B2, "reporting verbs"),

        new("ing-vs-to-infinitive", VerbPatterns, A2, "-ing or to-infinitive"),
        new("verb-object-infinitive", VerbPatterns, B1, "verb + object + infinitive"),
        new("make-let-help", VerbPatterns, B1, "make, let, help"),
        new("remember-stop-try", VerbPatterns, B2, "remember, stop, try"),

        new("plurals", NounsArticlesDeterminers, A1, "plurals"),
        new("countable-uncountable", NounsArticlesDeterminers, A1, "countable and uncountable nouns"),
        new("a-an-the", NounsArticlesDeterminers, A1, "a, an or the"),
        new("some-any-no", NounsArticlesDeterminers, A1, "some, any, no"),
        new("possessive-s-of", NounsArticlesDeterminers, A1, "possessive 's and of"),
        new("zero-article", NounsArticlesDeterminers, A2, "no article"),
        new("much-many-few-little", NounsArticlesDeterminers, A2, "much, many, a lot of, (a) few, (a) little"),
        new("all-both-either-each", NounsArticlesDeterminers, B1, "all, both, either, neither, each, every"),

        new("personal-pronouns", Pronouns, A1, "I, me, my, mine"),
        new("reflexive-pronouns", Pronouns, A2, "reflexive pronouns"),
        new("indefinite-pronouns", Pronouns, A2, "something, anyone and the rest"),
        new("one-ones", Pronouns, B1, "one, ones"),

        new("comparatives-superlatives", AdjectivesAdverbs, A2, "comparatives and superlatives"),
        new("adverbs-of-manner", AdjectivesAdverbs, A2, "adverbs of manner"),
        new("ed-ing-adjectives", AdjectivesAdverbs, A2, "-ed and -ing adjectives"),
        new("as-as-too-enough", AdjectivesAdverbs, A2, "as … as, too, enough"),
        new("adjective-order", AdjectivesAdverbs, B1, "adjective order"),
        new("so-such", AdjectivesAdverbs, B1, "so, such"),
        new("adverb-position", AdjectivesAdverbs, B1, "adverb position"),

        new("prepositions-time", Prepositions, A1, "at, on, in: time"),
        new("prepositions-place", Prepositions, A1, "at, on, in: place"),
        new("prepositions-movement", Prepositions, A2, "prepositions of movement"),
        new("dependent-prepositions", Prepositions, B1, "dependent prepositions"),

        new("purpose-reason", ClausesLinking, A2, "to, so that, because, so"),
        new("contrast", ClausesLinking, B1, "but, although, however"),
        new("relative-clauses", ClausesLinking, B1, "defining relative clauses"),
        new("question-tags", ClausesLinking, B1, "question tags"),
        new("indirect-questions", ClausesLinking, B1, "indirect questions"),
        new("non-defining-relative-clauses", ClausesLinking, B2, "non-defining relative clauses"),
        new("despite-in-spite-of", ClausesLinking, B2, "despite, in spite of"),
    ];
}
