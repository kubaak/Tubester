using Tubester.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWorkerCore(builder.Configuration);
var host = builder.Build();

host.Run();
