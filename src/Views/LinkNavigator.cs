using System;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;

namespace SourceGit.Views
{
    public static class LinkNavigator
    {
        public static async Task OpenAsync(Control owner, string link, KeyModifiers modifiers)
        {
            if (string.IsNullOrWhiteSpace(link))
                return;

            if (modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                Native.OS.OpenBrowser(link);
                return;
            }

            var confirmed = await App.AskConfirmAsync(
                $"Open this link in browser?\n\n{link}\n\nTip: Ctrl+click opens directly.",
                Models.ConfirmButtonType.YesNo);
            if (confirmed)
                Native.OS.OpenBrowser(link);
        }
    }
}
