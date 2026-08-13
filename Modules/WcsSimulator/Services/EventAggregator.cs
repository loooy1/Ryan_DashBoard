namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 进程内发布/订阅中心（Singleton）。
/// 用于跨标签页状态变更广播（通过 localStorage storage 事件触发）和跨组件事件通知，
/// 取代各自定时器轮询，从根本上消除多标签页并发渲染卡顿。
/// </summary>
public class EventAggregator
{
    private readonly object _lock = new();
    private readonly HashSet<Action<string>> _subscribers = [];

    public void Subscribe(Action<string> handler)
    {
        lock (_lock) { _subscribers.Add(handler); }
    }

    public void Unsubscribe(Action<string> handler)
    {
        lock (_lock) { _subscribers.Remove(handler); }
    }

    public void Publish(string eventType)
    {
        lock (_lock)
        {
            foreach (var sub in _subscribers.ToList())
                try { sub(eventType); } catch { }
        }
    }
}
