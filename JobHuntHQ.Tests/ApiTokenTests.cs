using JobHuntHQ.Models;

namespace JobHuntHQ.Tests;

public class ApiTokenTests
{
    [Fact]
    public void Generate_returns_a_prefixed_token_whose_hash_matches_the_raw_value()
    {
        var (raw, displayPrefix, hash) = ApiToken.Generate();

        Assert.StartsWith(ApiToken.Prefix, raw);
        Assert.Equal(12, displayPrefix.Length);
        Assert.Equal(raw[..12], displayPrefix);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
        Assert.Equal(ApiToken.ComputeSha256Hex(raw), hash);
        Assert.DoesNotContain(raw, hash);
    }

    [Fact]
    public void Generate_does_not_repeat_the_secret()
    {
        var first = ApiToken.Generate();
        var second = ApiToken.Generate();

        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.NotEqual(first.Sha256Hex, second.Sha256Hex);
    }

    [Theory]
    [InlineData("applications:read", true)]
    [InlineData("APPLICATIONS:READ", true)]
    [InlineData("applications:write", false)]
    [InlineData(" applications:read ", false)]
    [InlineData("", false)]
    public void HasScope_matches_scope_names_case_insensitively(string required, bool expected)
    {
        var token = new ApiToken { Scopes = "profile:read, applications:read" };

        Assert.Equal(expected, token.HasScope(required));
    }

    [Fact]
    public void HasScope_trims_entries_stored_on_the_token()
    {
        var token = new ApiToken { Scopes = " applications:read , profile:read " };

        Assert.True(token.HasScope("applications:read"));
        Assert.True(token.HasScope("profile:read"));
    }

    [Fact]
    public void IsRevoked_follows_RevokedUtc()
    {
        var token = new ApiToken();
        Assert.False(token.IsRevoked);

        token.RevokedUtc = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(token.IsRevoked);
    }
}
