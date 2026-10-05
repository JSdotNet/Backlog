using Backlog.Infrastructure.SpecManager.OAuth;

namespace Backlog.Infrastructure.SpecManager.UnitTests;

public sealed class PkceTests
{
    [Fact]
    public void The_challenge_is_the_S256_of_the_verifier_as_RFC_7636_works_it_out()
    {
        // RFC 7636, Appendix B.
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void A_verifier_is_43_base64url_characters_and_never_the_same_twice()
    {
        var first = Pkce.CreateVerifier();
        var second = Pkce.CreateVerifier();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_state_is_url_safe_and_never_the_same_twice()
    {
        var first = Pkce.CreateState();

        Assert.Matches("^[A-Za-z0-9_-]+$", first);
        Assert.NotEqual(first, Pkce.CreateState());
    }
}
