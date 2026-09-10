var builder = DistributedApplication.CreateBuilder(args);

var host = builder.AddProject<Projects.DDT_Host>("ddt-host")
    .WithEnvironment("DDT__Roles", "web");

builder.AddViteApp("ddt-web", "../DDT.Web")
    .WithReference(host)
    .WaitFor(host);

builder.Build().Run();
