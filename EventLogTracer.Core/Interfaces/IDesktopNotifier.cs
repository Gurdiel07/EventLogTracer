namespace EventLogTracer.Core.Interfaces;

public interface IDesktopNotifier
{
    void ShowNotification(string title, string message);
}
