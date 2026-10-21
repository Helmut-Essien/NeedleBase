using NeedleBase.Application.Contracts.Errors;

namespace NeedleBase.Application.Tests;

/// <summary>G6: clients read ProblemDetails and map stable codes to friendly copy.</summary>
public class AuthErrorMessagesTests
{
    [Fact]
    public void Parse_ReadsProblemDetailsMembers()
    {
        var problem = ApiProblem.Parse(400, """
            {"type":"x","title":"One or more validation errors occurred.","status":400,
             "code":"VALIDATION_FAILED","errors":{"Email":["Enter a valid email."]}}
            """);

        Assert.Equal(400, problem.Status);
        Assert.Equal("VALIDATION_FAILED", problem.Code);
        Assert.Equal("Enter a valid email.", problem.FirstValidationError);
        Assert.Equal("Enter a valid email.", AuthErrorMessages.ForRegister(problem));
    }

    [Theory]
    [InlineData("INVALID_CREDENTIALS", "Wrong email or password")]
    [InlineData("ACCOUNT_LOCKED", "Wait a few minutes")]
    [InlineData("LOGIN_THROTTLED", "Wait a few minutes")]
    [InlineData("ACCOUNT_DEACTIVATED", "turned off")]
    [InlineData("NO_MEMBERSHIP", "isn't linked to a business")]
    public void ForLogin_MapsKnownCodes(string code, string expectedFragment)
    {
        var problem = ApiProblem.Parse(401, $$"""{"status":401,"title":"Couldn't sign you in","code":"{{code}}"}""");
        Assert.Contains(expectedFragment, AuthErrorMessages.ForLogin(problem));
    }

    [Fact]
    public void ForRegister_RegisterFailed_DoesNotRevealWhetherEmailExists()
    {
        var problem = ApiProblem.Parse(400, """{"status":400,"code":"REGISTER_FAILED"}""");
        var message = AuthErrorMessages.ForRegister(problem);
        Assert.Contains("sign in instead", message);
        Assert.DoesNotContain("already registered", message);
    }

    [Theory]
    [InlineData(500, "", AuthErrorMessages.ServerError)]
    [InlineData(429, "", AuthErrorMessages.RateLimited)]
    [InlineData(401, "not json", AuthErrorMessages.LoginFallback)]
    [InlineData(401, """{"code":"SOMETHING_NEW"}""", AuthErrorMessages.LoginFallback)]
    public void ForLogin_FallsBackToGenericText(int status, string body, string expected)
    {
        Assert.Equal(expected, AuthErrorMessages.ForLogin(ApiProblem.Parse(status, body)));
    }
}
