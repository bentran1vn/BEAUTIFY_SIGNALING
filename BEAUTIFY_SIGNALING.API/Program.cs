using BEAUTIFY_SIGNALING.SERVICES.Hub;
using BEAUTIFY_SIGNALING.SERVICES.LiveStream;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(
    options => options.AddDefaultPolicy(
        policy => 
            policy
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowAnyOrigin()));

builder.Services.AddLogging();
builder.Services.AddHttpClient();
builder.Services.AddSignalR();

builder.Services.AddSingleton(new JanusWebSocketManager(""));



var app = builder.Build();

app.UseCors();
app.MapHub<LivestreamHub>("/livestreamHub");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

app.MapControllers();

app.Run();