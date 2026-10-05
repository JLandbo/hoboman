using System.Text.Json;

namespace Hoboman.Tests.Workflows;

public sealed class WorkflowLibraryTests : IDisposable
{
    readonly TemporaryFolder _temporary = new();

    string WorkflowsFolder => Path.Combine(_temporary.Path, "workflows");

    CancellationToken Cancellation => TestContext.Current.CancellationToken;

    WorkflowLibrary Library() => new(new AppFolder(_temporary.Path), NullLogger<WorkflowLibrary>.Instance);

    // A workflow's folder is named by its id.
    Guid WriteWorkflow(string json)
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(WorkflowsFolder, $"{id}"));
        File.WriteAllText(Path.Combine(WorkflowsFolder, $"{id}", "workflow.json"), json);
        return id;
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
            Name = "Ordre: sync / v2",
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
        await Library().SaveAsync(workflow, Cancellation);

        // Act
        var loaded = await Library().LoadAsync(workflow.Id, Cancellation);

        // Assert
        Assert.Equal(JsonSerializer.Serialize(workflow), JsonSerializer.Serialize(loaded));
    }

    [Fact]
    public async Task LoadAsync_WhenTheIdInTheFileIsAnother_ThenTheFolderNameWins()
    {
        // Arrange
        var id = WriteWorkflow($$"""{"id": "{{Guid.NewGuid()}}", "name": "Ordre-sync"}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equal(id, loaded!.Id);
    }

    [Fact]
    public async Task LoadAsync_WhenADefaultIsNull_ThenKeepsItAsAValue()
    {
        // Arrange
        var id = WriteWorkflow("""{"variables": [{"name": "order", "default": null}]}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Equal(JsonValueKind.Null, Assert.Single(loaded!.Variables).Default.ValueKind);
    }

    [Fact]
    public async Task SaveAsync_WhenAValueHasNoDefault_ThenWritesNone()
    {
        // Arrange
        var workflow = new Workflow { Id = Guid.NewGuid(), Variables = [new("token")] };

        // Act
        await Library().SaveAsync(workflow, Cancellation);

        // Assert
        Assert.DoesNotContain("default", File.ReadAllText(Path.Combine(WorkflowsFolder, $"{workflow.Id}", "workflow.json")));
    }

    [Fact]
    public async Task LoadAsync_WhenAStepHasNoRequest_ThenLoadsItWithoutOne()
    {
        // Arrange
        var id = WriteWorkflow("""{"steps": [{}]}""");

        // Act
        var loaded = await Library().LoadAsync(id, Cancellation);

        // Assert
        Assert.Null(Assert.Single(loaded!.Steps).Request);
    }

    [Theory]
    [InlineData("../secret.js")]
    [InlineData("..\\secret.js")]
    [InlineData("map\\..\\..\\secret.js")]
    [InlineData("secret.txt")]
    public async Task LoadScriptAsync_WhenTheNameLeadsOutOfTheFolderOrIsNotJavaScript_ThenGivesNoCode(string script)
    {
        // Arrange
        var id = WriteWorkflow("{}");
        File.WriteAllText(Path.Combine(WorkflowsFolder, "secret.js"), "return 1;");
        File.WriteAllText(Path.Combine(WorkflowsFolder, $"{id}", "secret.txt"), "return 1;");

        // Act
        var code = await Library().LoadScriptAsync(id, script, Cancellation);

        // Assert
        Assert.Null(code);
    }

    [Fact]
    public async Task ListAsync_WhenAFolderHasNoWorkflowFileOrIsNoId_ThenLeavesItOut()
    {
        // Arrange
        var id = WriteWorkflow("""{"name": "Ordre-sync"}""");
        Directory.CreateDirectory(Path.Combine(WorkflowsFolder, $"{Guid.NewGuid()}"));
        foreach (var other in new[] { "Copy", $"{Guid.NewGuid():B}" })
        {
            Directory.CreateDirectory(Path.Combine(WorkflowsFolder, other));
            File.WriteAllText(Path.Combine(WorkflowsFolder, other, "workflow.json"), """{"name": "Copy"}""");
        }

        // Act
        var workflows = await Library().ListAsync(Cancellation);

        // Assert
        Assert.Equal([(id, "Ordre-sync")], workflows);
    }

    [Fact]
    public async Task ListAsync_WhenAWorkflowCannotBeRead_ThenGivesItWithoutAName()
    {
        // Arrange
        var id = WriteWorkflow("{");

        // Act
        var workflows = await Library().ListAsync(Cancellation);

        // Assert
        Assert.Equal([(id, (string?)null)], workflows);
    }

    [Fact]
    public async Task CreateAsync_WhenCalled_ThenSavesAWorkflowWithANewId()
    {
        // Act
        var created = await Library().CreateAsync("Ordre-sync", Cancellation);

        // Assert
        var loaded = await Library().LoadAsync(created.Id, Cancellation);
        Assert.Equal((true, created.Id, "Ordre-sync"), (created.Id != Guid.Empty, loaded?.Id, loaded?.Name));
    }

    [Fact]
    public async Task CreateAsync_WhenTheNameIsTaken_ThenMakesAnotherWorkflowWithIt()
    {
        // Arrange
        var first = await Library().CreateAsync("Ordre-sync", Cancellation);

        // Act
        var second = await Library().CreateAsync("Ordre-sync", Cancellation);

        // Assert
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(["Ordre-sync", "Ordre-sync"], (await Library().ListAsync(Cancellation)).Select(workflow => workflow.Name));
    }

    [Fact]
    public async Task SaveAsync_WhenTheFolderIsGoneAndMayNotBeCreated_ThenThrows()
    {
        // Act
        var failure = await Record.ExceptionAsync(() => Library().SaveAsync(new Workflow { Id = Guid.NewGuid(), Name = "Ordre-sync" }, Cancellation, createDirectory: false));

        // Assert
        Assert.IsType<DirectoryNotFoundException>(failure);
    }

    [Fact]
    public async Task RenameAsync_WhenCalled_ThenChangesOnlyTheNameAndKeepsTheFolder()
    {
        // Arrange
        var id = WriteWorkflow("""{"name": "Ordre-sync", "parameters": [{"name": "orderId"}]}""");
        File.WriteAllText(Path.Combine(WorkflowsFolder, $"{id}", "map.js"), "");

        // Act
        await Library().RenameAsync(id, "Ordre: import", Cancellation);

        // Assert
        var loaded = await Library().LoadAsync(id, Cancellation);
        Assert.Equal(("Ordre: import", "orderId"), (loaded!.Name, loaded.Parameters.Single().Name));
        Assert.True(File.Exists(Path.Combine(WorkflowsFolder, $"{id}", "map.js")));
    }

    [Fact]
    public async Task DeleteAsync_WhenCalled_ThenRemovesTheFolder()
    {
        // Arrange
        var id = WriteWorkflow("{}");

        // Act
        await Library().DeleteAsync(id, Cancellation);

        // Assert
        Assert.False(Directory.Exists(Path.Combine(WorkflowsFolder, $"{id}")));
    }
}
