using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;

namespace PaperNote.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        OnBackPressedDispatcher.AddCallback(this, new NavigationBackCallback(this));
    }

    private sealed class NavigationBackCallback(MainActivity activity) : OnBackPressedCallback(true)
    {
        public override void HandleOnBackPressed()
        {
            if (Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Page is Shell shell
                && shell.Navigation.NavigationStack.Count > 1)
            {
                _ = shell.Navigation.PopAsync();
                return;
            }

            // Put the root task in the background. This behaves like closing the app for
            // the user and avoids tearing down MAUI's service provider before Android
            // finishes destroying Shell fragments.
            activity.MoveTaskToBack(true);
        }
    }
}
