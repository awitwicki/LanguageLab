namespace LanguageLab.Application.Seeding;

/// <summary>
/// One irregular verb: the three forms and the Ukrainian translation. Group is
/// the 1-based pattern type from the learner's table (1 — all forms alike … 4 — all differ).
/// </summary>
public sealed record IrregularVerb(int Group, string V1, string V2, string V3, string Translation)
{
    /// <summary>
    /// What the learner sees as the "word": all three forms at once. The triplet is
    /// deliberately not the bare infinitive, so the verb gets its own WordPair with its
    /// own shelf and Leitner progress instead of sharing them with the same word from a book.
    /// </summary>
    public string Word => $"{V1} – {V2} – {V3}";
}

/// <summary>
/// The irregular-verbs table, grouped by pattern rather than alphabetically — that is how
/// the groups are meant to be learned, and the order within a group keeps the mini-families
/// (bought / thought / caught, begin / drink / swim, know / grow / throw) next to each other.
/// The seeder turns table position into frequency, so batches follow this order.
/// </summary>
public static class IrregularVerbs
{
    public const string DictionaryName = "Irregular verbs";

    /// <summary>Chapter titles, indexed by Group - 1.</summary>
    public static readonly IReadOnlyList<string> GroupTitles =
    [
        "All three forms alike · cut – cut – cut",
        "First and third alike · come – came – come",
        "Second and third alike · buy – bought – bought",
        "All three forms differ · begin – began – begun",
    ];

    public static readonly IReadOnlyList<IrregularVerb> All =
    [
        // Type 1 — V1 = V2 = V3
        new(1, "cut", "cut", "cut", "різати"),
        new(1, "put", "put", "put", "класти"),
        new(1, "let", "let", "let", "дозволяти"),
        new(1, "set", "set", "set", "встановлювати"),
        new(1, "hit", "hit", "hit", "вдаряти"),
        new(1, "shut", "shut", "shut", "зачиняти"),
        new(1, "cost", "cost", "cost", "коштувати"),
        new(1, "hurt", "hurt", "hurt", "боліти / ранити"),
        new(1, "read", "read", "read", "читати"),

        // Type 2 — V1 = V3
        new(2, "come", "came", "come", "приходити"),
        new(2, "become", "became", "become", "ставати"),
        new(2, "run", "ran", "run", "бігти"),

        // Type 3 — V2 = V3
        new(3, "buy", "bought", "bought", "купувати"),
        new(3, "bring", "brought", "brought", "приносити"),
        new(3, "think", "thought", "thought", "думати"),
        new(3, "catch", "caught", "caught", "ловити"),
        new(3, "teach", "taught", "taught", "навчати"),
        new(3, "sleep", "slept", "slept", "спати"),
        new(3, "keep", "kept", "kept", "тримати"),
        new(3, "feel", "felt", "felt", "відчувати"),
        new(3, "leave", "left", "left", "залишати"),
        new(3, "lose", "lost", "lost", "втрачати"),
        new(3, "mean", "meant", "meant", "означати"),
        new(3, "meet", "met", "met", "зустрічати"),
        new(3, "sit", "sat", "sat", "сидіти"),
        new(3, "spend", "spent", "spent", "витрачати"),
        new(3, "send", "sent", "sent", "надсилати"),
        new(3, "sell", "sold", "sold", "продавати"),
        new(3, "tell", "told", "told", "розповідати"),
        new(3, "hold", "held", "held", "тримати"),
        new(3, "find", "found", "found", "знаходити"),
        new(3, "have", "had", "had", "мати"),
        new(3, "make", "made", "made", "робити"),
        new(3, "say", "said", "said", "казати"),
        new(3, "pay", "paid", "paid", "платити"),
        new(3, "hear", "heard", "heard", "чути"),
        new(3, "stand", "stood", "stood", "стояти"),
        new(3, "understand", "understood", "understood", "розуміти"),
        new(3, "win", "won", "won", "вигравати"),
        new(3, "build", "built", "built", "будувати"),
        new(3, "get", "got", "got", "отримувати"),

        // Type 4 — all three forms differ
        new(4, "be", "was/were", "been", "бути"),
        new(4, "do", "did", "done", "робити"),
        new(4, "go", "went", "gone", "йти"),
        new(4, "see", "saw", "seen", "бачити"),
        new(4, "eat", "ate", "eaten", "їсти"),
        new(4, "give", "gave", "given", "давати"),
        new(4, "take", "took", "taken", "брати"),
        new(4, "write", "wrote", "written", "писати"),
        new(4, "speak", "spoke", "spoken", "говорити"),
        new(4, "break", "broke", "broken", "ламати"),
        new(4, "choose", "chose", "chosen", "вибирати"),
        new(4, "drive", "drove", "driven", "керувати (авто)"),
        new(4, "ride", "rode", "ridden", "їздити верхи"),
        new(4, "wear", "wore", "worn", "носити (одяг)"),
        new(4, "fall", "fell", "fallen", "падати"),
        new(4, "forget", "forgot", "forgotten", "забувати"),
        new(4, "begin", "began", "begun", "починати"),
        new(4, "drink", "drank", "drunk", "пити"),
        new(4, "swim", "swam", "swum", "плавати"),
        new(4, "ring", "rang", "rung", "дзвонити"),
        new(4, "sing", "sang", "sung", "співати"),
        new(4, "know", "knew", "known", "знати"),
        new(4, "grow", "grew", "grown", "рости"),
        new(4, "throw", "threw", "thrown", "кидати"),
        new(4, "blow", "blew", "blown", "дути"),
        new(4, "fly", "flew", "flown", "літати"),
        new(4, "draw", "drew", "drawn", "малювати"),
    ];
}
