namespace LanguageLab.Domain.IrregularVerbs;

/// <summary>How a card was drawn: the stage's five-word window, or a weighted random run.</summary>
public enum DrillMode
{
    Batch,
    Free,
}

/// <summary>What a free run draws from: one stage, that stage and every earlier one, or the whole catalog.</summary>
public enum DrillScope
{
    Stage,
    Cumulative,
    All,
}
