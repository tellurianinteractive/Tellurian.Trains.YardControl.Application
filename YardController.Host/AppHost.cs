var builder = DistributedApplication.CreateBuilder(args);

var yardController = builder.AddProject<Projects.YardController>("YardController");

builder.Build().Run();
