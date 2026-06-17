var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgis/postgis", "17-3.5")
    .WithDataVolume();

var taxidb = postgres.AddDatabase("taxidb");

builder.AddProject<Projects.Taxi_Web_Api>("api")
    .WithReference(taxidb)
    .WaitFor(taxidb);

builder.Build().Run();
