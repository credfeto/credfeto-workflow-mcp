using System;
using System.Collections.Generic;
using System.Linq;
using Credfeto.Workflow.GitHub.Workflows;
using FunFair.Test.Common;
using Xunit;

namespace Credfeto.Workflow.GitHub.Tests.Workflows;

public sealed class WorkflowDispatchDeclarationTests : TestBase
{
    [Theory]
    [InlineData("on: workflow_dispatch\n")]
    [InlineData("on: \"workflow_dispatch\"\n")]
    [InlineData("on: [push, workflow_dispatch]\n")]
    [InlineData("on: ['push', \"workflow_dispatch\"]\n")]
    [InlineData("\"on\": workflow_dispatch\n")]
    [InlineData("'on': workflow_dispatch\n")]
    [InlineData("on:\n  - push\n  - workflow_dispatch\n")]
    [InlineData("on:\n  push:\n    branches: [main]\n  workflow_dispatch:\n")]
    [InlineData("on:\n  workflow_dispatch: {}\n")]
    [InlineData("on:\n  workflow_dispatch: null\n")]
    [InlineData("on:\n  workflow_dispatch: ~\n")]
    [InlineData("on:\n  workflow_dispatch:\n    inputs: {}\n")]
    [InlineData("\uFEFFname: Build\r\non: # triggers\r\n  workflow_dispatch:\r\njobs:\r\n  build:\r\n")]
    [InlineData("name: \"a \\\" # b\"\non: workflow_dispatch\n")]
    public static void ADeclarationWithoutInputsIsRead(string text)
    {
        IReadOnlySet<string>? inputs = WorkflowDispatchDeclaration.TryRead(text);

        Assert.NotNull(inputs);
        Assert.Empty(inputs);
    }

    [Fact]
    public void InputNamesAreRead()
    {
        const string text = """
            name: Deploy
            on:
              workflow_dispatch:
                inputs:
                  environment:
                    description: "Where # to deploy"
                    required: true
                    type: choice
                    options:
                      - staging
                      - production
                  "dry-run":
                    type: boolean
                  # not an input
              push:
            jobs:
              deploy:
                runs-on: ubuntu-latest
            """;

        IReadOnlySet<string>? inputs = WorkflowDispatchDeclaration.TryRead(text);

        Assert.NotNull(inputs);
        Assert.Equal(
            expected: ["dry-run", "environment"],
            actual: inputs.Order(StringComparer.Ordinal),
            comparer: StringComparer.Ordinal
        );
    }

    [Theory]
    [InlineData("name: Build\n")]
    [InlineData("on: push\n")]
    [InlineData("on: [push, pull_request]\n")]
    [InlineData("on:\n  push:\n  pull_request:\n")]
    [InlineData("on:\n  - push\n")]
    [InlineData("on:\n")]
    [InlineData("on: {workflow_dispatch: {}}\n")]
    [InlineData("on:\n  push:\n    workflow_dispatch:\n")]
    [InlineData("on:\n  # workflow_dispatch:\n  push:\n")]
    [InlineData("jobs:\n  on:\n    workflow_dispatch:\n")]
    [InlineData("on:\n  workflow_dispatch: yes\n")]
    [InlineData("on:\n\tworkflow_dispatch:\n")]
    [InlineData("on:\n    push:\n  workflow_dispatch:\n")]
    [InlineData("on:\n  workflow_dispatch:\n    inputs: {a: {}}\n")]
    [InlineData("on:\n  workflow_dispatch:\n    inputs:\n      - a\n")]
    [InlineData("on:\n  workflow_dispatch:\n    inputs:\n      a:\n      a:\n")]
    [InlineData("on:\n  workflow_dispatch:\n      inputs:\n    push:\n")]
    [InlineData("on:\n  workflow_dispatch:\n    inputs:\n        a:\n      b:\n")]
    [InlineData("on:\n  \"workflow_dispatch:\n")]
    [InlineData("on:\n  \"workflow_dispatch\"x:\n")]
    [InlineData("on:\n  workflow_dispatch\n")]
    [InlineData("on:\n  : workflow_dispatch\n")]
    public static void AnythingElseIsNotADeclaration(string text)
    {
        Assert.Null(WorkflowDispatchDeclaration.TryRead(text));
    }

    [Fact]
    public void OnlyTheFirstOnSectionIsRead()
    {
        Assert.Null(WorkflowDispatchDeclaration.TryRead("on: push\non: workflow_dispatch\n"));
    }
}
