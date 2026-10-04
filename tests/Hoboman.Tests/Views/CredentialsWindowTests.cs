using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Hoboman.Tests.ViewModels;

namespace Hoboman.Tests.Views;

[Collection("Ui")]
public sealed class CredentialsWindowTests
{
    [Fact]
    public async Task CredentialsWindow_WhenTheClientIdIsCopied_ThenTheClipboardHasIt()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var window = await ShowAsync(harness);

            // Act
            Ui.Click(Ui.Descendants<Button>(window).Single(button => button.IsVisible && button.Tag as string == "docs-client"));
            await Ui.IdleAsync();

            // Assert
            Assert.Equal("docs-client", harness.Clipboard.Held);
        });
    }

    [Fact]
    public async Task CredentialsWindow_WhenSaved_ThenStaysOpen()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var window = await ShowAsync(harness);
            harness.CredentialEditor.Selected!.Name = "Acies Docs dev";

            // Act
            Ui.Click(Ui.Descendants<Button>(window).Single(button => button.IsDefault));
            await Ui.UntilAsync(() => harness.Credentials.All.Any(choice => choice.Credential.Name == "Acies Docs dev"));

            // Assert
            Assert.True(window.IsVisible);
        });
    }

    [Fact]
    public async Task CredentialsWindow_WhenShown_ThenACredentialCanBeNeitherNoneNorInherited()
    {
        using var harness = new Harness();
        await harness.SaveCredentialsAsync();
        await Ui.RunAsync(async () =>
        {
            // Act
            var window = await ShowAsync(harness);

            // Assert
            var kinds = Ui.Descendants<RadioButton>(window).Where(choice => choice.IsVisible && choice.Content is "Basic" or "Bearer token" or "OAuth 2.0" or "None" or "Inherit from folder")
                .Select(choice => (string)choice.Content);
            Assert.Equal(["Basic", "Bearer token", "OAuth 2.0"], kinds);
        });
    }

    [Fact]
    public async Task MainWindow_WhenCredentialsIsClicked_ThenTheCredentialsAreShown()
    {
        using var harness = new Harness();
        await Ui.RunAsync(async () =>
        {
            // Arrange
            var window = await Ui.ShowAsync(harness, harness.Main());

            // Act
            Ui.Click(Ui.Descendants<Button>(window).Single(button => AutomationProperties.GetName(button) == "Credentials"));
            await Ui.UntilAsync(() => harness.Dialogs.Shown is not null);

            // Assert
            Assert.Same(harness.CredentialEditor, harness.Dialogs.Shown);
        });
    }

    static async Task<CredentialsWindow> ShowAsync(Harness harness)
    {
        await Ui.ShowAsync(harness, harness.Main());
        await harness.CredentialEditor.LoadAsync(TestContext.Current.CancellationToken);
        var window = new CredentialsWindow(harness.CredentialEditor);
        Ui.Show(window);
        await Ui.IdleAsync();
        return window;
    }
}
