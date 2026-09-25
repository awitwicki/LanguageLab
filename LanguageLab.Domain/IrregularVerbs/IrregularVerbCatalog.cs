namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>
/// The 68 irregular verbs of the program in learning order: four stages by verb type.
/// Static data — verbs are never database rows. Examples are English; translations are
/// Ukrainian.
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

    private static IrregularVerb V(
        int group, string v1, string v2, string v3, string translation,
        string present, string past, string perfect, string? note = null) =>
        new(
            group,
            v1,
            v2.Split('/'),
            v3.Split('/'),
            translation,
            [new Example(Tense.Present, present), new Example(Tense.Past, past), new Example(Tense.Perfect, perfect)],
            note);

    public static readonly IReadOnlyList<IrregularVerb> Verbs =
    [
        // Group 1 — all three forms alike
        V(1, "cut", "cut", "cut", "різати", "I [cut] the bread every morning.", "Yesterday I [cut] my finger.", "I have [cut] the paper into strips."),
        V(1, "put", "put", "put", "класти", "I [put] my keys on the table every evening.", "Last night I [put] the book back on the shelf.", "I have [put] your bag in the car."),
        V(1, "let", "let", "let", "дозволяти", "My parents [let] me stay up late on Fridays.", "Yesterday she [let] the cat out.", "They have [let] us use their garden."),
        V(1, "set", "set", "set", "встановлювати", "We [set] the table before dinner.", "He [set] the alarm for six yesterday.", "I have [set] a new record."),
        V(1, "hit", "hit", "hit", "вдаряти", "The boys [hit] the ball over the fence every time.", "Last week a car [hit] the fence.", "She has [hit] the target three times."),
        V(1, "shut", "shut", "shut", "зачиняти", "Please [shut] the door when you leave.", "He [shut] the window an hour ago.", "They have [shut] the shop for the holidays."),
        V(1, "cost", "cost", "cost", "коштувати", "Tickets [cost] ten dollars each.", "The dinner [cost] more than we expected.", "The repairs have [cost] us a fortune."),
        V(1, "hurt", "hurt", "hurt", "боліти / ранити", "My feet [hurt] after a long walk.", "I [hurt] my knee last weekend.", "You have [hurt] her feelings."),
        V(1, "read", "read", "read", "читати", "I [read] the news every morning.", "Last night I [read] two chapters.", "I have [read] this book twice.", "Spelt the same in all three forms, but V2 and V3 are pronounced /red/."),

        // Group 2 — first and third alike
        V(2, "come", "came", "come", "приходити", "My friends [come] to visit every summer.", "He [came] home late yesterday.", "She has [come] to see you."),
        V(2, "become", "became", "become", "ставати", "Children [become] taller every year.", "She [became] a doctor in 2020.", "The city has [become] very expensive."),
        V(2, "run", "ran", "run", "бігти", "We [run] in the park every Sunday.", "He [ran] five kilometres this morning.", "I have [run] a marathon twice."),

        // Group 3 — second and third alike
        V(3, "buy", "bought", "bought", "купувати", "I [buy] fresh bread every day.", "She [bought] a new phone last week.", "We have [bought] a house."),
        V(3, "bring", "brought", "brought", "приносити", "Please [bring] your notebook to class.", "He [brought] flowers yesterday.", "They have [brought] some good news."),
        V(3, "think", "thought", "thought", "думати", "I [think] about it every day.", "I [thought] you were at work.", "I have [thought] about your offer."),
        V(3, "catch", "caught", "caught", "ловити", "We [catch] the same bus every morning.", "She [caught] a cold last week.", "The police have [caught] the thief."),
        V(3, "teach", "taught", "taught", "навчати", "They [teach] English at a local school.", "My mother [taught] me to cook.", "He has [taught] here for ten years."),

        V(3, "sleep", "slept", "slept", "спати", "Babies [sleep] most of the day.", "I [slept] badly last night.", "The cat has [slept] all afternoon."),
        V(3, "keep", "kept", "kept", "тримати", "I [keep] my keys in this drawer.", "She [kept] the letter for years.", "You have [kept] your promise."),
        V(3, "feel", "felt", "felt", "відчувати", "I [feel] happy today.", "Yesterday I [felt] tired all day.", "I have never [felt] better."),
        V(3, "leave", "left", "left", "залишати", "The train will [leave] at nine.", "She [left] the party early.", "They have [left] the country."),
        V(3, "mean", "meant", "meant", "означати", "What do these words [mean]?", "I never [meant] to hurt you.", "This job has [meant] a lot to me."),

        V(3, "spend", "spent", "spent", "витрачати", "We [spend] our weekends by the lake.", "I [spent] too much money yesterday.", "She has [spent] a year in Spain."),
        V(3, "send", "sent", "sent", "надсилати", "I [send] her a message every morning.", "He [sent] the letter last Monday.", "We have [sent] the invitations."),

        V(3, "sell", "sold", "sold", "продавати", "They [sell] fresh fish at the market.", "He [sold] his car last month.", "We have [sold] all the tickets."),
        V(3, "tell", "told", "told", "розповідати", "Please [tell] me the truth.", "She [told] me a secret yesterday.", "I have [told] you twice."),
        V(3, "hold", "held", "held", "тримати", "Please [hold] the door for me.", "He [held] the baby carefully.", "She has [held] this job since May."),

        V(3, "stand", "stood", "stood", "стояти", "We [stand] in line every morning.", "He [stood] by the window for an hour.", "The house has [stood] here for a century."),
        V(3, "understand", "understood", "understood", "розуміти", "I [understand] the rules now.", "She [understood] the question at once.", "I have finally [understood] the problem."),

        V(3, "make", "made", "made", "робити", "I [make] coffee every morning.", "She [made] a cake yesterday.", "We have [made] a decision."),
        V(3, "say", "said", "said", "казати", "They always [say] hello.", "He [said] nothing about it.", "I have [said] enough."),
        V(3, "pay", "paid", "paid", "платити", "We [pay] the rent on the first of the month.", "I [paid] for the tickets yesterday.", "She has [paid] the bill."),
        V(3, "have", "had", "had", "мати", "I [have] two cats.", "We [had] a great time last night.", "They have [had] the same car for years."),

        V(3, "sit", "sat", "sat", "сидіти", "We [sit] by the window at lunch.", "He [sat] down and waited.", "She has [sat] there all morning."),
        V(3, "meet", "met", "met", "зустрічати", "We [meet] every Friday.", "I [met] your brother yesterday.", "Have you [met] my wife?"),
        V(3, "win", "won", "won", "вигравати", "They [win] every match.", "Our team [won] the final last year.", "She has [won] three medals."),
        V(3, "find", "found", "found", "знаходити", "I always [find] my keys in the end.", "He [found] a wallet on the street.", "We have [found] a good hotel."),
        V(3, "get", "got", "got/gotten", "отримувати", "I [get] up at seven.", "She [got] a letter yesterday.", "He has [got] a new job.", "American English also uses gotten as the third form."),
        V(3, "lose", "lost", "lost", "втрачати", "I often [lose] my umbrella.", "We [lost] the game on Saturday.", "I have [lost] my keys again."),
        V(3, "hear", "heard", "heard", "чути", "I [hear] the birds every morning.", "I [heard] a strange noise last night.", "Have you [heard] the news?"),
        V(3, "build", "built", "built", "будувати", "They [build] houses for a living.", "He [built] this table himself.", "They have [built] a new bridge."),

        // Group 4 — all three forms differ
        V(4, "begin", "began", "begun", "починати", "Classes [begin] at nine.", "The film [began] an hour ago.", "The meeting has already [begun]."),
        V(4, "drink", "drank", "drunk", "пити", "I [drink] tea every morning.", "He [drank] two glasses of water.", "She has [drunk] all the juice."),
        V(4, "swim", "swam", "swum", "плавати", "We [swim] in the sea every summer.", "I [swam] across the lake yesterday.", "He has [swum] since he was three."),
        V(4, "ring", "rang", "rung", "дзвонити", "The bells [ring] every hour.", "The phone [rang] twice last night.", "The alarm has [rung] already."),
        V(4, "sing", "sang", "sung", "співати", "The children [sing] in a choir.", "She [sang] beautifully at the concert.", "I have never [sung] in public."),

        V(4, "know", "knew", "known", "знати", "I [know] the answer.", "I [knew] him at school.", "I have [known] her for years."),
        V(4, "grow", "grew", "grown", "рости", "Tomatoes [grow] well here.", "The tree [grew] two metres last year.", "The company has [grown] fast."),
        V(4, "throw", "threw", "thrown", "кидати", "Don't [throw] stones at the window.", "He [threw] the ball to me.", "They have [thrown] away the old chairs."),
        V(4, "blow", "blew", "blown", "дути", "The wind can [blow] hard here.", "The wind [blew] all night.", "The storm has [blown] the roof off."),
        V(4, "fly", "flew", "flown", "літати", "Birds [fly] south in winter.", "We [flew] to Rome last spring.", "I have [flown] many times."),
        V(4, "draw", "drew", "drawn", "малювати", "The children [draw] pictures every day.", "She [drew] a map for me.", "He has [drawn] a portrait of his mother."),

        V(4, "speak", "spoke", "spoken", "говорити", "I [speak] three languages.", "She [spoke] to the manager yesterday.", "Have you [spoken] to him?"),
        V(4, "break", "broke", "broken", "ламати", "Glasses [break] easily.", "I [broke] my phone last week.", "Someone has [broken] the window."),
        V(4, "choose", "chose", "chosen", "вибирати", "You can [choose] any colour.", "He [chose] the blue one.", "We have [chosen] a name for the baby."),
        V(4, "wear", "wore", "worn", "носити (одяг)", "I [wear] glasses for reading.", "She [wore] a red dress to the party.", "He has [worn] the same jacket for years."),

        V(4, "drive", "drove", "driven", "керувати (авто)", "I [drive] to work every day.", "She [drove] all night.", "He has [driven] this road many times."),
        V(4, "ride", "rode", "ridden", "їздити верхи", "We [ride] our bikes at weekends.", "He [rode] a horse for the first time.", "I have never [ridden] a camel."),
        V(4, "write", "wrote", "written", "писати", "I [write] in my diary every evening.", "She [wrote] a long letter yesterday.", "He has [written] three books."),

        V(4, "take", "took", "taken", "брати", "I [take] the bus to work.", "She [took] my umbrella by mistake.", "They have [taken] the last seat."),
        V(4, "give", "gave", "given", "давати", "We [give] presents at Christmas.", "He [gave] me his number.", "She has [given] up smoking."),
        V(4, "forget", "forgot", "forgotten", "забувати", "I often [forget] names.", "I [forgot] my password again.", "You have [forgotten] our meeting."),
        V(4, "eat", "ate", "eaten", "їсти", "We [eat] dinner at seven.", "I [ate] too much last night.", "Have you [eaten] yet?"),
        V(4, "fall", "fell", "fallen", "падати", "Leaves [fall] in autumn.", "He [fell] off his bike.", "Prices have [fallen] this year."),
        V(4, "see", "saw", "seen", "бачити", "I [see] my parents every weekend.", "We [saw] a great film last night.", "Have you [seen] my keys?"),

        V(4, "be", "was/were", "been", "бути", "I want to [be] a doctor.", "We [were] at home yesterday.", "She has [been] to Japan twice.", "Past Simple is was with I / he / she / it and were with you / we / they."),
        V(4, "do", "did", "done", "робити", "I [do] my homework after school.", "He [did] the dishes last night.", "Have you [done] the shopping?"),
        V(4, "go", "went", "gone", "йти", "We [go] to school by bus.", "They [went] home early.", "She has [gone] to the shop."),
    ];

    private static readonly IReadOnlyDictionary<string, IrregularVerb> ByV1 =
        Verbs.ToDictionary(v => v.V1, StringComparer.Ordinal);

    /// <summary>The four stages of the program, one per verb type.</summary>
    public static int GroupCount => GroupTitles.Count;

    public static IrregularVerb? Find(string v1) => ByV1.GetValueOrDefault(v1);

    public static IReadOnlyList<IrregularVerb> VerbsOfGroup(int group) =>
        Verbs.Where(v => v.Group == group).ToList();

    /// <summary>The stage and every earlier one — the cumulative scope of free training.</summary>
    public static IReadOnlyList<IrregularVerb> VerbsUpToGroup(int group) =>
        Verbs.Where(v => v.Group <= group).ToList();

    public static string GroupTitle(int group) => GroupTitles[group - 1];
}
