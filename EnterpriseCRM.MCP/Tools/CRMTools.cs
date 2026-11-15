using System.ComponentModel;
using EnterpriseCRM.MCP.Services;
using ModelContextProtocol.Server;

namespace EnterpriseCRM.MCP.Tools;

[McpServerToolType]
public class CRMTools
{
    private readonly MockCRMService _crm;

    public CRMTools(MockCRMService crm)
    {
        _crm = crm;
    }

    [McpServerTool]
    [Description("Retrieves customer details using customer ID.")]
    public string GetCustomer(
        [Description("Customer ID, for example C1001")]
        string customerId)
    {
        var customer = _crm.GetCustomer(customerId);

        return customer is null
            ? "Customer not found."
            : System.Text.Json.JsonSerializer.Serialize(customer);
    }

    [McpServerTool]
    [Description("Retrieves open cases belonging to a customer.")]
    public string GetOpenCases(
        [Description("Customer ID, for example C1001")]
        string customerId)
    {
        var cases = _crm.GetOpenCases(customerId);

        return System.Text.Json.JsonSerializer.Serialize(cases);
    }
}