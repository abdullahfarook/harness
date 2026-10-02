using Harness.Models;
using NUnit.Framework;

namespace Harness.Tests;

public class TokenizerTests
{
    [TestCase("lfm", "tokenizer.json")]
    [TestCase("laya", "tokenizer/tokenizer.json")]
    public void MatchesIndependentUpstreamTokenIds(string model, string file)
    {
        using NativeTokenizer tokenizer = new(Path.Combine(Root, model, file));
        using System.Text.Json.JsonDocument fixtures = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "tokenizer-reference.json")));
        foreach (System.Text.Json.JsonElement fixture in fixtures.RootElement.GetProperty(model).EnumerateArray())
        {
            Assert.That(tokenizer.Encode(fixture.GetProperty("text").GetString()!), Is.EqualTo(fixture.GetProperty("ids").EnumerateArray().Select(x => x.GetInt32()).ToArray()));
        }
    }
    private static string Root => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "../../../../../.local/website-models"));

    [Test]
    public void LfmSpecialTokensMatchPublishedTokenizer()
    {
        using NativeTokenizer tokenizer = new(Path.Combine(Root, "lfm/tokenizer.json"));
        Assert.That(tokenizer.Encode("<|im_start|>user<|im_end|>"), Has.Length.EqualTo(3));
        Assert.That(tokenizer.SpecialId("<|im_start|>"), Is.EqualTo(6));
        Assert.That(tokenizer.SpecialId("<|im_end|>"), Is.EqualTo(7));
    }

    [TestCase("lfm", "tokenizer.json")]
    [TestCase("laya", "tokenizer/tokenizer.json")]
    public void NativeTokenizerPreservesUnicodeAndDoesNotPad(string model, string file)
    {
        using NativeTokenizer tokenizer = new(Path.Combine(Root, model, file));
        int[] ids = tokenizer.Encode("Hello café دنیا");
        Assert.That(ids.Length, Is.LessThan(25));
        Assert.That(tokenizer.Decode(ids), Does.Contain("café"));
    }
}
