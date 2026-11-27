using Api.Data;
using Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Api.Services;

/// <summary>
/// Development-only demo data behind the AppHost's "Seed sample data" and "Simulate traffic" dashboard commands.
/// Simulated work goes through <see cref="RequestQueue"/>, so it produces the same spans, metrics and logs as the UI.
/// </summary>
public sealed class QueueSimulator(ServiceDeskDbContext db, RequestQueue queue, ILogger<QueueSimulator> logger)
{
    private static readonly string[] Agents = ["Ava Thompson", "Kenji Watanabe", "Noah Bennett", "Grace Okafor"];

    private static readonly string[] Employees =
    [
        "Hannah Lee", "Raj Mehta", "Lena Fischer", "Omar Haddad", "Chloe Martin",
        "Tomás Silva", "Maya Johnson", "Ethan Brooks", "Sarah Kim", "Diego Ramos"
    ];

    private static readonly (string Title, RequestCategory Category)[] IncomingRequests =
    [
        ("Laptop battery drains in under two hours", RequestCategory.Hardware),
        ("Docking station doesn't detect the second monitor", RequestCategory.Hardware),
        ("Loaner laptop L-0457 is missing keyboard keys", RequestCategory.Hardware),
        ("Excel crashes when opening the FY26 budget workbook", RequestCategory.Software),
        ("Request a Figma license for the brand team", RequestCategory.Software),
        ("Zoom add-in missing from Outlook", RequestCategory.Software),
        ("Password expired while traveling; can't sign in", RequestCategory.Access),
        ("Add a contractor to the Finance SharePoint site", RequestCategory.Access),
        ("Need read access to the production Grafana dashboards", RequestCategory.Access),
        ("Guest Wi-Fi portal doesn't load in the lobby", RequestCategory.Network),
        ("Slow file transfers to the London office share", RequestCategory.Network),
        ("Can't reach the intranet over VPN", RequestCategory.Network)
    ];

    public async Task<SeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        var now = queue.UtcNow;

        ServiceRequest Request(string title, RequestCategory category, RequestPriority priority, string requestedBy,
            double hoursAgo, string? assignedTo = null, double? resolvedHoursAgo = null)
        {
            var createdAt = now.AddHours(-hoursAgo);
            return new ServiceRequest
            {
                Title = title,
                Category = category,
                Priority = priority,
                RequestedBy = requestedBy,
                AssignedTo = assignedTo,
                Status = resolvedHoursAgo is not null ? RequestStatus.Resolved : assignedTo is not null ? RequestStatus.InProgress : RequestStatus.New,
                CreatedAt = createdAt,
                DueAt = createdAt + ServiceRequest.SlaFor(priority),
                ResolvedAt = resolvedHoursAgo is { } hours ? now.AddHours(-hours) : null
            };
        }

        List<ServiceRequest> requests =
        [
            Request("Payroll app returns 500 when submitting timesheets", RequestCategory.Software, RequestPriority.Critical, "Lena Fischer", 2.5, "Kenji Watanabe"),
            Request("Replace failing SSD in build agent BLD-07", RequestCategory.Hardware, RequestPriority.Critical, "Raj Mehta", 5),
            Request("VPN disconnects every 10 minutes when working from home", RequestCategory.Network, RequestPriority.High, "Hannah Lee", 6, "Ava Thompson"),
            Request("Reset MFA for Sarah Kim after a phone replacement", RequestCategory.Access, RequestPriority.High, "Sarah Kim", 1.2),
            Request("Teams calls drop audio in the Seattle office", RequestCategory.Network, RequestPriority.High, "Omar Haddad", 9.5),
            Request(@"Shared drive \\fs01\finance is read-only for the AP team", RequestCategory.Access, RequestPriority.High, "Chloe Martin", 3, "Noah Bennett"),
            Request("Laptop won't join office Wi-Fi after the latest Windows update", RequestCategory.Network, RequestPriority.Medium, "Tomás Silva", 4),
            Request("New hire setup: laptop and accounts for Diego Ramos", RequestCategory.Access, RequestPriority.Medium, "Maya Johnson", 20, "Grace Okafor"),
            Request("Outlook keeps prompting for a password", RequestCategory.Software, RequestPriority.Medium, "Ethan Brooks", 2),
            Request("Projector in conference room 4B shows no signal", RequestCategory.Hardware, RequestPriority.Medium, "Maya Johnson", 26),
            Request("Power BI workspace access for the Q4 sales report", RequestCategory.Access, RequestPriority.Low, "Lena Fischer", 30),
            Request("Install Visual Studio 2026 on workstation DEV-221", RequestCategory.Software, RequestPriority.Low, "Raj Mehta", 40, "Kenji Watanabe"),
            Request("Third-floor printer jams on every duplex job", RequestCategory.Hardware, RequestPriority.Low, "Chloe Martin", 50),
            Request("Unlock Active Directory account for Hannah Lee", RequestCategory.Access, RequestPriority.High, "Hannah Lee", 5, "Grace Okafor", resolvedHoursAgo: 4.5),
            Request("Monitor flickers at desk 12-114", RequestCategory.Hardware, RequestPriority.Low, "Ethan Brooks", 30, "Noah Bennett", resolvedHoursAgo: 6),
            Request("Wi-Fi dead zone near the second-floor kitchen", RequestCategory.Network, RequestPriority.Medium, "Omar Haddad", 40, "Ava Thompson", resolvedHoursAgo: 10),
            Request("Adobe Acrobat Pro license for contract reviews", RequestCategory.Software, RequestPriority.Low, "Maya Johnson", 52, "Ava Thompson", resolvedHoursAgo: 20)
        ];

        await queue.ReplaceAllAsync(requests, cancellationToken);

        var summary = await queue.SummarizeAsync(cancellationToken);
        logger.QueueSeeded(requests.Count, summary.Open, summary.Overdue);
        return new SeedResult(requests.Count, summary);
    }

    /// <summary>
    /// Each round two requests arrive, agents pick up and resolve up to three each, and now and then a resolved request
    /// is reopened. Every call also tries to resolve a request nobody has picked up, which the queue rejects (409).
    /// </summary>
    public async Task<SimulationResult> SimulateAsync(int rounds, CancellationToken cancellationToken)
    {
        using var activity = ServiceDeskTelemetry.ActivitySource.StartActivity("servicedesk.simulate");
        activity?.SetTag("servicedesk.simulation.rounds", rounds);

        var random = Random.Shared;
        int raised = 0, pickedUp = 0, resolved = 0, reopened = 0, rejected = 0;

        for (var round = 0; round < rounds; round++)
        {
            for (var i = 0; i < 2; i++)
            {
                var (title, category) = IncomingRequests[random.Next(IncomingRequests.Length)];
                var priority = random.Next(100) switch
                {
                    < 10 => RequestPriority.Critical,
                    < 35 => RequestPriority.High,
                    < 75 => RequestPriority.Medium,
                    _ => RequestPriority.Low
                };

                // Backdate arrivals across the SLA window so resolution times and SLA breaches look like a real queue.
                var age = ServiceRequest.SlaFor(priority) * (0.05 + random.NextDouble() * 1.05);
                await queue.RaiseAsync(title, Employees[random.Next(Employees.Length)], category, priority,
                    cancellationToken, raisedAt: queue.UtcNow - age);
                raised++;
            }

            foreach (var request in await NextAsync(RequestStatus.New, random.Next(1, 4), cancellationToken))
            {
                await queue.UpdateAsync(request, new(Status: RequestStatus.InProgress, AssignedTo: Agents[random.Next(Agents.Length)]), cancellationToken);
                pickedUp++;
            }

            foreach (var request in await NextAsync(RequestStatus.InProgress, random.Next(1, 4), cancellationToken))
            {
                await queue.UpdateAsync(request, new(Status: RequestStatus.Resolved), cancellationToken);
                resolved++;
            }

            if (random.Next(5) == 0 && await LatestResolvedAsync(cancellationToken) is { } done)
            {
                await queue.UpdateAsync(done, new(Status: RequestStatus.InProgress), cancellationToken);
                reopened++;
            }
        }

        // An agent tries to close a request nobody has picked up; the queue rejects the move.
        if ((await NextAsync(RequestStatus.New, 1, cancellationToken)).FirstOrDefault() is { } unassigned
            && await queue.UpdateAsync(unassigned, new(Status: RequestStatus.Resolved), cancellationToken) is not null)
        {
            rejected++;
        }

        logger.TrafficSimulated(rounds, raised, pickedUp, resolved, reopened, rejected);
        return new SimulationResult(rounds, raised, pickedUp, resolved, reopened, rejected, await queue.SummarizeAsync(cancellationToken));
    }

    // Agents work the most urgent requests first, then the ones closest to breaching their SLA.
    private Task<List<ServiceRequest>> NextAsync(RequestStatus status, int count, CancellationToken cancellationToken) =>
        db.Requests
            .Where(r => r.Status == status)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.DueAt)
            .Take(count)
            .ToListAsync(cancellationToken);

    private Task<ServiceRequest?> LatestResolvedAsync(CancellationToken cancellationToken) =>
        db.Requests
            .Where(r => r.Status == RequestStatus.Resolved)
            .OrderByDescending(r => r.ResolvedAt)
            .FirstOrDefaultAsync(cancellationToken);
}
