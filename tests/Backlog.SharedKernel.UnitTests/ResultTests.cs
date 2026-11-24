using Backlog.SharedKernel.Results;

namespace Backlog.SharedKernel.UnitTests;

/// <summary>
/// The result union (app-defaults ADR 0005): the factories and conversions put
/// the outcome in the case a caller's exhaustive switch reads, and a failure
/// always carries a real error.
/// </summary>
public class ResultTests
{
    private static readonly Error Refused = Error.Validation("thing.refused", "The thing was refused.");

    [Fact]
    public void Success_IsTheOkCase()
    {
        Assert.IsType<Ok>(Result.Success().Value);
    }

    [Fact]
    public void SuccessWithAValue_IsTheOkCaseHoldingIt()
    {
        var ok = Assert.IsType<Ok<int>>(Result.Success(42).Value);

        Assert.Equal(42, ok.Value);
    }

    [Fact]
    public void Failure_IsTheErrorCaseHoldingTheError()
    {
        Assert.Equal(Refused, Assert.IsType<Error>(Result.Failure(Refused).Value));
        Assert.Equal(Refused, Assert.IsType<Error>(Result.Failure<int>(Refused).Value));
    }

    [Fact]
    public void FailureWithNoError_Throws()
    {
        Assert.Throws<ArgumentException>(() => Result.Failure(Error.None));
        Assert.Throws<ArgumentException>(() => Result.Failure<int>(Error.None));
    }

    [Fact]
    public void AValue_ConvertsToTheOkCase()
    {
        Result<string> result = "kept";

        var ok = Assert.IsType<Ok<string>>(result.Value);
        Assert.Equal("kept", ok.Value);
    }

    [Fact]
    public void AnError_ConvertsToTheErrorCase()
    {
        Result plain = Refused;
        Result<string> valued = Refused;

        Assert.Equal(Refused, Assert.IsType<Error>(plain.Value));
        Assert.Equal(Refused, Assert.IsType<Error>(valued.Value));
    }

    [Fact]
    public void AnExhaustiveSwitch_ReadsTheCase()
    {
        Result<int> result = 7;

        var read = result switch
        {
            Ok<int>(var value) => value,
            Error => -1,
        };

        Assert.Equal(7, read);
    }
}
