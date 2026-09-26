using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace SentriPet
{
    class DesktopApp : Application
    {
        DesktopController controller;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
            Name = AppInfo.Name;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // no lifetime when rendering off-screen (--snapshot)
            var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                controller = new DesktopController(this, desktop);
                controller.Start();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
