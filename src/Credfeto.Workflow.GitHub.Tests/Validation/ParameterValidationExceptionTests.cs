using Credfeto.Workflow.GitHub.Interfaces.Validation;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Validation;

public sealed class ParameterValidationExceptionTests : TestBase
{
    [Fact]
    public void ForSetsParameterNameRuleAndMessage()
    {
        ParameterValidationException exception = ParameterValidationException.For(
            parameterName: "repo",
            rule: "it must be valid"
        );

        Assert.Equal(expected: "repo", actual: exception.ParameterName);
        Assert.Equal(expected: "it must be valid", actual: exception.Rule);
        Assert.Equal(expected: "Parameter 'repo' is not valid: it must be valid", actual: exception.Message);
    }

    [Fact]
    public void DefaultConstructedHasEmptyParameterNameAndRule()
    {
        ParameterValidationException exception = new();

        Assert.Equal(expected: string.Empty, actual: exception.ParameterName);
        Assert.Equal(expected: string.Empty, actual: exception.Rule);
    }
}
