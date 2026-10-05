using System.Windows.Automation;
using System.Windows.Controls;
using Hoboman.Tests.ViewModels;
using Hoboman.Tests.Workflows;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class WorkflowAuthRefreshViewTests
{
    [Fact]
    public async Task WorkflowView_WhenTheWorkflowHasOAuth_ThenItsAuthAndTheStepThatInheritsHaveTheRefreshButton()
    {
        using var harness = new Harness();
        await SaveAsync(harness);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);

            // Act
            await main.OpenWorkflowAsync("Flow");
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());

            // Assert
            Assert.Equal(2, Buttons(harness, window).Count);
        });
    }

    [Fact]
    public async Task WorkflowView_WhenTheStepsRefreshIsClicked_ThenTheWorkflowsTokenIsFetched()
    {
        using var harness = new Harness();
        await SaveAsync(harness);
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var main = harness.Main();
            await main.LoadAsync();
            var window = await Ui.ShowAsync(harness, main);
            await main.OpenWorkflowAsync("Flow");
            await Ui.UntilAsync(() => Ui.Descendants<WorkflowView>(window).Any());

            // Act
            Ui.Click(Buttons(harness, window).Single(button => button.DataContext is WorkflowStepViewModel));
            await Ui.UntilAsync(() => main.Workflow!.Auth.AccessToken is not null);

            // Assert
            Assert.NotNull(harness.OAuth.Asked);
        });
    }

    static Task SaveAsync(Harness harness) => harness.WorkflowLibrary.SaveAsync("Flow", new()
    {
        Id = Guid.NewGuid(),
        Auth = new(AuthKind.OAuth2, OAuth: new() { TokenUrl = "https://dev.local/token", ClientId = "client" }),
        Steps = [new() { Name = "Ping", Request = new() { Url = "https://dev.local/ping", Auth = new(AuthKind.Inherit) } }],
    }, TestContext.Current.CancellationToken);

    static List<Button> Buttons(Harness harness, System.Windows.Window window) =>
        [.. Ui.Descendants<Button>(Ui.Descendants<WorkflowView>(window).Single())
            .Where(button => button.IsVisible && AutomationProperties.GetName(button) == harness.Translator.Format("OAuth.Reauthenticate", "Flow", harness.Translator.Of("Environment.None")))];
}
