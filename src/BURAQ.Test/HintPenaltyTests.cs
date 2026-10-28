using System.Collections;
using System.Reflection;
using BURAQ.Models.Data;
using BURAQ.Models.Request.Edit;
using BURAQ.Models.Request.Game;
using Xunit;

namespace BURAQ.Test;

public sealed class HintPenaltyTests
{
    [Fact]
    public void PaidHintsRemainHiddenUntilTheTeamUnlocksThem()
    {
        var challenge = NewChallenge(originalScore: 500, hints: ["Free context", "Paid clue"]);
        SetHintPenalties(challenge, [0, 25]);

        var detail = CreateDetail(challenge, new Dictionary<int, int>());
        var hints = GetHints(detail);

        Assert.Equal("Free context", GetProperty<string>(hints[0], "Content"));
        Assert.True(GetProperty<bool>(hints[0], "IsUnlocked"));
        Assert.Equal(0, GetProperty<int>(hints[0], "Penalty"));

        Assert.Null(GetProperty<string?>(hints[1], "Content"));
        Assert.False(GetProperty<bool>(hints[1], "IsUnlocked"));
        Assert.Equal(25, GetProperty<int>(hints[1], "Penalty"));
        Assert.Equal(500, GetProperty<int>(detail, "Score"));
    }

    [Fact]
    public void UnlockedHintReducesOnlyThatTeamsChallengeAwardAndNeverBelowZero()
    {
        var challenge = NewChallenge(originalScore: 20, hints: ["Paid clue"]);
        SetHintPenalties(challenge, [25]);

        var detail = CreateDetail(challenge, new Dictionary<int, int> { [0] = 25 });
        var hint = Assert.Single(GetHints(detail));

        Assert.Equal("Paid clue", GetProperty<string>(hint, "Content"));
        Assert.True(GetProperty<bool>(hint, "IsUnlocked"));
        Assert.Equal(0, GetProperty<int>(detail, "Score"));
    }

    [Theory]
    [InlineData(2, new[] { 5, 10 }, true)]
    [InlineData(2, new[] { 5 }, false)]
    [InlineData(2, new[] { 5, -1 }, false)]
    public void HintPenaltyConfigurationRequiresOneNonNegativeValuePerHint(int existingHintCount,
        int[] penalties, bool expected)
    {
        var model = new ChallengeUpdateModel { HintPenalties = penalties.ToList() };
        var validation = typeof(ChallengeUpdateModel).GetMethod("HasValidHintPenaltyConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(validation);

        Assert.Equal(expected, validation.Invoke(model, [existingHintCount]));
    }

    [Theory]
    [InlineData(new[] { "First", "Second", "Third" }, true)]
    [InlineData(new[] { "Second", "First" }, false)]
    [InlineData(new[] { "Edited", "Second" }, false)]
    [InlineData(new[] { "First" }, false)]
    public void PurchasedHintPositionsCanOnlyBePreservedOrAppended(string[] updatedHints, bool expected)
    {
        var rule = typeof(GameChallenge).GetMethod("KeepsPurchasedHintsStable",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(rule);

        Assert.Equal(expected, rule.Invoke(null, [new List<string> { "First", "Second" }, updatedHints.ToList()]));
    }

    private static GameChallenge NewChallenge(int originalScore, List<string> hints) =>
        new()
        {
            OriginalScore = originalScore,
            MinScoreRate = 1,
            Difficulty = 1,
            Hints = hints
        };

    private static void SetHintPenalties(GameChallenge challenge, List<int> penalties)
    {
        var property = typeof(GameChallenge).GetProperty("HintPenalties");
        Assert.NotNull(property);
        property.SetValue(challenge, penalties);
    }

    private static object CreateDetail(GameChallenge challenge, IReadOnlyDictionary<int, int> unlockedPenalties)
    {
        var factory = typeof(ChallengeDetailModel).GetMethod(
            nameof(ChallengeDetailModel.FromInstance),
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types:
            [
                typeof(GameInstance),
                typeof(int),
                typeof(ChallengeInfo),
                typeof(IReadOnlyDictionary<int, int>)
            ],
            modifiers: null);
        Assert.NotNull(factory);

        return factory.Invoke(null,
                   [new GameInstance { Challenge = challenge }, 0, null, unlockedPenalties])
               ?? throw new InvalidOperationException("Challenge details were not created.");
    }

    private static object[] GetHints(object detail) =>
        ((IEnumerable)(GetProperty<object>(detail, "Hints")
            ?? throw new InvalidOperationException("Hints were not included in the challenge details.")))
        .Cast<object>()
        .ToArray();

    private static T? GetProperty<T>(object instance, string name) =>
        (T?)instance.GetType().GetProperty(name)?.GetValue(instance);
}
