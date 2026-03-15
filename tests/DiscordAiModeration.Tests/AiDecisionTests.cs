using DiscordAiModeration.Core.Models;
using FluentAssertions;
using Xunit;

namespace DiscordAiModeration.Tests;

public sealed class AiDecisionTests
{
    [Fact]
    public void AiDecision_ShouldStoreValues()
    {
        var decision = new AiDecision(true, "Harassment", 78, "Possible targeted insult.");

        decision.ShouldAlert.Should().BeTrue();
        decision.RuleName.Should().Be("Harassment");
        decision.Confidence.Should().Be(78);
        decision.Reason.Should().Be("Possible targeted insult.");
    }
}
