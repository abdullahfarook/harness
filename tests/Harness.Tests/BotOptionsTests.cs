using NUnit.Framework;

namespace Harness.Tests;

public class BotOptionsTests
{
    [Test]
    public void DefaultsAreLiveWebsiteAndHeadedBrowser()
    {
        BotOptions options=BotOptions.Parse([]);
        Assert.That(options.Url.AbsoluteUri,Is.EqualTo("https://openplatestudio.com/"));
        Assert.That(options.Headless,Is.False);
    }
    [Test]
    public void UnknownOptionsAndInvalidTimeoutFail()
    {
        Assert.Throws<ArgumentException>(() => BotOptions.Parse(["--typo"]));
        Assert.Throws<ArgumentException>(() => BotOptions.Parse(["--timeout-seconds","-1"]));
    }
}
