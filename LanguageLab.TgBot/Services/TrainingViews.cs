using System.Globalization;
using System.Text;
using LanguageLab.Domain.Entities;
using LanguageLab.Domain.Training;
using Telegram.Bot.Types.ReplyMarkups;

namespace LanguageLab.TgBot.Services;

/// <summary>
/// Тексти й клавіатури тренування. Чистий: жодних звернень до БД чи Telegram API.
/// Повідомлення розраховані на ParseMode.Html — переклади приходять із файлу
/// і можуть містити символи, які ламають legacy-Markdown.
/// </summary>
public static class TrainingViews
{
    private const int MaxButtonLabel = 40;

    public static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Menu(string dictionaryName, int learnable, int due) =>
        $"Словник: <b>{Escape(dictionaryName)}</b> · вивчаю {learnable} · на сьогодні {due}";

    public static InlineKeyboardMarkup MenuKeyboard(long dictionaryId, int dueCount)
    {
        List<List<InlineKeyboardButton>> rows =
        [
            [InlineKeyboardButton.WithCallbackData("📚 Новий батч (5 слів)", $"tnew_{dictionaryId}")]
        ];

        if (dueCount > 0)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData($"🔁 Закріплення ({dueCount})", "treview")]);
        }

        rows.Add([
            InlineKeyboardButton.WithCallbackData("📊 Статистика", "tstats"),
            InlineKeyboardButton.WithCallbackData("📖 Змінити словник", "tdicts")
        ]);

        return new InlineKeyboardMarkup(rows);
    }

    public static string CardsText(IReadOnlyList<WordPair> words)
    {
        var builder = new StringBuilder("Нові слова:\n\n");

        foreach (var word in words)
        {
            builder.AppendLine($"<b>{Escape(word.Word)}</b> — {Escape(word.Translation)}");
        }

        return builder.ToString();
    }

    public static InlineKeyboardMarkup CardsKeyboard(long trainingId)
    {
        List<List<InlineKeyboardButton>> rows =
        [
            [InlineKeyboardButton.WithCallbackData("Почати квіз ▶", $"tcards_{trainingId}")]
        ];

        return new InlineKeyboardMarkup(rows);
    }

    public static string QuestionText(int number, int total, string? header, TrainingQuestion question)
    {
        var prompt = question.Direction == QuestionDirection.EnToUa
            ? question.WordPair.Word
            : question.WordPair.Translation;

        var builder = new StringBuilder($"Питання {number}/{total}");

        if (!string.IsNullOrEmpty(header))
        {
            builder.Append($" · {header}");
        }

        builder.Append("\n\n");
        builder.Append($"<b>{Escape(prompt)}</b>");

        return builder.ToString();
    }

    public static string OptionLabel(WordPair option, QuestionDirection direction) =>
        direction == QuestionDirection.EnToUa ? option.Translation : option.Word;

    public static InlineKeyboardMarkup QuestionKeyboard(
        TrainingQuestion question, IReadOnlyList<WordPair> options, bool canDelete)
    {
        var rows = new List<List<InlineKeyboardButton>>();

        for (var i = 0; i < options.Count; i += 2)
        {
            var row = new List<InlineKeyboardButton>();

            for (var j = i; j < Math.Min(i + 2, options.Count); j++)
            {
                var label = Trim(OptionLabel(options[j], question.Direction));
                row.Add(InlineKeyboardButton.WithCallbackData(label, $"tq_{question.Id}_{options[j].Id}"));
            }

            rows.Add(row);
        }

        var actions = new List<InlineKeyboardButton>
        {
            InlineKeyboardButton.WithCallbackData("✅ Знаю", $"tknow_{question.Id}")
        };

        if (canDelete)
        {
            actions.Add(InlineKeyboardButton.WithCallbackData("🗑 Видалити", $"tdel_{question.Id}"));
        }

        rows.Add(actions);
        return new InlineKeyboardMarkup(rows);
    }

    public static string AnswerHeader(bool isCorrect, WordPair word) =>
        isCorrect
            ? "✅ Правильно"
            : $"❌ {Escape(word.Word)} — {Escape(word.Translation)}";

    public static string SummaryText(TrainingSummary summary, DateTime nowUtc)
    {
        var percent = summary.Total == 0 ? 0 : (int)Math.Round(summary.Ratio * 100);
        var builder = new StringBuilder($"Сесія завершена — {summary.Correct}/{summary.Total} ({percent}%)\n\n");

        foreach (var word in summary.Words.OrderByDescending(w => w.Correct == w.Total).ThenBy(w => w.Word))
        {
            var mark = word.Correct == word.Total ? "✅" : "❌";
            var score = word.Correct == word.Total ? string.Empty : $" ({word.Correct}/{word.Total})";

            builder.AppendLine(
                $"<b>{mark} {Escape(word.Word)}</b> — {Escape(word.Translation)}{score} · box {word.Box} · {FormatDue(word, nowUtc)}");
        }

        return builder.ToString();
    }

    public static InlineKeyboardMarkup SummaryKeyboard(
        TrainingSummary summary, long trainingId, long? dictionaryId, int dueCount)
    {
        var primary = new List<InlineKeyboardButton>();

        if (summary.Passed && dictionaryId.HasValue)
        {
            primary.Add(InlineKeyboardButton.WithCallbackData("Ще 5 слів", $"tnew_{dictionaryId.Value}"));
        }
        else if (!summary.Passed)
        {
            primary.Add(InlineKeyboardButton.WithCallbackData("Повторити помилки", $"tretry_{trainingId}"));
        }

        if (dueCount > 0)
        {
            primary.Add(InlineKeyboardButton.WithCallbackData($"🔁 Закріплення ({dueCount})", "treview"));
        }

        var rows = new List<List<InlineKeyboardButton>>();

        if (primary.Count > 0)
        {
            rows.Add(primary);
        }

        rows.Add([InlineKeyboardButton.WithCallbackData("Стоп", "tstop")]);
        return new InlineKeyboardMarkup(rows);
    }

    public static string StatsText(TrainingStats stats)
    {
        var answered = stats.Correct + stats.Wrong;
        var accuracy = answered == 0 ? 0 : (int)Math.Round(100.0 * stats.Correct / answered);

        var builder = new StringBuilder("<b>Статистика</b>\n\n");

        for (var i = 0; i < stats.BoxCounts.Count; i++)
        {
            builder.AppendLine($"box {i + 1}: {stats.BoxCounts[i]}");
        }

        builder.AppendLine();
        builder.AppendLine($"Вивчено: {stats.Learned}");
        builder.AppendLine($"Знав до навчання: {stats.Known}");
        builder.AppendLine($"На сьогодні: {stats.Due}");
        builder.AppendLine($"Правильних відповідей: {accuracy}% ({stats.Correct} з {answered})");

        return builder.ToString();
    }

    private static string FormatDue(WordResult word, DateTime nowUtc)
    {
        if (word.IsLearned || word.DueAt is null)
        {
            return "вивчено";
        }

        var due = word.DueAt.Value.Date;

        if (due == nowUtc.Date.AddDays(1))
        {
            return "завтра";
        }

        return due == nowUtc.Date
            ? "сьогодні"
            : due.ToString("dd.MM", CultureInfo.InvariantCulture);
    }

    private static string Trim(string label) =>
        label.Length <= MaxButtonLabel ? label : label[..(MaxButtonLabel - 1)] + "…";
}
