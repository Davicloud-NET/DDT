var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.DDT_Host>("ddt-host");

builder.Build().Run();
