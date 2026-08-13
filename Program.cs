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

// HttpClient for GRCS API
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Module navigation state (scoped = per-browser-tab)
builder.Services.AddScoped<ModuleNavigationService>();

// WCS Simulator module services
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.IWcsService,
                           GRCS.Dashboard.Modules.WcsSimulator.Services.MockWcsService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.WcsFlowStateService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.CargoCodeService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.StationLockService>();
builder.Services.AddSingleton<GRCS.Dashboard.Modules.WcsSimulator.Services.EventAggregator>();
builder.Services.AddSingleton<GRCS.Dashboard.Modules.WcsSimulator.Services.SignalAutoService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.AutoRunService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.ContainerTaskService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.LocalStoreService>();
builder.Services.AddScoped<GRCS.Dashboard.Modules.WcsSimulator.Services.TaskLedgerService>();

await builder.Build().RunAsync();
