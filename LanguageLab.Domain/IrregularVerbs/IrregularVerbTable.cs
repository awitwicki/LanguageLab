namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// The irregular-verbs table, grouped by pattern rather than alphabetically — that is how
/// the groups are meant to be learned, and the order within a step keeps the mini-families
/// (bought / thought / caught, begin / drink / swim, know / grow / throw) next to each other.
/// The session planner uses this order for verbs it has never seen the user answer.
/// </summary>
public static class IrregularVerbTable
{
    public static readonly IReadOnlyList<string> StepTitles =
    [
        "All three forms alike",
        "First and third alike",
        "Second and third alike",
        "All three forms differ",
    ];

    public static readonly IReadOnlyList<IrregularVerb> All =
    [
        // Step 1 — V1 = V2 = V3
        new(1, "cut", "cut", "cut", "різати"),
        new(1, "put", "put", "put", "класти"),
        new(1, "let", "let", "let", "дозволяти"),
        new(1, "set", "set", "set", "встановлювати"),
        new(1, "hit", "hit", "hit", "вдаряти"),
        new(1, "shut", "shut", "shut", "зачиняти"),
        new(1, "cost", "cost", "cost", "коштувати"),
        new(1, "hurt", "hurt", "hurt", "боліти / ранити"),
        new(1, "read", "read", "read", "читати"),

        // Step 2 — V1 = V3
        new(2, "come", "came", "come", "приходити"),
        new(2, "become", "became", "become", "ставати"),
        new(2, "run", "ran", "run", "бігти"),

        // Step 3 — V2 = V3
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

        // Step 4 — all three forms differ
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

    public static readonly IReadOnlyDictionary<int, IReadOnlyList<IrregularVerb>> Steps =
        All.GroupBy(v => v.Step)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IrregularVerb>)g.ToList());

    private static readonly IReadOnlyDictionary<string, IrregularVerb> ByV1 =
        All.ToDictionary(v => v.V1, StringComparer.Ordinal);

    public static string TitleOf(int step) => StepTitles[step - 1];

    public static IrregularVerb? Find(string v1) => ByV1.GetValueOrDefault(v1);
}
