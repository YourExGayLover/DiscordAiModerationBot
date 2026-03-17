using DiscordAiModeration.Bot.Models;

namespace DiscordAiModeration.Bot.Services;

public static class CatholicMoralityRulePack
{
    public const string PackName = "Catholic Moral Teaching Watch";

    public static RulesExportFile Create(long guildId)
    {
        return new RulesExportFile
        {
            GuildId = guildId,
            ExportedUtc = DateTime.UtcNow,
            Rules = new List<RuleImportItem>
            {
                new()
                {
                    Name = "Moral Teaching Good-Faith Exemption",
                    Description = """
Do not alert on respectful questions, requests for Church teaching, pastoral counseling, repentance, testimony against sin, recovery discussion, medical triage discussion without advocacy, or quoting another position for debate.
CCC References: CCC 1783-1785, CCC 1802
Catechism Quote: "Conscience must be informed and moral judgment enlightened."
""",
                    Examples = new[]
                    {
                        "What does the Church teach about contraception?",
                        "I used to be addicted and I want help quitting.",
                        "Can someone explain why Catholics reject abortion?"
                    }
                },
                new()
                {
                    Name = "Abortion Advocacy",
                    Description = """
Flag messages that defend, celebrate, recommend, normalize, or encourage abortion as morally acceptable. Do not flag medical emergency discussion that does not advocate abortion as a good.
CCC References: CCC 2270-2275
Catechism Quote: "Since the first century the Church has affirmed the moral evil of every procured abortion."
""",
                    Examples = new[]
                    {
                        "Abortion is healthcare and always morally fine.",
                        "Just get an abortion and move on.",
                        "There is nothing wrong with killing the unborn."
                    }
                },
                new()
                {
                    Name = "Contraception Advocacy",
                    Description = """
Flag messages that promote contraception as morally good or urge Catholics to reject the Church's teaching on openness to life in marriage.
CCC References: CCC 2366-2370
Catechism Quote: "Every action which... proposes, whether as an end or as a means, to render procreation impossible is intrinsically evil."
""",
                    Examples = new[]
                    {
                        "Contraception is always morally good.",
                        "Catholics should ignore the Church and use birth control.",
                        "There is nothing sinful about deliberately sterilizing sex."
                    }
                },
                new()
                {
                    Name = "Promotion of Sex Outside Marriage",
                    Description = """
Flag messages that celebrate or encourage fornication, adultery, hookups, or sexual behavior outside valid marriage as morally acceptable.
CCC References: CCC 2337-2353, CCC 2380-2381
Catechism Quote: "Sexual pleasure is morally disordered when sought for itself, isolated from its procreative and unitive purposes."
""",
                    Examples = new[]
                    {
                        "Casual sex is totally fine.",
                        "Sleep around while you are young.",
                        "Cheating is okay if you do not get caught."
                    }
                },
                new()
                {
                    Name = "Pornography or Lewd Promotion",
                    Description = """
Flag messages that promote pornography, sexual exploitation, sharing explicit sexual content, or encouraging others to consume such content.
CCC References: CCC 2354
Catechism Quote: "Pornography consists in removing real or simulated sexual acts from the intimacy of the partners, in order to display them deliberately to third parties."
""",
                    Examples = new[]
                    {
                        "Porn is healthy and everyone should watch it.",
                        "Send explicit pics here.",
                        "OnlyFans content is morally great and should be promoted."
                    }
                },
                new()
                {
                    Name = "Drug Use Promotion",
                    Description = """
Flag messages that encourage recreational drug abuse, intoxication, or misuse of substances in a way contrary to sobriety and moral responsibility.
CCC References: CCC 2290-2291
Catechism Quote: "The use of drugs inflicts very grave damage on human health and life."
""",
                    Examples = new[]
                    {
                        "Everyone should get high this weekend.",
                        "Try cocaine at least once.",
                        "Abusing pills is no big deal."
                    }
                },
                new()
                {
                    Name = "Drunkenness or Alcohol Abuse Promotion",
                    Description = """
Flag messages that celebrate drunkenness, binge drinking, or pressure others into getting drunk. Do not flag ordinary discussion of lawful moderate alcohol use.
CCC References: CCC 2290
Catechism Quote: "The virtue of temperance disposes us to avoid every kind of excess."
""",
                    Examples = new[]
                    {
                        "Let us get wasted tonight.",
                        "Being blackout drunk is the best.",
                        "You are lame if you do not get drunk."
                    }
                }
            }
        };
    }
}
