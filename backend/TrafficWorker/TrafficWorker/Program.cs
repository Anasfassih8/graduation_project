
using TrafficWorker.services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<TrafficLightService>();

var host = builder.Build();
host.Run();
