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
                    Description = "Do not alert on respectful questions, requests for Church teaching, pastoral counseling, repentance, testimony against sin, recovery discussion, medical triage discussion without advocacy, or quoting another position for debate. This exemption should win unless the message clearly promotes conduct contrary to Catholic moral teaching.",
                    Examples = new List<string>
                    {
                        "What does the Catechism say about contraception?",
                        "I used to sleep around and I regret it. Please pray for me.",
                        "Can someone explain why the Church says abortion is wrong?",
                        "I am trying to quit porn and drugs."
                    }
                },
                new()
                {
                    Name = "Abortion advocacy or normalization",
                    Description = "Alert when a message defends, celebrates, recommends, normalizes, or encourages abortion as morally acceptable, a right, a good solution, or a routine personal choice. Do not alert on miscarriage, ectopic pregnancy triage discussion without advocacy, legal news reporting, or sincere requests for explanation.",
                    Examples = new List<string>
                    {
                        "Abortion is healthcare and there is nothing morally wrong with it.",
                        "Just get an abortion and move on.",
                        "Women should always have the right to kill the baby if they want.",
                        "Abortion is a good solution when a pregnancy is inconvenient."
                    }
                },
                new()
                {
                    Name = "Contraception promotion or mockery of openness to life",
                    Description = "Alert when a message promotes contraception as morally good, urges Catholics to reject the Church's teaching on openness to life, or mocks chastity and marital openness to children in a direct promotional way. Do not alert on questions about NFP, infertility grief, or good-faith doctrinal discussion.",
                    Examples = new List<string>
                    {
                        "Catholics should ignore the Church and use birth control however they want.",
                        "Contraception is obviously good and the Church is wrong to oppose it.",
                        "Anyone who is open to life is stupid. Just use contraception.",
                        "Married Catholics should reject Humanae Vitae and contracept."
                    }
                },
                new()
                {
                    Name = "Sex outside marriage or adultery promotion",
                    Description = "Alert when a message celebrates, encourages, or pressures others toward fornication, hookup culture, adultery, prostitution, or other sexual conduct outside marriage as morally acceptable. Do not alert on repentance, abuse support, questions, or non-promotional discussion of past sin.",
                    Examples = new List<string>
                    {
                        "Casual sex is totally fine. Sleep around while you're young.",
                        "There is nothing wrong with sex before marriage.",
                        "Cheating on your spouse is fine if they never find out.",
                        "Hooking up with random people is healthy and normal."
                    }
                },
                new()
                {
                    Name = "Pornography or explicit sexual exploitation promotion",
                    Description = "Alert when a message promotes pornography, OnlyFans-style sexual exploitation, sexting, nude exchanges, or explicit sexual behavior in a celebratory or encouraging way. Do not alert on anti-porn discussion, recovery support, or non-graphic moral discussion.",
                    Examples = new List<string>
                    {
                        "Porn is healthy and everyone should watch it.",
                        "Send nudes. It is not a big deal.",
                        "OnlyFans is a great moral way to make money.",
                        "You should watch porn to improve your sex life."
                    }
                },
                new()
                {
                    Name = "Drug use promotion",
                    Description = "Alert when a message glorifies, encourages, pressures, or normalizes recreational drug use, intoxication, or abuse of illegal or non-medical substances. Do not alert on addiction recovery, legal news, prescribed medical use, or warnings against drug abuse.",
                    Examples = new List<string>
                    {
                        "You should try cocaine at least once.",
                        "Getting high every weekend is awesome.",
                        "Everyone should do edibles and chill.",
                        "Hard drugs are fine if they make you feel good."
                    }
                },
                new()
                {
                    Name = "Drunkenness or alcohol abuse promotion",
                    Description = "Alert when a message celebrates drunkenness, binge drinking, or pressures others to get wasted, blackout drunk, or abuse alcohol. Do not alert on moderate lawful alcohol use, cooking, cultural discussion, or condemnation of drunkenness.",
                    Examples = new List<string>
                    {
                        "Let's get wasted tonight.",
                        "Being blackout drunk is the best.",
                        "You are lame if you do not get drunk with us.",
                        "Drink until you cannot stand up."
                    }
                },
                new()
                {
                    Name = "Persistent public contradiction of Catholic moral teaching after correction",
                    Description = "Alert when a user persistently and assertively keeps promoting grave moral error after correction by moderators or catechetical explanation, especially when they continue to pressure others to reject Catholic teaching on life, sexuality, chastity, sobriety, or openness to life. This is for repeated agitation, not one-off confusion.",
                    Examples = new List<string>
                    {
                        "I know the Church condemns abortion, but I am going to keep telling everyone it is good.",
                        "Even after the mods corrected me, I will keep teaching that porn and hookups are fine.",
                        "I was already told what Catholic teaching is and I am still going to push people to get high and get drunk."
                    }
                }
            }
        };
    }
}
