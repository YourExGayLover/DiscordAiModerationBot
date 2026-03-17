using DiscordAiModeration.Bot.Models;

namespace DiscordAiModeration.Bot.Services;

public static class CatholicRulePack
{
    public const string PackName = "Catholic Heresy Watch";

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
                    Name = "Good-Faith Discussion Exemption",
                    Description = "Do not alert on respectful questions, requests for clarification, quoting another position for discussion, asking what Catholics believe, comparing traditions in good faith, or imperfect wording where the user is plainly seeking understanding rather than asserting error. This exemption should win unless the message clearly promotes or teaches against Catholic doctrine.",
                    Examples = new List<string>
                    {
                        "Why do Catholics believe the Eucharist is really Jesus and not symbolic?",
                        "Protestants say confession to a priest is unbiblical. How would a Catholic answer that?",
                        "I disagree but I am trying to understand what the Catechism teaches on baptism.",
                        "Can someone explain why Catholics ask saints to pray for them?"
                    }
                },
                new()
                {
                    Name = "Denial of the Trinity or Christ's Divinity",
                    Description = "Alert when a message clearly asserts that the Trinity is false, that Jesus is not truly God, that the Son is a creature only, or that the Holy Spirit is not divine. Do not alert when someone is asking a question or quoting another view for discussion.",
                    Examples = new List<string>
                    {
                        "Jesus is not God and the Trinity is a pagan invention.",
                        "The Holy Spirit is not divine, just a force.",
                        "Christ was only a created being, not equal to the Father."
                    }
                },
                new()
                {
                    Name = "Denial of the Incarnation, Resurrection, or essential Creedal truths",
                    Description = "Alert when a message clearly denies that the Word became flesh, that Jesus truly rose bodily from the dead, or rejects essential apostolic creed-level truths in a direct and assertive way.",
                    Examples = new List<string>
                    {
                        "Jesus did not physically rise from the dead.",
                        "The incarnation is just a metaphor.",
                        "Christ was not truly born of the Virgin Mary."
                    }
                },
                new()
                {
                    Name = "Denial of the Real Presence in the Eucharist",
                    Description = "Alert when a message clearly teaches that the Eucharist is only a symbol, that the Mass is merely symbolic worship, or that Christ is not truly present in the Eucharist. Do not alert on sincere requests for explanation or comparison.",
                    Examples = new List<string>
                    {
                        "The Eucharist is only bread and wine and nothing more.",
                        "Jesus is not really present in Communion, it is purely symbolic.",
                        "The Mass is just a memorial meal and Catholics are wrong about the Real Presence."
                    }
                },
                new()
                {
                    Name = "Denial of sacramental baptism or baptismal regeneration",
                    Description = "Alert when a message clearly teaches that baptism does nothing, is only an outward symbol, is never a means of grace, or directly rejects Catholic teaching that baptism truly unites us to Christ and remits sins. Do not alert on nuanced discussion about extraordinary cases or desire for baptism.",
                    Examples = new List<string>
                    {
                        "Baptism is only a public symbol and never does anything spiritually.",
                        "Baptism does not remit sins in any sense.",
                        "Catholics are wrong to say baptism saves."
                    }
                },
                new()
                {
                    Name = "Denial of confession and priestly absolution",
                    Description = "Alert when a message clearly teaches that sacramental confession is invalid, that priests have no authority to absolve sins, or that confession to a priest is anti-Christian. Do not alert on personal discomfort or good-faith questions.",
                    Examples = new List<string>
                    {
                        "No priest can forgive sins and confession is a fake Catholic invention.",
                        "Confession to a priest is invalid and should be rejected.",
                        "Priestly absolution is blasphemy."
                    }
                },
                new()
                {
                    Name = "Rejection of the Church's teaching authority and apostolic succession",
                    Description = "Alert when a message clearly teaches that Christ did not establish a visible teaching Church, that bishops and priests have no apostolic authority whatsoever, or that the Church has no authority to bind believers in doctrine. Do not alert on discussion of historical abuse, prudential criticism, or questions about authority.",
                    Examples = new List<string>
                    {
                        "The Church has no authority to define doctrine for Christians.",
                        "Apostolic succession is a fraud and bishops have no real authority.",
                        "Every believer is his own final authority and the Church cannot teach binding truth."
                    }
                },
                new()
                {
                    Name = "Anti-Catholic slander presented as fact",
                    Description = "Alert when a message presents hostile falsehoods as fact against Catholicism, such as claiming Catholics worship idols, worship Mary, are not Christians, or that the Pope is the antichrist. Do not alert on respectful disagreement or when someone is asking whether such claims are true.",
                    Examples = new List<string>
                    {
                        "Catholics worship idols and Mary.",
                        "Catholics are not Christians.",
                        "The Pope is the antichrist."
                    }
                },
                new()
                {
                    Name = "Promotion of leaving the Church or rejecting the sacraments",
                    Description = "Alert when a message urges members to leave the Catholic Church, reject the sacraments, abandon confession, avoid the Mass, or turn others away from Catholic teaching in a direct promotional way.",
                    Examples = new List<string>
                    {
                        "Leave the Catholic Church and stop going to Mass.",
                        "Do not confess to priests and reject Catholic sacraments.",
                        "Catholics should abandon the Eucharist because it is false worship."
                    }
                },
                new()
                {
                    Name = "Persistent catechism contradiction after correction",
                    Description = "Alert when a user persistently and assertively keeps teaching against clearly defined Catholic doctrine after correction by moderators or catechetical explanation, especially if they continue pushing error as truth. This is for repeated doctrinal agitation, not one-off confusion.",
                    Examples = new List<string>
                    {
                        "I was corrected already but I still say the Eucharist is only symbolic and Catholics need to stop teaching otherwise.",
                        "I know what the Church says, and I am still here to teach people that confession is false and they must reject it.",
                        "Even after being shown the Catechism, I will keep teaching that the Trinity is false."
                    }
                }
            }
        };
    }
}
