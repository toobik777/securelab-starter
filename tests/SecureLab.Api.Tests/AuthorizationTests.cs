using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

/// <summary>Сценарії ЛР 4 (A-01…A-12): контроль доступу до Incident через HTTP-межу.</summary>
public sealed class AuthorizationTests(SecureLabApiFactory factory, Xunit.Abstractions.ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private static readonly Guid CommentAlicePublic = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid CommentBob = Guid.Parse("30000000-0000-0000-0000-000000000002");
    private static readonly Guid CommentInternal = Guid.Parse("30000000-0000-0000-0000-000000000003");

    // ------------------------------------------------------------------ допоміжні методи

    private Task<HttpClient> As(string userName) => factory.LoginAsync(userName, TestPasswords.Seed);

    private async Task<Guid> CreateAsync(HttpClient client, string label)
    {
        using var response = await client.PostAsJsonAsync("/api/incidents", new
        {
            title = $"{label}-{Guid.NewGuid():N}",
            description = "Навчальний запис для перевірки контролю доступу, довший за сорок символів.",
            severity = "Low",
            occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Incident?> ReadAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        return await db.Incidents.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id);
    }

    private async Task CleanupAsync(params Guid[] ids)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => ids.Contains(item.Id)).ExecuteDeleteAsync();
    }

    /// <summary>Status, Content-Type і тіло без полів кореляції (traceId, requestId).</summary>
    private static async Task<(HttpStatusCode Status, string? ContentType, string Body)> ShapeAsync(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, "");
        }

        using var json = JsonDocument.Parse(raw);
        var stable = json.RootElement.EnumerateObject()
            .Where(property => property.Name is not ("traceId" or "requestId"))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => $"{property.Name}={property.Value}");
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, string.Join("|", stable));
    }

    // ------------------------------------------------------------------ A-01

    [Fact]
    public async Task A01_Anonymous_ProtectedRoutes_Return401_NotHtmlRedirect()
    {
        using var client = factory.Anonymous();
        var routes = new[]
        {
            $"/api/incidents/{DbSeeder.AliceIncidentId}",
            "/api/incidents",
            "/api/incidents/severity-summary",
            "/api/incidents/search?q=a",
            $"/api/incident-comments/{CommentAlicePublic}"
        };

        foreach (var route in routes)
        {
            using var response = await client.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        }

        using var mutation = await client.PostAsync($"/api/incidents/{DbSeeder.AliceIncidentId}/triage", null);
        Assert.Equal(HttpStatusCode.Unauthorized, mutation.StatusCode);
    }

    // ------------------------------------------------------------------ A-02 / A-03 / A-03R

    [Fact]
    public async Task A02_Alice_ReadsOwnIncident_Returns200()
    {
        using var alice = await As("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(DbSeeder.AliceIncidentId, json.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Аліса Коваль", json.RootElement.GetProperty("ownerDisplayName").GetString());
    }

    [Fact]
    public async Task A02_Alice_UpdatesOwnIncident_ChangesOnlyAllowedContent()
    {
        using var alice = await As("alice");
        var id = await CreateAsync(alice, "A02-update");
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}",
                new { title = "Оновлений власником заголовок", severity = "Medium" });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await ReadAsync(id);
            Assert.Equal("Оновлений власником заголовок", after!.Title);
            Assert.Equal(IncidentSeverity.Medium, after.Severity);
            Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
            Assert.Equal(IncidentStatus.New, after.Status);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A03_Alice_ReadsBobIncident_Returns404_AndNoBobData()
    {
        using var alice = await As("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Невідома спроба входу", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Боб Мельник", body, StringComparison.Ordinal);
        Assert.DoesNotContain(DbSeeder.BobIncidentId.ToString(), body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A03R_Morgan_Analyst_ReadsAliceIncident_AndSeesInternalComment()
    {
        using var morgan = await As("morgan");
        using var response = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var commentIds = json.RootElement.GetProperty("comments").EnumerateArray()
            .Select(comment => comment.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(CommentAlicePublic, commentIds);
        Assert.Contains(CommentInternal, commentIds); // Analyst: internal comment дозволений матрицею
    }

    [Fact]
    public async Task A03R_Alice_Reporter_DoesNotSeeInternalCommentInDetails()
    {
        using var alice = await As("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var commentIds = json.RootElement.GetProperty("comments").EnumerateArray()
            .Select(comment => comment.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(CommentAlicePublic, commentIds);
        Assert.DoesNotContain(CommentInternal, commentIds);
    }

    [Fact]
    public async Task Administrator_HasNoImplicitAccess_ToForeignIncident()
    {
        using var admin = await As("admin");
        using var response = await admin.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ A-04 (update / delete чужого)

    [Fact]
    public async Task A04_Update_AsNonOwner_ReturnsNotFound_AndDoesNotChangeResource()
    {
        using var alice = await As("alice");
        using var bob = await As("bob");
        var id = await CreateAsync(bob, "A04-update");
        try
        {
            var before = await ReadAsync(id);
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}",
                new { title = "HACKED by alice", description = "змінено чужим", severity = "Critical" });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var after = await ReadAsync(id);
            Assert.Equal(before!.Title, after!.Title);
            Assert.Equal(before.Description, after.Description);
            Assert.Equal(before.Severity, after.Severity);
            Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
            Assert.Equal(DbSeeder.BobId, after.OwnerUserId);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A04_Delete_AsNonOwner_ReturnsNotFound_AndResourceStillExists()
    {
        using var alice = await As("alice");
        using var bob = await As("bob");
        var id = await CreateAsync(bob, "A04-delete");
        try
        {
            using var response = await alice.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.NotNull(await ReadAsync(id));
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task Delete_AsOwner_New_Returns204_ButAfterTriageReturns403()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");
        var removable = await CreateAsync(alice, "Del-own");
        var triaged = await CreateAsync(alice, "Del-triaged");
        try
        {
            using (var ok = await alice.DeleteAsync($"/api/incidents/{removable}"))
                Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
            Assert.Null(await ReadAsync(removable));

            using (var triage = await morgan.PostAsync($"/api/incidents/{triaged}/triage", null))
                Assert.Equal(HttpStatusCode.OK, triage.StatusCode);
            using var denied = await alice.DeleteAsync($"/api/incidents/{triaged}");
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.NotNull(await ReadAsync(triaged));
        }
        finally { await CleanupAsync(removable, triaged); }
    }

    // ------------------------------------------------------------------ A-05 (list)

    [Fact]
    public async Task A05_List_AsAlice_ContainsOnlyOwnedIncidents()
    {
        using var alice = await As("alice");
        var list = await alice.GetFromJsonAsync<List<JsonElement>>("/api/incidents");

        Assert.NotNull(list);
        var ids = list!.Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(DbSeeder.AliceIncidentId, ids);
        Assert.DoesNotContain(DbSeeder.BobIncidentId, ids);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var owners = await db.Incidents.AsNoTracking().Where(item => ids.Contains(item.Id))
            .Select(item => item.OwnerUserId).Distinct().ToListAsync();
        Assert.Equal(new[] { DbSeeder.AliceId }, owners);
    }

    [Fact]
    public async Task A05_List_AsAnalyst_ContainsIncidentsOfSeveralOwners()
    {
        using var morgan = await As("morgan");
        var list = await morgan.GetFromJsonAsync<List<JsonElement>>("/api/incidents");
        var ids = list!.Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(DbSeeder.AliceIncidentId, ids);
        Assert.Contains(DbSeeder.BobIncidentId, ids);
    }

    // ------------------------------------------------------------------ A-06 / A-07 / A-09

    [Fact]
    public async Task A06_Triage_AsReporter_OwnIncident_Returns403_AndStatusUnchanged()
    {
        using var alice = await As("alice");
        var id = await CreateAsync(alice, "A06");
        try
        {
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(IncidentStatus.New, (await ReadAsync(id))!.Status);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A06_Triage_AsReporter_ForeignIncident_ReturnsNotFound_AndStatusUnchanged()
    {
        using var alice = await As("alice");
        using var bob = await As("bob");
        var id = await CreateAsync(bob, "A06-foreign");
        try
        {
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(IncidentStatus.New, (await ReadAsync(id))!.Status);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A07_Triage_AsAnalyst_NewIncident_MovesToTriaged_AndRecordsHistory()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");
        var id = await CreateAsync(alice, "A07");
        try
        {
            using var response = await morgan.PostAsync($"/api/incidents/{id}/triage", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(IncidentStatus.Triaged, (await ReadAsync(id))!.Status);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var history = await db.IncidentStatusHistory.AsNoTracking().SingleAsync(item => item.IncidentId == id);
            Assert.Equal(DbSeeder.MorganId, history.ChangedByUserId);
            Assert.Equal(IncidentStatus.New, history.OldStatus);
            Assert.Equal(IncidentStatus.Triaged, history.NewStatus);

            // Повторний перехід: право є, але поточний status не New -> 409, стан не змінюється.
            using var again = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A07_Analyst_HasNoRight_ToUpdateOrDelete_ForeignContent()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");
        var id = await CreateAsync(alice, "A07-least-privilege");
        try
        {
            var before = await ReadAsync(id);
            using var update = await morgan.PutAsJsonAsync($"/api/incidents/{id}", new { title = "змінено аналітиком" });
            using var delete = await morgan.DeleteAsync($"/api/incidents/{id}");

            Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            var after = await ReadAsync(id);
            Assert.Equal(before!.Title, after!.Title);
        }
        finally { await CleanupAsync(id); }
    }

    [Fact]
    public async Task A09_DeniedTriage_WritesSafeAuditEvent_WithSameTraceIdAsResponse()
    {
        using var alice = await As("alice");
        var id = await CreateAsync(alice, "A09");
        try
        {
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var traceId = json.RootElement.GetProperty("traceId").GetString()!;

            var audit = CapturedLogs.Snapshot().Where(line => line.StartsWith("authorization.denied", StringComparison.Ordinal)
                && line.Contains(id.ToString(), StringComparison.Ordinal)).ToArray();
            var line = Assert.Single(audit);
            output.WriteLine(line);
            Assert.Contains($"subject={DbSeeder.AliceId}", line);
            Assert.Contains("action=incident.triage", line);
            Assert.Contains($"resource=incident:{id}", line);
            Assert.Contains("result=denied", line);
            Assert.Contains("occurredAtUtc=", line);
            Assert.Contains($"traceId={traceId}", line);
            foreach (var forbidden in new[] { TestPasswords.Seed, ".AspNetCore.Identity", "Cookie", "password", "AQAAAA" })
            {
                Assert.DoesNotContain(forbidden, line, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { await CleanupAsync(id); }
    }

    // ------------------------------------------------------------------ A-08a (overposting)

    [Fact]
    public async Task A08a_Update_IgnoresForgedOwnerRoleAndStatus_ButChangesAllowedContent()
    {
        using var alice = await As("alice");
        var id = await CreateAsync(alice, "A08a");
        try
        {
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}", new
            {
                title = "Допустимий новий заголовок",
                status = "Closed",
                ownerUserId = DbSeeder.BobId,
                role = "Administrator",
                id = Guid.NewGuid()
            });

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await ReadAsync(id);
            Assert.NotNull(after); // Id не підмінено
            Assert.Equal("Допустимий новий заголовок", after!.Title);
            Assert.Equal(DbSeeder.AliceId, after.OwnerUserId);
            Assert.Equal(IncidentStatus.New, after.Status);

            // Роль у body не вплинула: Alice і далі не Analyst.
            using var triage = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, triage.StatusCode);
        }
        finally { await CleanupAsync(id); }
    }

    // ------------------------------------------------------------------ A-10 (нерозрізнюваність 404)

    [Fact]
    public async Task A10_ForeignAndUnknownIds_GiveIdenticalResponses_ForReadUpdateDeleteTriage()
    {
        using var alice = await As("alice");
        var unknown = Guid.NewGuid();

        async Task AssertSameAsync(Func<Guid, Task<HttpResponseMessage>> call)
        {
            using var foreign = await call(DbSeeder.BobIncidentId);
            using var missing = await call(unknown);
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            Assert.Equal(await ShapeAsync(missing), await ShapeAsync(foreign));
        }

        await AssertSameAsync(id => alice.GetAsync($"/api/incidents/{id}"));
        await AssertSameAsync(id => alice.PutAsJsonAsync($"/api/incidents/{id}", new { title = "x" }));
        await AssertSameAsync(id => alice.DeleteAsync($"/api/incidents/{id}"));
        await AssertSameAsync(id => alice.PostAsync($"/api/incidents/{id}/triage", null));

        // Те саме для child: чужий коментар і неіснуючий id.
        using var foreignComment = await alice.GetAsync($"/api/incident-comments/{CommentBob}");
        using var missingComment = await alice.GetAsync($"/api/incident-comments/{unknown}");
        Assert.Equal(HttpStatusCode.NotFound, foreignComment.StatusCode);
        Assert.Equal(await ShapeAsync(missingComment), await ShapeAsync(foreignComment));
    }

    // ------------------------------------------------------------------ A-11 (search, severity-summary)

    [Fact]
    public async Task A11_Search_AsAlice_DoesNotReturnBobIncident_ButAnalystDoes()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");

        var aliceHits = await alice.GetFromJsonAsync<List<JsonElement>>("/api/incidents/search?q=O%27Brien");
        var morganHits = await morgan.GetFromJsonAsync<List<JsonElement>>("/api/incidents/search?q=O%27Brien");

        Assert.Empty(aliceHits!);
        Assert.NotEmpty(morganHits!); // seed: інцидент Bob з O'Brien у title
    }

    [Fact]
    public async Task A11_SeveritySummary_AsAlice_CountsOnlyOwnIncidents()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");
        var aliceSummary = await alice.GetFromJsonAsync<List<JsonElement>>("/api/incidents/severity-summary");
        var morganSummary = await morgan.GetFromJsonAsync<List<JsonElement>>("/api/incidents/severity-summary");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var ownCount = await db.Incidents.CountAsync(item => item.OwnerUserId == DbSeeder.AliceId);
        var allCount = await db.Incidents.CountAsync();

        int Sum(List<JsonElement> summary) => summary.Sum(item => item.GetProperty("count").GetInt32());
        Assert.Equal(ownCount, Sum(aliceSummary!));
        Assert.Equal(allCount, Sum(morganSummary!));
        Assert.True(Sum(aliceSummary!) < Sum(morganSummary!));
    }

    // ------------------------------------------------------------------ A-12 (child + internal comment)

    [Fact]
    public async Task A12_Child_Comment_OfForeignIncident_ReturnsNotFound_ForReporter()
    {
        using var alice = await As("alice");
        using var response = await alice.GetAsync($"/api/incident-comments/{CommentBob}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("журналу автентифікації", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A12_InternalComment_NotVisibleToReporterOwner_ButVisibleToAnalyst()
    {
        using var alice = await As("alice");
        using var morgan = await As("morgan");

        using var reporter = await alice.GetAsync($"/api/incident-comments/{CommentInternal}");
        using var analyst = await morgan.GetAsync($"/api/incident-comments/{CommentInternal}");
        using var publicOwn = await alice.GetAsync($"/api/incident-comments/{CommentAlicePublic}");

        Assert.Equal(HttpStatusCode.NotFound, reporter.StatusCode);
        Assert.DoesNotContain("Внутрішня примітка", await reporter.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, analyst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, publicOwn.StatusCode);
    }
}
