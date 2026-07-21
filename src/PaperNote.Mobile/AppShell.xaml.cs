using PaperNote.Mobile.Pages;

namespace PaperNote.Mobile;

public partial class AppShell : Shell
{
    public AppShell(LibraryPage library)
    {
        InitializeComponent();
        Items.Add(new ShellContent
        {
            Title = "资料库",
            Route = "library",
            Content = library
        });
    }

    protected override bool OnBackButtonPressed()
    {
        // Pop pages opened from the library before Android closes the Activity.
        if (Navigation.NavigationStack.Count > 1)
        {
            _ = Navigation.PopAsync();
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
