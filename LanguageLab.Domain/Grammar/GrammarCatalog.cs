using static LanguageLab.Domain.Grammar.GrammarSyllabus;

namespace LanguageLab.Domain.Grammar;

/// <summary>
/// The written grammar topics and their content, in syllabus order. Which topics exist at all, and
/// in what order, is <see cref="GrammarSyllabus"/>; a syllabus topic missing here is shown as
/// planned. All of it is written for this project; nothing is taken from a published grammar.
/// </summary>
public static class GrammarCatalog
{
    public static IReadOnlyList<GrammarTopic> Topics { get; } =
    [
        new(
            Key: "be",
            Section: SentenceBasics,
            Level: GrammarLevel.A1,
            Title: "be: am, is, are",
            Explanation:
            [
                "Use [am], [is] and [are] to say who someone is, what something is like, or where it is.",
                "I → [am]. He, she, it → [is]. You, we, they → [are].",
                "In speech we often join them: [I'm], [she's], [they're].",
            ],
            Examples:
            [
                "I [am] a student.",
                "She [is] at work.",
                "We [are] tired today.",
                "It [is] cold outside.",
            ],
            Exercises:
            [
                new("My parents ___ at home.", ["am", "is", "are"], "are", "My parents = they → are."),
                new("I ___ from Ukraine.", ["am", "is", "are"], "am", "I → am."),
                new("This book ___ very old.", ["am", "is", "are"], "is", "This book = it → is."),
                new("You and Tom ___ late again.", ["am", "is", "are"], "are", "You and Tom = you (two people) → are."),
                new("The water ___ cold.", ["am", "is", "are"], "is", "The water = it → is."),
            ]),
        new(
            Key: "there-is-are",
            Section: SentenceBasics,
            Level: GrammarLevel.A1,
            Title: "there is, there are",
            Explanation:
            [
                "Use [there is] and [there are] to say that something exists or is in a place.",
                "One thing, or something you can't count (water, money) → [there is]. Two or more things → [there are].",
                "For a question, swap the words: [Is there…?] [Are there…?]",
            ],
            Examples:
            [
                "There [is] a cat in the garden.",
                "There [are] two windows in my room.",
                "There [is] some milk in the fridge.",
                "[Are] there any eggs?",
            ],
            Exercises:
            [
                new("There ___ a bank near my house.", ["is", "are"], "is", "One bank → there is."),
                new("There ___ three people in the car.", ["is", "are"], "are", "Three people → there are."),
                new("There ___ some coffee in the cup.", ["is", "are"], "is", "You can't count coffee → there is."),
                new("There ___ a lot of shops in our town.", ["is", "are"], "are", "A lot of shops = many → there are."),
                new("___ there a park near here?", ["Is", "Are"], "Is", "One park → Is there…?"),
            ]),
        new(
            Key: "have-have-got",
            Section: SentenceBasics,
            Level: GrammarLevel.A1,
            Title: "have, have got",
            Explanation:
            [
                "Use [have] to talk about things you own, people in your family and how you look.",
                "I, you, we, they → [have]. He, she, it → [has].",
                "[have got] means the same: [I've got], [she's got]. In questions and negatives after [do] / [does] use [have], not [got].",
            ],
            Examples:
            [
                "I [have] a sister.",
                "She [has] got a new bike.",
                "They [have got] a big house.",
                "[Do] you have a pen?",
            ],
            Exercises:
            [
                new("She ___ two brothers.", ["has", "have"], "has", "She → has."),
                new("We ___ got a big garden.", ["has", "have"], "have", "We → have got."),
                new("He ___ got blue eyes.", ["has", "have"], "has", "He → has got."),
                new("Do you ___ a pen?", ["have", "has", "got"], "have", "After do / does we use have, not got."),
                new("They ___ a dog.", ["has", "have"], "have", "They → have."),
            ]),
        new(
            Key: "present-simple",
            Section: PresentTenses,
            Level: GrammarLevel.A1,
            Title: "present simple",
            Explanation:
            [
                "Use the present simple for habits, facts and things that are always true.",
                "With he, she, it the verb takes [-s]: [works], [lives]. With I, you, we, they it stays the same: [work], [live].",
                "Negatives and questions use [do] / [does] and the plain verb: [I don't like], [she doesn't like], [Does she like…?]",
            ],
            Examples:
            [
                "I [work] in an office.",
                "He [plays] football on Sundays.",
                "She [doesn't] eat meat.",
                "[Does] he live here?",
            ],
            Exercises:
            [
                new("My sister ___ in a bank.", ["work", "works"], "works", "My sister = she → works."),
                new("They ___ coffee in the morning.", ["drink", "drinks"], "drink", "They → the plain verb, no -s."),
                new("He ___ like fish.", ["don't", "doesn't"], "doesn't", "He → doesn't."),
                new("What time ___ the shop open?", ["do", "does"], "does", "The shop = it → does."),
                new("She doesn't ___ TV in the evening.", ["watch", "watches", "watching"], "watch", "After doesn't we use the plain verb."),
            ]),
        new(
            Key: "present-continuous",
            Section: PresentTenses,
            Level: GrammarLevel.A1,
            Title: "present continuous",
            Explanation:
            [
                "Use the present continuous for something that is happening now, at this moment.",
                "Make it with [am], [is] or [are] and the verb + [-ing]: [I am reading], [she is reading], [they are reading].",
                "If the verb ends in a silent [e], drop it before [-ing]: [write] → [writing].",
            ],
            Examples:
            [
                "I [am] cooking dinner.",
                "He [is] sleeping.",
                "They [are] playing outside.",
                "[Are] you listening?",
            ],
            Exercises:
            [
                new("Look! It ___ raining.", ["is", "are", "am"], "is", "It → is."),
                new("I ___ reading a book now.", ["am", "is", "are"], "am", "I → am."),
                new("They are ___ football at the moment.", ["play", "plays", "playing"], "playing", "After are we need the -ing form."),
                new("We ___ not working today.", ["is", "are", "am"], "are", "We → are."),
                new("She is ___ a letter.", ["write", "writing", "writeing"], "writing", "Drop the silent e: write → writing."),
            ]),
        new(
            Key: "past-simple",
            Section: PastTenses,
            Level: GrammarLevel.A1,
            Title: "past simple",
            Explanation:
            [
                "Use the past simple for something that started and finished in the past: [yesterday], [last week], [in 2020].",
                "Regular verbs add [-ed]: [work] → [worked]. Many common verbs are irregular: [go] → [went], [see] → [saw].",
                "Negatives and questions use [did] and the plain verb: [I didn't go], [Did you go…?]",
            ],
            Examples:
            [
                "I [worked] all day.",
                "She [went] home early.",
                "We [didn't] see him.",
                "[Did] you call her?",
            ],
            Exercises:
            [
                new("I ___ to the cinema yesterday.", ["go", "went", "goed"], "went", "go is irregular: go → went."),
                new("She ___ her keys last week.", ["lose", "lost", "losted"], "lost", "lose is irregular: lose → lost."),
                new("We ___ TV last night.", ["watch", "watched", "watching"], "watched", "A finished action in the past → watched."),
                new("He didn't ___ the answer.", ["know", "knew", "known"], "know", "After didn't we use the plain verb."),
                new("What time ___ you get up today?", ["did", "do", "does"], "did", "A question about the past → did."),
            ]),
    ];
}
