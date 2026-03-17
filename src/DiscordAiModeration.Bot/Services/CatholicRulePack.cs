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
                    Description = """
Do not alert on respectful questions, requests for clarification, quoting another position for discussion, asking what Catholics believe, comparing traditions in good faith, or imperfect wording where the user is plainly seeking understanding rather than asserting error.
CCC References: CCC 30, CCC 851, CCC 1785
Catechism Quote: "Man is by nature and vocation a religious being."
""",
                    Examples = new[]
                    {
                        "Why do Catholics believe in the Real Presence?",
                        "Can someone explain confession to me?",
                        "Protestants say the Eucharist is symbolic; how would Catholics answer that?"
                    }
                },
                new()
                {
                    Name = "Denial of the Trinity",
                    Description = """
Flag messages that clearly deny the Trinity, deny that God is Father, Son, and Holy Spirit, or teach that the Son or the Holy Spirit are merely creatures rather than truly God.
CCC References: CCC 232-267
Catechism Quote: "The mystery of the Most Holy Trinity is the central mystery of Christian faith and life."
""",
                    Examples = new[]
                    {
                        "The Trinity is a false doctrine.",
                        "Jesus is not God, only a creature.",
                        "The Holy Spirit is just a force, not God."
                    }
                },
                new()
                {
                    Name = "Denial of the Divinity of Christ",
                    Description = """
Flag messages that clearly deny that Jesus Christ is true God and true man, deny the Incarnation, or present Jesus as a merely human teacher.
CCC References: CCC 456-483, CCC 464
Catechism Quote: "The Word became flesh to save us by reconciling us with God."
""",
                    Examples = new[]
                    {
                        "Jesus was only a moral teacher.",
                        "Christ was not really God in the flesh.",
                        "The Incarnation never happened."
                    }
                },
                new()
                {
                    Name = "Denial of the Real Presence",
                    Description = """
Flag messages that clearly deny the Real Presence, teach that the Eucharist is only symbolic, or urge Catholics to reject the Church's teaching on the Eucharist.
CCC References: CCC 1374, CCC 1376
Catechism Quote: "In the most blessed sacrament of the Eucharist the body and blood, together with the soul and divinity, of our Lord Jesus Christ... is truly, really, and substantially contained."
""",
                    Examples = new[]
                    {
                        "The Eucharist is only a symbol.",
                        "Jesus is not really present in Communion.",
                        "Catholics are wrong to worship the Eucharist."
                    }
                },
                new()
                {
                    Name = "Denial of Sacramental Confession",
                    Description = """
Flag messages that clearly deny the sacrament of Penance, reject priestly absolution as invalid, or urge Catholics to ignore Christ's gift of reconciliation through the Church.
CCC References: CCC 1441-1449, CCC 1461
Catechism Quote: "Since Christ entrusted to his apostles the ministry of reconciliation, bishops who are their successors, and priests, the bishops' collaborators, continue to exercise this ministry."
""",
                    Examples = new[]
                    {
                        "No priest can forgive sins.",
                        "Confession to a priest is unbiblical and fake.",
                        "Catholics should never go to confession."
                    }
                },
                new()
                {
                    Name = "Denial of Baptismal Regeneration",
                    Description = """
Flag messages that clearly deny that baptism truly remits sins and gives new birth in Christ, or teach that sacramental baptism is only an empty symbol.
CCC References: CCC 1213, CCC 1262-1270
Catechism Quote: "Holy Baptism is the basis of the whole Christian life, the gateway to life in the Spirit."
""",
                    Examples = new[]
                    {
                        "Baptism does nothing.",
                        "Baptism is only an outward symbol.",
                        "No one receives grace in baptism."
                    }
                },
                new()
                {
                    Name = "Explicit Heresy Against Defined Catholic Teaching",
                    Description = """
Flag messages that obstinately reject or mock truths the Catholic Church teaches must be believed with divine and Catholic faith. Use this for direct doctrinal rejection not covered more specifically by another rule.
CCC References: CCC 2088-2089
Catechism Quote: "Heresy is the obstinate post-baptismal denial of some truth which must be believed with divine and catholic faith."
""",
                    Examples = new[]
                    {
                        "Dogma means nothing and Catholics should reject it.",
                        "The Church's defined doctrines are false and should be ignored.",
                        "I reject any doctrine Rome says must be believed."
                    }
                }
            }
        };
    }
}
