using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using EventLogTracer.Core.Interfaces;

namespace EventLogTracer.App.Services;

public class DesktopNotifier : IDesktopNotifier
{
    private INotificationManager? _manager;

    public void AttachToWindow(Window window)
    {
        _manager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 5
        };
    }

    public void ShowNotification(string title, string message)
    {
        if (_manager is null)
            return;

        Dispatcher.UIThread.Post(() =>
            _manager.Show(new Notification(
                title,
                message,
                Avalonia.Controls.Notifications.NotificationType.Information)));
    }
}
