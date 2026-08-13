namespace GRCS.Dashboard.Modules.WcsSimulator.Services;

/// <summary>
/// 进程内发布/订阅中心（Singleton，无状态、不依赖 Scoped 服务，可安全用 Singleton）。
/// 用于跨标签页状态变更广播（通过 localStorage storage 事件触发）和跨组件事件通知，
/// 取代各自定时器轮询，从根本上消除多标签页并发渲染卡顿。
///
/// 性能细节：订阅列表变化时（增/删订阅者）重建一次快照数组，Publish 时只读快照、
/// 零分配零拷贝，遍历期间退订也不会抛集合已修改异常。
/// </summary>
public class EventAggregator
{
    private readonly object _lock = new();
    private readonly HashSet<Action<string>> _subscribers = [];
    private Action<string>[] _snapshot = [];

    public void Subscribe(Action<string> handler)
    {
        lock (_lock)
        {
            _subscribers.Add(handler);
            _snapshot = [.. _subscribers];   // 订阅列表变化时重建快照，Publish 期间零分配
        }
    }

    public void Unsubscribe(Action<string> handler)
    {
        lock (_lock)
        {
            _subscribers.Remove(handler);
            _snapshot = [.. _subscribers];
        }
    }

    public void Publish(string eventType)
    {
        Action<string>[] snapshot;
        lock (_lock) { snapshot = _snapshot; }
        foreach (var sub in snapshot)
            try { sub(eventType); } catch { }
    }
}
