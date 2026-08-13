using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using GRCS.Dashboard;
using GRCS.Dashboard.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// MudBlazor
builder.Services.AddMudServices();

// HttpClient for GRCS API（全局 10s 超时：后端半死不活时不再无限挂起请求）
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromSeconds(10)
});

// Module navigation state (scoped = per-browser-tab)
builder.Services.AddScoped<ModuleNavigationService>();

// WCS Simulator module services
// ── DI 生命周期约定（重要）──
// Blazor WASM 中 AddScoped 的服务在每个浏览器标签页内天然是单例：跨页面导航存活、
// 组件销毁不重置状态，与 Singleton 行为等价。因此凡是依赖其他 Scoped 服务的服务
// 一律注册 AddScoped——注册成 Singleton 会在应用启动时被 DI 容器校验拦截
// （ScopedInSingletonException），页面白屏"An unhandled error has occurred"
// （2026-08-14 曾因此故障：SignalAutoService 曾被误注册为 Singleton）。
// 只有无状态、不依赖任何 Scoped 服务的服务（如 EventAggregator）才用 Singleton。
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.IWcsService,
                           GRCS.Dashboard.Modules.WcsSimulator.Services.MockWcsService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.WcsFlowStateService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.CargoCodeService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.StationLockService>();
builder.Services.AddSingleton<GRCS.Dashboard.Modules.WcsSimulator.Services.EventAggregator>();
// SignalAutoService 依赖 LocalStoreService / TaskLedgerService / MockWcsService 等 Scoped 服务，
// 注册为 Singleton 会触发 ScopedInSingletonException（net8+ 开发环境 ValidateOnBuild）。
// WASM 中 Scoped 本身就是每个标签页一个实例（跨页面导航存活），行为与 Singleton 等价。
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.SignalAutoService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.AutoRunService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.ContainerTaskService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.LocalStoreService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.TaskLedgerService>();
// 任务阶段事件共享轮询器：全应用唯一轮询 task-stages，替代各处各自轮询
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.TaskStageHub>();

await builder.Build().RunAsync();
