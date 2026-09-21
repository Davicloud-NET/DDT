// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

var builder = DistributedApplication.CreateBuilder(args);

var database = builder.AddPostgres("postgres")
    .WithDataVolume()
    .AddDatabase("ddtdb");

var host = builder.AddProject<Projects.DDT_Host>("ddt-host")
    .WithEnvironment("DDT__Roles", "web")
    .WithReference(database)
    .WaitFor(database);

builder.AddViteApp("ddt-web", "../DDT.Web")
    .WithReference(host)
    .WaitFor(host);

builder.Build().Run();
