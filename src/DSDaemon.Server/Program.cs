using DSDaemon.Server.Services;
using DSDaemon.Server.Sessions;
using Microsoft.AspNetCore.Server.Kestrel.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<SessionManager>();

// Plain HTTP/2 (no TLS) is fine for local dev only. The design goal here is
// agents connecting over an untrusted network, so a real deployment must
// configure a certificate (ListenAnyIP(..., o => o.UseHttps(...))) and swap
// agent auth from the placeholder token check in SessionRegistryService to
// something real before exposing this beyond localhost.
builder.WebHost.ConfigureKestrel(options => {
    options.ListenAnyIP(5270, o => o.Protocols = HttpProtocols.Http2);
});

var app = builder.Build();

app.MapGrpcService<SessionRegistryService>();
app.MapGrpcService<DispatcherBridgeService>();
app.MapGet("/", () =>
    "DSDaemon.Server — gRPC bridge for DSDaemon agents. Connect with a gRPC client, not a browser.");

app.Run();
