namespace PaperNote.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell)
    {
        InitializeComponent();
        _shell = shell;
        UserAppTheme = AppTheme.Light;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "PaperNote" };
        return window;
    }
}
