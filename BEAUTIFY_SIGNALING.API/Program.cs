using BEAUTIFY_PACKAGES.BEAUTIFY_PACKAGES.PERSISTENCE.DependencyInjection.Options;
using BEAUTIFY_SIGNALING.API.Extensions;
using BEAUTIFY_SIGNALING.REPOSITORY.DependencyInjection.Extensions;
using BEAUTIFY_SIGNALING.SERVICES.Abstractions;
using BEAUTIFY_SIGNALING.SERVICES.Hub;
using BEAUTIFY_SIGNALING.SERVICES.LiveStream;
using BEAUTIFY_SIGNALING.SERVICES.Services.JwtServices;
using BEAUTIFY_SIGNALING.SERVICES.Services.LiveStreams;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInterceptorPersistence();
builder.Services.ConfigureSqlServerRetryOptionsPersistence(
    builder.Configuration.GetSection(nameof(SqlServerRetryOptions)));
builder.Services.AddSqlServerPersistence();
builder.Services.AddRepositoryPersistence();

builder.Services.AddTransient<IJwtServices, JwtServices>();
builder.Services.AddTransient<ILiveStreamServices, LiveStreamServices>();
builder.Services.AddSwaggerServices();

builder.Services.AddControllers();
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

builder.Services.AddSingleton(new JanusWebSocketManager(builder.Configuration.GetValue<string>("JanusWebsocket")!));

var app = builder.Build();

app.UseCors();
app.MapHub<LivestreamHub>("/livestreamHub");

// Configure the HTTP request pipeline.
// if (app.Environment.IsDevelopment())
// {
//     
// }
// app.UseSwagger();
// app.UseSwaggerUI();

app.UseSwaggerAPI();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();