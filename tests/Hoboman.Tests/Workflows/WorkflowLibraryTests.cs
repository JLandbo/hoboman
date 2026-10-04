using System.Text.Json;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string WorkflowsFolder => Path.Combine(_temporary.Path, "workflows");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    WorkflowLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<WorkflowLibrary>.Instance);

    void WriteWorkflow(string name, string json)
    {
        Directory.CreateDirectory(Path.Combine(WorkflowsFolder, name));
        File.WriteAllText(Path.Combine(WorkflowsFolder, name, "workflow.json"), json);
    }

    public void Dispose() => _temporary.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SecretOwnersOf_WhenTheWorkflowHasAuthOrNot_ThenHasItsIdOnlyWithAuth(bool hasAuth)
    {
        // Arrange
        var step = Guid.NewGuid();
        var workflow = new Workflow { Id = Guid.NewGuid(), Auth = hasAuth ? new(AuthKind.Bearer) : null, Steps = [new() { Request = new() { Id = step, Url = "https://dev.local" } }] };

        // Act
        var owners = WorkflowLibrary.SecretOwnersOf(workflow);

        // Assert
        Assert.Equal(hasAuth ? [step, workflow.Id] : [step], owners);
    }

    [Fact]
    public async Task LoadAsync_WhenTheWorkflowWasSaved_ThenGivesItBack()
    {
        // Arrange
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            Parameters = [new("orderId"), new("pageSize") { Default = JsonSerializer.SerializeToElement(new { size = 50, name = "Æble" }) }],
            Variables = [new("token")],
            Steps =
            [
                new()
                {
                    Name = "Login",
                    Request = new()
                    {
                        Id = Guid.NewGuid(),
                        Auth = new(AuthKind.Basic, "{{user}}"),
                        Method = "POST",
                        Url = "https://dev.local/login",
                        Query = [new("page", "2")],
                        Headers = [new("X-Order", "{{orderId}}", false)],
                        BodyKind = BodyKind.Json,
                        Body = "{}",
                        UseEnvironmentVariablesInBody = false,
                        Base64 = new() { Encode = ["$.password"] },
                    },
                    Saves = [new("token", "$.access_token")],
                },
            ],
        };
        await Library().SaveAsync("Ordre-sync", workflow, Cancellation);

        // Act
        var loaded = await Library().LoadAsync("Ordre-sync", Cancellation);

        // Assert
        Assert.Equal(JsonSerializer.Serialize(workflow), JsonSerializer.Serialize(loaded));
    }

    [Fact]
    public async Task LoadAsync_WhenADefaultIsNull_ThenKeepsItAsAValue()
    {
        // Arrange
        WriteWorkflow("Ordre-sync", """{"variables": [{"name": "order", "default": null}]}""");

        // Act
        var loaded = await Library().LoadAsync("Ordre-sync", Cancellation);

        // Assert
        Assert.Equal(JsonValueKind.Null, Assert.Single(loaded!.Variables).Default.ValueKind);
    }

    [Fact]
    public async Task SaveAsync_WhenAValueHasNoDefault_ThenWritesNone()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("token")] };

        // Act
        await Library().SaveAsync("Ordre-sync", workflow, Cancellation);

        // Assert
        Assert.DoesNotContain("default", File.ReadAllText(Path.Combine(WorkflowsFolder, "Ordre-sync", "workflow.json")));
    }

    [Fact]
    public async Task LoadAsync_WhenAStepHasNoRequest_ThenLoadsItWithoutOne()
    {
        // Arrange
        WriteWorkflow("Ordre-sync", """{"steps": [{}]}""");

        // Act
        var loaded = await Library().LoadAsync("Ordre-sync", Cancellation);

        // Assert
        Assert.Null(Assert.Single(loaded!.Steps).Request);
    }

    [Theory]
    [InlineData("../secret.js")]
    [InlineData("..\\secret.js")]
    [InlineData("secret.txt")]
    public async Task LoadScriptAsync_WhenTheNameLeadsOutOfTheFolderOrIsNotJavaScript_ThenGivesNoCode(string script)
    {
        // Arrange
        WriteWorkflow("Ordre-sync", "{}");
        File.WriteAllText(Path.Combine(WorkflowsFolder, "secret.js"), "return 1;");
        File.WriteAllText(Path.Combine(WorkflowsFolder, "Ordre-sync", "secret.txt"), "return 1;");

        // Act
        var code = await Library().LoadScriptAsync("Ordre-sync", script, Cancellation);

        // Assert
        Assert.Null(code);
    }

    [Fact]
    public async Task LoadAsync_WhenTheNameHasASlash_ThenThrows()
    {
        // Act
        var failure = await Record.ExceptionAsync(() => Library().LoadAsync("Shop/Ordre-sync", Cancellation));

        // Assert
        Assert.IsType<ArgumentException>(failure);
    }

    [Fact]
    public async Task NamesAsync_WhenAFolderHasNoWorkflowFile_ThenLeavesItOut()
    {
        // Arrange
        WriteWorkflow("Ordre-sync", "{}");
        Directory.CreateDirectory(Path.Combine(WorkflowsFolder, "Empty"));

        // Act
        var names = await Library().NamesAsync(Cancellation);

        // Assert
        Assert.Equal(["Ordre-sync"], names);
    }

    [Fact]
    public async Task CreateAsync_WhenCalled_ThenSavesAWorkflowWithANewId()
    {
        // Act
        var created = await Library().CreateAsync("Ordre-sync", Cancellation);

        // Assert
        var loaded = await Library().LoadAsync("Ordre-sync", Cancellation);
        Assert.Equal((true, created.Id), (created.Id != Guid.Empty, loaded?.Id));
    }

    [Fact]
    public async Task CreateAsync_WhenTheWorkflowExists_ThenThrows()
    {
        // Arrange
        await Library().CreateAsync("Ordre-sync", Cancellation);

        // Act
        var failure = await Record.ExceptionAsync(() => Library().CreateAsync("Ordre-sync", Cancellation));

        // Assert
        Assert.IsType<IOException>(failure);
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderIsGoneAndMayNotBeCreated_ThenThrows()
    {
        // Act
        var failure = await Record.ExceptionAsync(() => Library().SaveAsync("Ordre-sync", new Workflow(), Cancellation, createDirectory: false));

        // Assert
        Assert.IsType<DirectoryNotFoundException>(failure);
    }

    [Fact]
    public async Task RenameAsync_WhenCalled_ThenMovesTheFolderWithEverythingInIt()
    {
        // Arrange
        WriteWorkflow("Ordre-sync", "{}");
        File.WriteAllText(Path.Combine(WorkflowsFolder, "Ordre-sync", "map.js"), "");

        // Act
        await Library().RenameAsync("Ordre-sync", "Ordre-import", Cancellation);

        // Assert
        Assert.Equal(["Ordre-import"], await Library().NamesAsync(Cancellation));
        Assert.True(File.Exists(Path.Combine(WorkflowsFolder, "Ordre-import", "map.js")));
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesTheFolder()
    {
        // Arrange
        WriteWorkflow("Ordre-sync", "{}");

        // Act
        await Library().DeleteAsync("Ordre-sync", Cancellation);

        // Assert
        Assert.False(Directory.Exists(Path.Combine(WorkflowsFolder, "Ordre-sync")));
    }
}
