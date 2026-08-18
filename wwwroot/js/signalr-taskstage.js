// SignalR 任务阶段事件桥（TaskStageHub 的 JS 侧）。
// 连接 WCS 后端 /hubs/task-stages，把推送事件回调到 .NET（TaskStageHub）。
// 依赖 js/signalr.min.js（@microsoft/signalr 浏览器 bundle）。
window.grcsTaskStage = (() => {
    let connection = null;
    let ref = null;
    let started = false;

    function connect(hubUrl) {
        if (started) return;
        started = true;
        connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect([0, 2000, 5000, 10000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        // 单条新事件（后端 Record 时广播）
        connection.on('EventAdded', (evt) => { if (ref) ref.invokeMethodAsync('OnEventAdded', evt); });
        // 全量快照（连接建立时/清空后，用于初始化或对账）
        connection.on('EventsReset', (evts) => { if (ref) ref.invokeMethodAsync('OnEventsReset', evts); });
        // 单任务删除（其它标签页删除后同步本地缓存）
        connection.on('TaskRemoved', (taskId) => { if (ref) ref.invokeMethodAsync('OnTaskRemoved', taskId); });

        connection.onreconnecting(() => { if (ref) ref.invokeMethodAsync('OnStateChanged', 'reconnecting'); });
        connection.onreconnected(() => { if (ref) ref.invokeMethodAsync('OnStateChanged', 'connected'); });
        connection.onclose(() => { if (ref) ref.invokeMethodAsync('OnStateChanged', 'disconnected'); });

        connection.start()
            .then(() => { if (ref) ref.invokeMethodAsync('OnStateChanged', 'connected'); })
            .catch((err) => { console.error('grcsTaskStage start failed:', err); if (ref) ref.invokeMethodAsync('OnStateChanged', 'disconnected'); });
    }

    function disconnect() {
        if (connection) {
            const c = connection;
            connection = null;
            started = false;
            c.stop().catch(() => {});
        }
    }

    return {
        setRef: (r) => { ref = r; },
        connect: connect,
        disconnect: disconnect
    };
})();