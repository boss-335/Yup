using System.Net;
using VoiceBattle.Net;
using Xunit;

namespace VoiceBattle.Net.Tests;

internal sealed class FakeAuth : IAuthenticator
{
    public readonly Dictionary<string, AuthResult> ByTicket = new();
    public TaskCompletionSource<AuthResult>? Pending;
    public Task<AuthResult> Validate(Hello h)
    {
        if (Pending != null) return Pending.Task;
        return Task.FromResult(h.SteamTicket != null && ByTicket.TryGetValue(h.SteamTicket, out var r) ? r : AuthResult.Refused("steam_ticket_required"));
    }
}

internal sealed class StubHandler : HttpMessageHandler
{
    public string Body = "";
    public HttpStatusCode Status = HttpStatusCode.OK;
    public Uri? LastUri;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastUri = request.RequestUri;
        return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
    }
}

public class AuthTests
{
    private static GameServer Server(IAuthenticator auth) =>
        new(Harness.Cfg, Harness.Battle, Harness.Modes, Harness.Packs, () => 1000, seed: 1, auth: auth);

    [Fact]
    public void SteamServer_RefusesWithoutValidTicket()
    {
        var auth = new FakeAuth();
        var s = Server(auth);
        var c = new FakeClient(s);
        c.Say(new Hello(Wire.ProtocolVersion, "x", "abu_sawt"));
        Assert.Equal("auth", c.Last<Error>().Code);
        Assert.True(c.Closed);
    }

    [Fact]
    public void ValidTicket_WelcomesWithSteamId_AndRoomsShowIt()
    {
        var auth = new FakeAuth();
        auth.ByTicket["aabb"] = new AuthResult(true, "76561198000000001", null);
        var s = Server(auth);
        var c = new FakeClient(s);
        c.Say(new Hello(Wire.ProtocolVersion, "Jaber", "abu_sawt", "aabb"));
        Assert.Equal("76561198000000001", c.Last<Welcome>().SteamId);
        c.Say(new CreateRoom("duel"));
        Assert.Equal("76561198000000001", c.Last<RoomState>().Players.Single().SteamId);
    }

    [Fact]
    public void SameSteamAccount_NewLoginReplacesOld()
    {
        var auth = new FakeAuth();
        auth.ByTicket["01"] = new AuthResult(true, "7656", null);
        auth.ByTicket["02"] = new AuthResult(true, "7656", null);
        var s = Server(auth);
        var first = new FakeClient(s); first.Say(new Hello(Wire.ProtocolVersion, "a", "abu_sawt", "01"));
        var second = new FakeClient(s); second.Say(new Hello(Wire.ProtocolVersion, "a", "abu_sawt", "02"));
        Assert.True(first.Closed);
        Assert.Equal("replaced", first.Last<Error>().Code);
        Assert.False(second.Closed);
    }

    [Fact]
    public void SlowValidation_MessagesBeforeWelcomeAreRefused_ThenItCompletes()
    {
        var auth = new FakeAuth { Pending = new TaskCompletionSource<AuthResult>() };
        var s = Server(auth);
        var c = new FakeClient(s);
        c.Say(new Hello(Wire.ProtocolVersion, "a", "abu_sawt", "aa"));
        c.Say(new CreateRoom("duel"));
        Assert.Equal("hello_first", c.Last<Error>().Code);
        c.Say(new Hello(Wire.ProtocolVersion, "a", "abu_sawt", "aa"));
        Assert.Equal("already_hello", c.Last<Error>().Code);
        auth.Pending.SetResult(new AuthResult(true, "9", null));
        Assert.Equal("9", c.Last<Welcome>().SteamId);
    }

    [Theory]
    [InlineData(@"{""response"":{""params"":{""result"":""OK"",""steamid"":""76561198012345678"",""ownersteamid"":""76561198012345678"",""vacbanned"":false,""publisherbanned"":false}}}", true, "76561198012345678")]
    [InlineData(@"{""response"":{""params"":{""result"":""OK"",""steamid"":""1"",""vacbanned"":true,""publisherbanned"":false}}}", false, "vac_banned")]
    [InlineData(@"{""response"":{""params"":{""result"":""OK"",""steamid"":""1"",""vacbanned"":false,""publisherbanned"":true}}}", false, "publisher_banned")]
    [InlineData(@"{""response"":{""error"":{""errorcode"":101,""errordesc"":""Invalid ticket""}}}", false, "steam_rejected:Invalid ticket")]
    [InlineData(@"<html>", false, "steam_bad_response")]
    public void WebApiResponse_IsParsed(string body, bool ok, string expect)
    {
        var r = SteamWebApiAuthenticator.Parse(body);
        Assert.Equal(ok, r.Ok);
        Assert.Equal(expect, ok ? r.SteamId : r.Reason);
    }

    [Fact]
    public async Task WebApi_CallsSteamWithKeyAppAndIdentity()
    {
        var h = new StubHandler { Body = @"{""response"":{""params"":{""result"":""OK"",""steamid"":""42"",""vacbanned"":false,""publisherbanned"":false}}}" };
        var a = new SteamWebApiAuthenticator(new HttpClient(h), "KEY123", 480, "voicebattle");
        var r = await a.Validate(new Hello(1, "x", "y", "0A0B"));
        Assert.True(r.Ok);
        Assert.Equal("42", r.SteamId);
        var q = h.LastUri!.Query;
        Assert.Contains("key=KEY123", q); Assert.Contains("appid=480", q); Assert.Contains("ticket=0A0B", q); Assert.Contains("identity=voicebattle", q);
        Assert.StartsWith(SteamWebApiAuthenticator.DefaultEndpoint, h.LastUri.ToString());
    }

    [Fact]
    public async Task WebApi_RejectsBadTicketsWithoutCallingSteam()
    {
        var h = new StubHandler();
        var a = new SteamWebApiAuthenticator(new HttpClient(h), "K", 480, "voicebattle");
        Assert.Equal("steam_ticket_required", (await a.Validate(new Hello(1, "x", "y", "zz&key=evil"))).Reason);
        Assert.Equal("steam_ticket_required", (await a.Validate(new Hello(1, "x", "y", null))).Reason);
        Assert.Null(h.LastUri);
        h.Status = HttpStatusCode.Forbidden;
        Assert.Equal("steam_http_403", (await a.Validate(new Hello(1, "x", "y", "aa"))).Reason);
    }

    [Fact]
    public void WebApi_NeedsAKey() => Assert.Throws<ArgumentException>(() => new SteamWebApiAuthenticator(new HttpClient(), "", 480, "x"));
}
