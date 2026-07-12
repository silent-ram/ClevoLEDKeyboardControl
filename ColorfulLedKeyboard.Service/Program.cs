using ColorfulLedKeyboard.Service;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = ColorfulLedKeyboard.Core.AppPaths.ServiceName;
});

builder.Services.AddSingleton<ServiceIpcServer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
