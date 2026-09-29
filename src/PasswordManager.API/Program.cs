
using Microsoft.EntityFrameworkCore;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.API;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseHttpsRedirection();

        app.MapGet("/", () => Results.Ok(new
        {
            message = "PasswordManager API is running.",
            status = "healthy"
        }));

        app.Run();
    }
}
