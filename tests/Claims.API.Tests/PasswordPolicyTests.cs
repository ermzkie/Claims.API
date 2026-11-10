using Claims.API.Auth;

namespace Claims.API.Tests;

public class PasswordPolicyTests
{
    [Theory] [InlineData(null)] [InlineData("")] [InlineData("1234567")] [InlineData("        ")]
    public void TooShortOrMissing_IsRejected(string? pw) => Assert.NotNull(PasswordPolicy.Validate(pw, "maria"));
    [Fact] public void EightChars_IsAccepted() => Assert.Null(PasswordPolicy.Validate("12345678", "maria"));
    [Fact] public void Over128_IsRejected() => Assert.NotNull(PasswordPolicy.Validate(new string('a', 129), "maria"));
    [Fact] public void Exactly128_IsAccepted() => Assert.Null(PasswordPolicy.Validate(new string('a', 128), "maria"));
    [Fact] public void EqualToUsername_CaseInsensitive_IsRejected() => Assert.NotNull(PasswordPolicy.Validate("MariaSantos", "mariasantos"));
    [Fact] public void SameAsCurrent_IsRejected() => Assert.NotNull(PasswordPolicy.Validate("samepass1", "maria", currentPassword: "samepass1"));
    [Fact] public void NoCompositionRules() => Assert.Null(PasswordPolicy.Validate("aaaaaaaa", "maria"));
}
