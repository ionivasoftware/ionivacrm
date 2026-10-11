using System.Security.Cryptography;
using FluentAssertions;
using IonCrm.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace IonCrm.Tests.SocialPosts;

public class AesGcmTokenProtectorTests
{
    [Fact]
    public void Roundtrip_and_fresh_nonce_each_time()
    {
        var p = new AesGcmTokenProtector("anahtar-malzemesi");
        var a = p.Protect("EAAB-long-page-token");
        var b = p.Protect("EAAB-long-page-token");

        a.Should().NotBe(b, "random nonce per encryption");
        p.Unprotect(a).Should().Be("EAAB-long-page-token");
        p.Unprotect(b).Should().Be("EAAB-long-page-token");
        p.Unprotect(p.Protect(string.Empty)).Should().Be(string.Empty);
    }

    [Fact]
    public void Wrong_key_or_tampering_fails_closed()
    {
        var cipher = new AesGcmTokenProtector("key-1").Protect("secret");

        var act = () => new AesGcmTokenProtector("key-2").Unprotect(cipher);
        act.Should().Throw<CryptographicException>();

        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0xFF; // flip a ciphertext bit
        var tampered = () => new AesGcmTokenProtector("key-1").Unprotect(Convert.ToBase64String(bytes));
        tampered.Should().Throw<CryptographicException>();

        var garbage = () => new AesGcmTokenProtector("key-1").Unprotect("not-base64!");
        garbage.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Configuration_prefers_dedicated_key_and_falls_back_to_jwt_secret()
    {
        var dedicated = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Social:TokenKey"] = "dedicated", ["JwtSettings:Secret"] = "jwt",
        }).Build();
        var jwtOnly = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["JwtSettings:Secret"] = "jwt" }).Build();
        var none = new ConfigurationBuilder().Build();

        var cipher = new AesGcmTokenProtector(dedicated).Protect("t");
        new AesGcmTokenProtector("dedicated").Unprotect(cipher).Should().Be("t");
        new AesGcmTokenProtector(jwtOnly).Unprotect(new AesGcmTokenProtector("jwt").Protect("u")).Should().Be("u");

        var act = () => new AesGcmTokenProtector(none);
        act.Should().Throw<InvalidOperationException>();
    }
}
