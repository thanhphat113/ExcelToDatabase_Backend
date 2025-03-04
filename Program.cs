using ExcelToDB_Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IDatabaseService, DatabaseService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowCredentials()
                  .AllowAnyMethod();
            // policy.WithOrigins("http://192.168.1.46:5173") 
            // .AllowAnyHeader()
            // .AllowCredentials()
            // .AllowAnyMethod();
        });
});


var app = builder.Build();


app.UseCors("AllowSpecificOrigins");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();



app.Run();

