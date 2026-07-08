using KnowVault.Admin;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAzureBlobContainerClient("uploads");
builder.AddAzureServiceBusClient("messaging");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "KnowVault.Admin");
app.MapUploadEndpoints();

app.Run();