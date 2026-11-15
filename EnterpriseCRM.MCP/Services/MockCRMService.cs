using EnterpriseCRM.MCP.Models;

namespace EnterpriseCRM.MCP.Services;

public class MockCRMService
{
    private readonly List<Customer> customers =
    [
        new Customer
        {
            CustomerId = "C1001",
            Name = "John Smith",
            Email = "john@example.com"
        },
        new Customer
        {
            CustomerId = "C1002",
            Name = "Sarah Jones",
            Email = "sarah@example.com"
        }
    ];

    private readonly List<Case> cases =
    [
        new Case
        {
            CaseId = "CASE001",
            CustomerId = "C1001",
            Subject = "Order not delivered",
            Status = "Open"
        },
        new Case
        {
            CaseId = "CASE002",
            CustomerId = "C1001",
            Subject = "Payment confirmation",
            Status = "In Progress"
        },
        new Case
        {
            CaseId = "CASE003",
            CustomerId = "C1002",
            Subject = "Product damaged",
            Status = "Open"
        }
    ];

    public Customer? GetCustomer(string customerId)
    {
        return customers.FirstOrDefault(
            x => x.CustomerId == customerId);
    }

    public List<Case> GetOpenCases(string customerId)
    {
        return cases
            .Where(x =>
                x.CustomerId == customerId &&
                x.Status != "Closed")
            .ToList();
    }
}