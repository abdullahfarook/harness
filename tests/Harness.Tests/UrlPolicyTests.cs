using System.Net;
using Harness.Browser;
using NUnit.Framework;

namespace Harness.Tests;

public class UrlPolicyTests
{
    [TestCase("POST")]
    [TestCase("PUT")]
    [TestCase("DELETE")]
    public void MutatingBrowserRequestsAreRejected(string method) => Assert.Throws<InvalidOperationException>(()=>UrlPolicy.ValidateMethod(method));
    [TestCase("127.0.0.1")]
    [TestCase("10.0.0.1")]
    [TestCase("192.168.1.5")]
    [TestCase("169.254.169.254")]
    [TestCase("::1")]
    [TestCase("fc00::1")]
    [TestCase("::ffff:127.0.0.1")]
    public void PrivateAddressesAreRejected(string address) => Assert.That(UrlPolicy.IsPublic(IPAddress.Parse(address)),Is.False);
    [Test]
    public async Task AllowsPublicSameOriginAndRejectsOtherOriginsAndSchemes()
    {
        UrlPolicy policy = new(new("https://openplatestudio.com/"),_ => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }));
        Assert.That((await policy.ValidateAsync("https://openplatestudio.com/about",true)).Host,Is.EqualTo("openplatestudio.com"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await policy.ValidateAsync("https://example.com",true));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await policy.ValidateAsync("file:///etc/passwd",false));
    }
}
