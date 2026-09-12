namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// The 68 irregular verbs of the program in learning order: four groups by pattern,
/// each group split into families that are learned one at a time. Static data —
/// verbs are never database rows. Examples are English; translations are Ukrainian.
/// </summary>
public static class IrregularVerbCatalog
{
    public static readonly IReadOnlyList<string> GroupTitles =
    [
        "All three forms alike",
        "First and third alike",
        "Second and third alike",
        "All three forms differ",
    ];

    public static readonly IReadOnlyList<VerbFamily> Families =
    [
        new("same", 1, "All three forms alike", []),
        new("back", 2, "Third form goes back to the first", []),
        new("ought", 3, "-ought / -aught", ["ought", "aught"]),
        new("ept", 3, "-ept / -elt / -eft", ["ept", "elt", "eft", "eant"]),
        new("ent", 3, "-ent", ["ent"]),
        new("old", 3, "-old / -eld", ["old", "eld"]),
        new("ood", 3, "-ood", ["ood"]),
        new("ade", 3, "-ade / -aid / -ad", ["ade", "aid", "ad"]),
        new("vowel", 3, "One vowel changes", []),
        new("i-a-u", 4, "i → a → u", []),
        new("ow-ew-own", 4, "-ow → -ew → -own", ["ew", "own", "awn"]),
        new("o-oke-oken", 4, "-o → -oke → -oken", ["oke", "oken", "ose", "osen", "ore", "orn"]),
        new("i-o-idden", 4, "-i → -o → -idden", ["ove", "ode", "ote", "iven", "idden", "itten"]),
        new("en", 4, "Past participle in -en", ["en"]),
        new("core", 4, "The three most frequent", []),
    ];

    private static IrregularVerb V(
        int group, string family, string v1, string v2, string v3, string translation,
        string present, string past, string perfect, string? note = null, params string[] confusables) =>
        new(
            group,
            family,
            v1,
            v2.Split('/'),
            v3.Split('/'),
            translation,
            [new Example(Tense.Present, present), new Example(Tense.Past, past), new Example(Tense.Perfect, perfect)],
            note,
            confusables.Length == 0 ? null : confusables);

    public static readonly IReadOnlyList<IrregularVerb> Verbs =
    [
        // Group 1 — all three forms alike
        V(1, "same", "cut", "cut", "cut", "різати",
            "I [cut] the bread every morning.", "Yesterday I [cut] my finger.", "I have [cut] the paper into strips.",
            null, "cat"),
        V(1, "same", "put", "put", "put", "класти",
            "I [put] my keys on the table every evening.", "Last night I [put] the book back on the shelf.", "I have [put] your bag in the car.",
            null, "pat"),
        V(1, "same", "let", "let", "let", "дозволяти",
            "My parents [let] me stay up late on Fridays.", "Yesterday she [let] the cat out.", "They have [let] us use their garden.",
            null, "lat"),
        V(1, "same", "set", "set", "set", "встановлювати",
            "We [set] the table before dinner.", "He [set] the alarm for six yesterday.", "I have [set] a new record.",
            null, "sat"),
        V(1, "same", "hit", "hit", "hit", "вдаряти",
            "The boys [hit] the ball over the fence every time.", "Last week a car [hit] the fence.", "She has [hit] the target three times.",
            null, "hat"),
        V(1, "same", "shut", "shut", "shut", "зачиняти",
            "Please [shut] the door when you leave.", "He [shut] the window an hour ago.", "They have [shut] the shop for the holidays.",
            null, "shot"),
        V(1, "same", "cost", "cost", "cost", "коштувати",
            "Tickets [cost] ten dollars each.", "The dinner [cost] more than we expected.", "The repairs have [cost] us a fortune.",
            null, "cast"),
        V(1, "same", "hurt", "hurt", "hurt", "боліти / ранити",
            "My feet [hurt] after a long walk.", "I [hurt] my knee last weekend.", "You have [hurt] her feelings."),
        V(1, "same", "read", "read", "read", "читати",
            "I [read] the news every morning.", "Last night I [read] two chapters.", "I have [read] this book twice.",
            "Spelt the same in all three forms, but V2 and V3 are pronounced /red/.", "red"),

        // Group 2 — first and third alike
        V(2, "back", "come", "came", "come", "приходити",
            "My friends [come] to visit every summer.", "He [came] home late yesterday.", "She has [come] to see you."),
        V(2, "back", "become", "became", "become", "ставати",
            "Children [become] taller every year.", "She [became] a doctor in 2020.", "The city has [become] very expensive."),
        V(2, "back", "run", "ran", "run", "бігти",
            "We [run] in the park every Sunday.", "He [ran] five kilometres this morning.", "I have [run] a marathon twice."),

        // Group 3 — second and third alike
        V(3, "ought", "buy", "bought", "bought", "купувати",
            "I [buy] fresh bread every day.", "She [bought] a new phone last week.", "We have [bought] a house."),
        V(3, "ought", "bring", "brought", "brought", "приносити",
            "Please [bring] your notebook to class.", "He [brought] flowers yesterday.", "They have [brought] some good news."),
        V(3, "ought", "think", "thought", "thought", "думати",
            "I [think] about it every day.", "I [thought] you were at work.", "I have [thought] about your offer."),
        V(3, "ought", "catch", "caught", "caught", "ловити",
            "We [catch] the same bus every morning.", "She [caught] a cold last week.", "The police have [caught] the thief."),
        V(3, "ought", "teach", "taught", "taught", "навчати",
            "They [teach] English at a local school.", "My mother [taught] me to cook.", "He has [taught] here for ten years."),

        V(3, "ept", "sleep", "slept", "slept", "спати",
            "Babies [sleep] most of the day.", "I [slept] badly last night.", "The cat has [slept] all afternoon."),
        V(3, "ept", "keep", "kept", "kept", "тримати",
            "I [keep] my keys in this drawer.", "She [kept] the letter for years.", "You have [kept] your promise."),
        V(3, "ept", "feel", "felt", "felt", "відчувати",
            "I [feel] happy today.", "Yesterday I [felt] tired all day.", "I have never [felt] better."),
        V(3, "ept", "leave", "left", "left", "залишати",
            "The train will [leave] at nine.", "She [left] the party early.", "They have [left] the country."),
        V(3, "ept", "mean", "meant", "meant", "означати",
            "What do these words [mean]?", "I never [meant] to hurt you.", "This job has [meant] a lot to me."),

        V(3, "ent", "spend", "spent", "spent", "витрачати",
            "We [spend] our weekends by the lake.", "I [spent] too much money yesterday.", "She has [spent] a year in Spain."),
        V(3, "ent", "send", "sent", "sent", "надсилати",
            "I [send] her a message every morning.", "He [sent] the letter last Monday.", "We have [sent] the invitations."),

        V(3, "old", "sell", "sold", "sold", "продавати",
            "They [sell] fresh fish at the market.", "He [sold] his car last month.", "We have [sold] all the tickets."),
        V(3, "old", "tell", "told", "told", "розповідати",
            "Please [tell] me the truth.", "She [told] me a secret yesterday.", "I have [told] you twice."),
        V(3, "old", "hold", "held", "held", "тримати",
            "Please [hold] the door for me.", "He [held] the baby carefully.", "She has [held] this job since May."),

        V(3, "ood", "stand", "stood", "stood", "стояти",
            "We [stand] in line every morning.", "He [stood] by the window for an hour.", "The house has [stood] here for a century."),
        V(3, "ood", "understand", "understood", "understood", "розуміти",
            "I [understand] the rules now.", "She [understood] the question at once.", "I have finally [understood] the problem."),

        V(3, "ade", "make", "made", "made", "робити",
            "I [make] coffee every morning.", "She [made] a cake yesterday.", "We have [made] a decision."),
        V(3, "ade", "say", "said", "said", "казати",
            "They always [say] hello.", "He [said] nothing about it.", "I have [said] enough."),
        V(3, "ade", "pay", "paid", "paid", "платити",
            "We [pay] the rent on the first of the month.", "I [paid] for the tickets yesterday.", "She has [paid] the bill."),
        V(3, "ade", "have", "had", "had", "мати",
            "I [have] two cats.", "We [had] a great time last night.", "They have [had] the same car for years."),

        V(3, "vowel", "sit", "sat", "sat", "сидіти",
            "We [sit] by the window at lunch.", "He [sat] down and waited.", "She has [sat] there all morning."),
        V(3, "vowel", "meet", "met", "met", "зустрічати",
            "We [meet] every Friday.", "I [met] your brother yesterday.", "Have you [met] my wife?"),
        V(3, "vowel", "win", "won", "won", "вигравати",
            "They [win] every match.", "Our team [won] the final last year.", "She has [won] three medals."),
        V(3, "vowel", "find", "found", "found", "знаходити",
            "I always [find] my keys in the end.", "He [found] a wallet on the street.", "We have [found] a good hotel."),
        V(3, "vowel", "get", "got", "got/gotten", "отримувати",
            "I [get] up at seven.", "She [got] a letter yesterday.", "He has [got] a new job.",
            "American English also uses gotten as the third form."),
        V(3, "vowel", "lose", "lost", "lost", "втрачати",
            "I often [lose] my umbrella.", "We [lost] the game on Saturday.", "I have [lost] my keys again."),
        V(3, "vowel", "hear", "heard", "heard", "чути",
            "I [hear] the birds every morning.", "I [heard] a strange noise last night.", "Have you [heard] the news?"),
        V(3, "vowel", "build", "built", "built", "будувати",
            "They [build] houses for a living.", "He [built] this table himself.", "They have [built] a new bridge."),

        // Group 4 — all three forms differ
        V(4, "i-a-u", "begin", "began", "begun", "починати",
            "Classes [begin] at nine.", "The film [began] an hour ago.", "The meeting has already [begun]."),
        V(4, "i-a-u", "drink", "drank", "drunk", "пити",
            "I [drink] tea every morning.", "He [drank] two glasses of water.", "She has [drunk] all the juice."),
        V(4, "i-a-u", "swim", "swam", "swum", "плавати",
            "We [swim] in the sea every summer.", "I [swam] across the lake yesterday.", "He has [swum] since he was three."),
        V(4, "i-a-u", "ring", "rang", "rung", "дзвонити",
            "The bells [ring] every hour.", "The phone [rang] twice last night.", "The alarm has [rung] already."),
        V(4, "i-a-u", "sing", "sang", "sung", "співати",
            "The children [sing] in a choir.", "She [sang] beautifully at the concert.", "I have never [sung] in public."),

        V(4, "ow-ew-own", "know", "knew", "known", "знати",
            "I [know] the answer.", "I [knew] him at school.", "I have [known] her for years."),
        V(4, "ow-ew-own", "grow", "grew", "grown", "рости",
            "Tomatoes [grow] well here.", "The tree [grew] two metres last year.", "The company has [grown] fast."),
        V(4, "ow-ew-own", "throw", "threw", "thrown", "кидати",
            "Don't [throw] stones at the window.", "He [threw] the ball to me.", "They have [thrown] away the old chairs."),
        V(4, "ow-ew-own", "blow", "blew", "blown", "дути",
            "The wind can [blow] hard here.", "The wind [blew] all night.", "The storm has [blown] the roof off."),
        V(4, "ow-ew-own", "fly", "flew", "flown", "літати",
            "Birds [fly] south in winter.", "We [flew] to Rome last spring.", "I have [flown] many times."),
        V(4, "ow-ew-own", "draw", "drew", "drawn", "малювати",
            "The children [draw] pictures every day.", "She [drew] a map for me.", "He has [drawn] a portrait of his mother."),

        V(4, "o-oke-oken", "speak", "spoke", "spoken", "говорити",
            "I [speak] three languages.", "She [spoke] to the manager yesterday.", "Have you [spoken] to him?"),
        V(4, "o-oke-oken", "break", "broke", "broken", "ламати",
            "Glasses [break] easily.", "I [broke] my phone last week.", "Someone has [broken] the window."),
        V(4, "o-oke-oken", "choose", "chose", "chosen", "вибирати",
            "You can [choose] any colour.", "He [chose] the blue one.", "We have [chosen] a name for the baby."),
        V(4, "o-oke-oken", "wear", "wore", "worn", "носити (одяг)",
            "I [wear] glasses for reading.", "She [wore] a red dress to the party.", "He has [worn] the same jacket for years."),

        V(4, "i-o-idden", "drive", "drove", "driven", "керувати (авто)",
            "I [drive] to work every day.", "She [drove] all night.", "He has [driven] this road many times."),
        V(4, "i-o-idden", "ride", "rode", "ridden", "їздити верхи",
            "We [ride] our bikes at weekends.", "He [rode] a horse for the first time.", "I have never [ridden] a camel."),
        V(4, "i-o-idden", "write", "wrote", "written", "писати",
            "I [write] in my diary every evening.", "She [wrote] a long letter yesterday.", "He has [written] three books."),

        V(4, "en", "take", "took", "taken", "брати",
            "I [take] the bus to work.", "She [took] my umbrella by mistake.", "They have [taken] the last seat."),
        V(4, "en", "give", "gave", "given", "давати",
            "We [give] presents at Christmas.", "He [gave] me his number.", "She has [given] up smoking."),
        V(4, "en", "forget", "forgot", "forgotten", "забувати",
            "I often [forget] names.", "I [forgot] my password again.", "You have [forgotten] our meeting."),
        V(4, "en", "eat", "ate", "eaten", "їсти",
            "We [eat] dinner at seven.", "I [ate] too much last night.", "Have you [eaten] yet?"),
        V(4, "en", "fall", "fell", "fallen", "падати",
            "Leaves [fall] in autumn.", "He [fell] off his bike.", "Prices have [fallen] this year."),
        V(4, "en", "see", "saw", "seen", "бачити",
            "I [see] my parents every weekend.", "We [saw] a great film last night.", "Have you [seen] my keys?"),

        V(4, "core", "be", "was/were", "been", "бути",
            "I want to [be] a doctor.", "We [were] at home yesterday.", "She has [been] to Japan twice.",
            "Past Simple is was with I / he / she / it and were with you / we / they."),
        V(4, "core", "do", "did", "done", "робити",
            "I [do] my homework after school.", "He [did] the dishes last night.", "Have you [done] the shopping?"),
        V(4, "core", "go", "went", "gone", "йти",
            "We [go] to school by bus.", "They [went] home early.", "She has [gone] to the shop."),
    ];

    private static readonly IReadOnlyDictionary<string, IrregularVerb> ByV1 =
        Verbs.ToDictionary(v => v.V1, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, VerbFamily> FamilyByKey =
        Families.ToDictionary(f => f.Key, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, int> FamilyIndexByKey =
        Families.Select((f, i) => (f.Key, i)).ToDictionary(x => x.Key, x => x.i, StringComparer.Ordinal);

    public static IrregularVerb? Find(string v1) => ByV1.GetValueOrDefault(v1);

    public static VerbFamily FamilyOf(IrregularVerb verb) => FamilyByKey[verb.Family];

    public static VerbFamily? FindFamily(string key) => FamilyByKey.GetValueOrDefault(key);

    /// <summary>Position of a family on the learning path, 0-based.</summary>
    public static int FamilyIndex(string key) => FamilyIndexByKey[key];

    public static IReadOnlyList<IrregularVerb> VerbsOf(string familyKey) =>
        Verbs.Where(v => v.Family == familyKey).ToList();

    public static IReadOnlyList<IrregularVerb> VerbsOfGroup(int group) =>
        Verbs.Where(v => v.Group == group).ToList();

    public static IReadOnlyList<VerbFamily> FamiliesOfGroup(int group) =>
        Families.Where(f => f.Group == group).ToList();

    public static string GroupTitle(int group) => GroupTitles[group - 1];
}
