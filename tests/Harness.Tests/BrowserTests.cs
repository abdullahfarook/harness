using System.Net;
using System.Net.Sockets;
using System.Text;
using Harness.Browser;
using NUnit.Framework;

namespace Harness.Tests;

public class BrowserTests
{
    [Test]
    public async Task EmptyPageIsRejected()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-empty-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None);
        Assert.ThrowsAsync<InvalidDataException>(async()=>await browser.NavigateAsync(new Uri(fixture.Url,"empty").AbsoluteUri));
    }
    [Test]
    public async Task PrivateSubresourceIsBlocked()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-private-resource-"+Guid.NewGuid().ToString("N")));
        UrlPolicy policy=new(fixture.Url,host=>Task.FromResult(new[] { IPAddress.Parse(host==fixture.Url.Host ? "8.8.8.8" : "127.0.0.1") }));
        await using BrowserTools browser=new(fixture.Url,evidence,null,policy);
        await browser.StartAsync(true,CancellationToken.None);
        await browser.NavigateAsync(new Uri(fixture.Url,"resource").AbsoluteUri);
        Assert.That(File.ReadAllText(Path.Combine(evidence.DirectoryPath,"events.jsonl")),Does.Contain("blocked_request").And.Contain("localhost"));
    }
    [Test]
    public async Task NavigationCancellationReturnsPromptly()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-cancel-test-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None);
        using CancellationTokenSource timeout=new(TimeSpan.FromMilliseconds(100));
        System.Diagnostics.Stopwatch elapsed=System.Diagnostics.Stopwatch.StartNew();
        Assert.That(async()=>await browser.NavigateAsync(new Uri(fixture.Url,"slow").AbsoluteUri,timeout.Token),Throws.InstanceOf<OperationCanceledException>());
        Assert.That(elapsed.Elapsed,Is.LessThan(TimeSpan.FromSeconds(2)));
    }
    [Test]
    public async Task SameOriginRedirectIsRejectedRatherThanMisreported()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-same-redirect-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None);
        Assert.That(async()=>await browser.NavigateAsync(new Uri(fixture.Url,"redirectsame").AbsoluteUri),Throws.Exception);
        Assert.That(browser.LastObservation,Is.Null);
    }
    [Test]
    public async Task WebSocketsAreBlocked()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-websocket-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None);
        await browser.NavigateAsync(new Uri(fixture.Url,"socket").AbsoluteUri);
        Assert.That(File.ReadAllText(Path.Combine(evidence.DirectoryPath,"events.jsonl")),Does.Contain("blocked_websocket"));
    }
    [Test]
    public async Task CrossOriginRedirectIsBlockedBeforeDestinationLoads()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-redirect-test-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None);
        Assert.That(async()=>await browser.NavigateAsync(new Uri(fixture.Url,"redirect").AbsoluteUri),Throws.Exception);
        Assert.That(File.ReadAllText(Path.Combine(evidence.DirectoryPath,"events.jsonl")),Does.Contain("blocked_request").And.Contain("https://example.com/"));
    }
    [Test]
    public async Task RealHeadedBrowserExtractsTextClicksObservedLinkAndRejectsStaleId()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-browser-test-"+Guid.NewGuid().ToString("N")));
        UrlPolicy testPolicy=new(fixture.Url,_ => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }));
        await using BrowserTools browser=new(fixture.Url,evidence,null,testPolicy);
        await browser.StartAsync(false,CancellationToken.None);
        PageObservation first=await browser.NavigateAsync(fixture.Url.AbsoluteUri);
        Assert.That(first.Text,Does.Contain("Fixture product"));
        Assert.That(first.Links,Has.Count.EqualTo(1));
        PageObservation second=await browser.ClickAsync(first.Links[0].Id);
        Assert.That(second.Url,Does.EndWith("/about"));
        Assert.That(second.Text,Does.Contain("Fixture audience"));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await browser.ClickAsync(first.Links[0].Id));
        Assert.That(File.Exists(Path.Combine(evidence.DirectoryPath,"page-00.png")),Is.True);
        string eventLine=File.ReadAllLines(Path.Combine(evidence.DirectoryPath,"events.jsonl")).First(line=>line.Contains("\"cursor\"",StringComparison.Ordinal));
        using System.Text.Json.JsonDocument observed=System.Text.Json.JsonDocument.Parse(eventLine);
        using System.Text.Json.JsonDocument cursor=System.Text.Json.JsonDocument.Parse(observed.RootElement.GetProperty("data").GetProperty("cursor").GetString()!);
        Assert.That(cursor.RootElement.GetProperty("moves").GetInt32(),Is.GreaterThan(10));
        for (int i=0;i<3;i++) { await browser.NavigateAsync(new Uri(fixture.Url,"?page="+i).AbsoluteUri); }
        Assert.ThrowsAsync<InvalidOperationException>(async()=>await browser.NavigateAsync(new Uri(fixture.Url,"?page=extra").AbsoluteUri));
        TestContext.Out.WriteLine("SCREENSHOT_DIRECTORY="+evidence.DirectoryPath);
    }

    [Test]
    public async Task ObserveCallsAlsoConsumeTheActionBudget()
    {
        using Fixture fixture=new();
        EvidenceWriter evidence=new(Path.Combine(Path.GetTempPath(),"website-budget-test-"+Guid.NewGuid().ToString("N")));
        await using BrowserTools browser=new(fixture.Url,evidence,null,new(fixture.Url,_=>Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") })));
        await browser.StartAsync(true,CancellationToken.None); await browser.NavigateAsync(fixture.Url.AbsoluteUri);
        for (int i=0;i<19;i++) { await browser.ObserveAsync(); }
        Assert.ThrowsAsync<InvalidOperationException>(async()=>await browser.ObserveAsync());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly HttpListener listener=new();
        public Uri Url { get; }
        public Fixture()
        {
            TcpListener reserve=new(IPAddress.Loopback,0); reserve.Start(); int port=((IPEndPoint)reserve.LocalEndpoint).Port; reserve.Stop();
            Url=new($"http://127.0.0.1:{port}/"); listener.Prefixes.Add(Url.AbsoluteUri); listener.Start();
            _=Task.Run(Serve);
        }
        private async Task Serve()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context=await listener.GetContextAsync(); } catch (HttpListenerException) { return; } catch (ObjectDisposedException) { return; }
                if (context.Request.Url!.AbsolutePath=="/slow") { await Task.Delay(3000); }
                if (context.Request.Url!.AbsolutePath=="/redirect") { context.Response.StatusCode=302; context.Response.RedirectLocation="https://example.com/"; context.Response.Close(); continue; }
                if (context.Request.Url!.AbsolutePath=="/redirectsame") { context.Response.StatusCode=302; context.Response.RedirectLocation="/about"; context.Response.Close(); continue; }
                string text=context.Request.Url!.AbsolutePath=="/about" ? "<title>About</title><body><h1>Fixture audience: developers</h1></body>" : "<title>Fixture</title><body><h1>Fixture product</h1><a href='/about' style='display:block;margin:200px'>About the product</a></body>";
                if (context.Request.Url!.AbsolutePath=="/socket") { text+="<script>new WebSocket('ws://127.0.0.1:8765/private')</script>"; }
                if (context.Request.Url!.AbsolutePath=="/empty") { text="<body></body>"; }
                if (context.Request.Url!.AbsolutePath=="/resource") { text+="<img src='http://localhost:8765/private.png'>"; }
                byte[] data=Encoding.UTF8.GetBytes(text); context.Response.ContentType="text/html"; context.Response.ContentLength64=data.Length;
                await context.Response.OutputStream.WriteAsync(data); context.Response.Close();
            }
        }
        public void Dispose() { listener.Close(); }
    }
}
