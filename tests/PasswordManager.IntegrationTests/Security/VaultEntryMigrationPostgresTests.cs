using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PasswordManager.Application.DTOs.Authentication;
using PasswordManager.Domain.Entities;
using PasswordManager.Infrastructure.Persistence;

namespace PasswordManager.IntegrationTests.Security;

public sealed class VaultEntryMigrationPostgresTests : IClassFixture<PostgresWebApplicationFactory>
{
    private readonly PostgresWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public VaultEntryMigrationPostgresTests(PostgresWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    [Fact]
    public async Task MigrationPreservesLegacyColumnValuesAndAllowsNewMetadataOnlyRows()
    {
        var email = $"entry-migration-{Guid.NewGuid():N}@vaultx.local";
        const string password = "VaultX@VaultPhase4Migration_2026!";
        using var register = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterUserRequest(email, password, password));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);
        using var login = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginJson.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261006065850_MakeVaultMetadataOnly");

        using var vaultRequest = new HttpRequestMessage(HttpMethod.Post, "/api/vault");
        vaultRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var vaultResponse = await _client.SendAsync(vaultRequest);
        Assert.Equal(HttpStatusCode.Created, vaultResponse.StatusCode);
        using var vaultJson = JsonDocument.Parse(await vaultResponse.Content.ReadAsStringAsync());
        var vaultId = vaultJson.RootElement.GetProperty("id").GetGuid();

        var legacyEntry = new VaultEntry(
            vaultId,
            "Existing entry",
            "legacy-user",
            "https://legacy.example.test",
            "preserve this note");
        var legacyCiphertext = new byte[] { 0, 1, 127, 255 };
        var legacyNonce = new byte[] { 2, 3, 4 };
        var legacyTag = new byte[] { 5, 6, 7, 8 };
        db.VaultEntries.Add(legacyEntry);
        db.Entry(legacyEntry).Property<byte[]?>("EncryptedPassword").CurrentValue = legacyCiphertext;
        db.Entry(legacyEntry).Property<byte[]?>("PasswordNonce").CurrentValue = legacyNonce;
        db.Entry(legacyEntry).Property<byte[]?>("PasswordAuthenticationTag").CurrentValue = legacyTag;
        await db.SaveChangesAsync();

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        var migratedEntry = await db.VaultEntries.SingleAsync(entry => entry.Id == legacyEntry.Id);
        Assert.Equal(legacyCiphertext, db.Entry(migratedEntry)
            .Property<byte[]?>("EncryptedPassword").CurrentValue);
        Assert.Equal(legacyNonce, db.Entry(migratedEntry)
            .Property<byte[]?>("PasswordNonce").CurrentValue);
        Assert.Equal(legacyTag, db.Entry(migratedEntry)
            .Property<byte[]?>("PasswordAuthenticationTag").CurrentValue);
        Assert.Equal("https://legacy.example.test", migratedEntry.WebsiteUrl);

        var metadataEntry = new VaultEntry(vaultId, "Metadata entry", "new-user");
        db.VaultEntries.Add(metadataEntry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var storedMetadataEntry = await db.VaultEntries.SingleAsync(entry => entry.Id == metadataEntry.Id);
        var storedProperties = db.Entry(storedMetadataEntry);
        Assert.Null(storedProperties.Property<byte[]?>("EncryptedPassword").CurrentValue);
        Assert.Null(storedProperties.Property<byte[]?>("PasswordNonce").CurrentValue);
        Assert.Null(storedProperties.Property<byte[]?>("PasswordAuthenticationTag").CurrentValue);
        Assert.Null(storedMetadataEntry.WebsiteUrl);

        await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync("20261006065850_MakeVaultMetadataOnly"));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
